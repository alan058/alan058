"""YouTube Coach: an AI agent that watches your videos and tells you how to get more viewers.

For each video it reads the title, description, tags, view and like counts, the
thumbnail image and the full transcript (what is said, with timestamps). Claude
then reviews it like a YouTube growth coach and writes a report with:

  * the three changes that would help most
  * scores for the hook, title, thumbnail, pacing and search (SEO)
  * better titles and thumbnail ideas
  * a rewritten opening (first 30 seconds)
  * moments where viewers are likely to leave, and how to fix them
  * chapters, a new description and tags you can paste into YouTube Studio
  * ideas for your next videos

It only reads public data and never likes, comments, plays or uploads anything.

Run one of:
    python video_coach.py review <video link or ID> [more links...]
    python video_coach.py channel <@handle, channel link or ID> [--latest 5]
    python video_coach.py check        check config.json and both API keys

Reports are saved as Markdown files in the "reports" folder. See README.md.
"""

import argparse
import base64
import datetime as dt
import json
import os
import pathlib
import re
import sys
import urllib.error
import urllib.parse
import urllib.request
from typing import List, Optional

HERE = pathlib.Path(__file__).resolve().parent
CONFIG_PATH = HERE / "config.json"
DEFAULT_REPORTS_DIR = HERE / "reports"

MODEL = "claude-opus-5-5"
YOUTUBE_API = "https://www.googleapis.com/youtube/v3"
VIDEO_ID_RE = re.compile(r"^[A-Za-z0-9_-]{11}$")
CHANNEL_ID_RE = re.compile(r"^UC[A-Za-z0-9_-]{22}$")


class CoachError(Exception):
    """A problem the user can fix (bad link, missing key, private video...)."""


# ---------------------------------------------------------------- settings


def load_config(path: pathlib.Path = CONFIG_PATH, env=None) -> dict:
    """Read config.json (if present). Environment variables win over the file."""
    env = os.environ if env is None else env
    config = {}
    if path.exists():
        try:
            config = json.loads(path.read_text(encoding="utf-8"))
        except json.JSONDecodeError as e:
            raise CoachError(f"{path.name} is not valid JSON: {e}") from None
    if env.get("YOUTUBE_API_KEY"):
        config["youtube_api_key"] = env["YOUTUBE_API_KEY"]
    if env.get("ANTHROPIC_API_KEY"):
        config["anthropic_api_key"] = env["ANTHROPIC_API_KEY"]
    config.setdefault("transcript_languages", ["en"])
    config.setdefault("channel_goal", "")
    if not config.get("youtube_api_key"):
        raise CoachError(
            "No YouTube API key. Put it in config.json as \"youtube_api_key\" "
            "or set the YOUTUBE_API_KEY environment variable (see README.md, step 1)."
        )
    return config


# ---------------------------------------------------------------- parsing links


def parse_video_id(text: str) -> str:
    """Accept a video ID or any common YouTube link and return the 11-character ID."""
    text = text.strip()
    if VIDEO_ID_RE.match(text):
        return text
    url = urllib.parse.urlparse(text if "://" in text else "https://" + text)
    host = (url.hostname or "").lower().removeprefix("www.").removeprefix("m.")
    parts = [p for p in url.path.split("/") if p]
    candidate = None
    if host == "youtu.be" and parts:
        candidate = parts[0]
    elif host.endswith("youtube.com"):
        query = urllib.parse.parse_qs(url.query)
        if "v" in query:
            candidate = query["v"][0]
        elif len(parts) >= 2 and parts[0] in ("shorts", "live", "embed", "v"):
            candidate = parts[1]
    if candidate and VIDEO_ID_RE.match(candidate):
        return candidate
    raise CoachError(f"Could not find a YouTube video ID in: {text}")


def parse_channel(text: str) -> dict:
    """Return {"id": ...} or {"forHandle": ...} for the YouTube channels API."""
    text = text.strip()
    if CHANNEL_ID_RE.match(text):
        return {"id": text}
    if text.startswith("@"):
        return {"forHandle": text}
    url = urllib.parse.urlparse(text if "://" in text else "https://" + text)
    parts = [p for p in url.path.split("/") if p]
    if parts and parts[0].startswith("@"):
        return {"forHandle": parts[0]}
    if len(parts) >= 2 and parts[0] == "channel" and CHANNEL_ID_RE.match(parts[1]):
        return {"id": parts[1]}
    raise CoachError(
        f"Could not read a channel from: {text}. Use your @handle, "
        "a youtube.com/@handle link, or the channel ID that starts with UC."
    )


def parse_duration(iso: str) -> int:
    """Turn YouTube's ISO 8601 duration (PT1H2M3S) into seconds."""
    m = re.fullmatch(r"P(?:(\d+)D)?T?(?:(\d+)H)?(?:(\d+)M)?(?:(\d+)S)?", iso or "")
    if not m:
        return 0
    d, h, mi, s = (int(x or 0) for x in m.groups())
    return ((d * 24 + h) * 60 + mi) * 60 + s


def clock(seconds: float) -> str:
    seconds = int(seconds)
    h, rest = divmod(seconds, 3600)
    m, s = divmod(rest, 60)
    return f"{h}:{m:02d}:{s:02d}" if h else f"{m}:{s:02d}"


# ---------------------------------------------------------------- YouTube data


class YouTube:
    """Tiny client for the public YouTube Data API v3 (read-only, API key)."""

    def __init__(self, api_key: str, opener=urllib.request.urlopen):
        self.api_key = api_key
        self.opener = opener

    def _get(self, resource: str, **params) -> dict:
        params["key"] = self.api_key
        url = f"{YOUTUBE_API}/{resource}?{urllib.parse.urlencode(params)}"
        try:
            with self.opener(url, timeout=30) as resp:
                return json.loads(resp.read().decode("utf-8"))
        except urllib.error.HTTPError as e:
            try:
                reason = json.loads(e.read().decode("utf-8"))["error"]["message"]
            except Exception:
                reason = e.reason
            raise CoachError(f"YouTube API error ({e.code}): {reason}") from None
        except urllib.error.URLError as e:
            raise CoachError(f"Could not reach YouTube: {e.reason}") from None

    def videos(self, video_ids: List[str]) -> List[dict]:
        found = {}
        for i in range(0, len(video_ids), 50):
            batch = video_ids[i:i + 50]
            data = self._get("videos", part="snippet,contentDetails,statistics",
                             id=",".join(batch))
            for item in data.get("items", []):
                found[item["id"]] = item
        missing = [v for v in video_ids if v not in found]
        if missing:
            raise CoachError(f"Video not found or private: {', '.join(missing)}")
        return [found[v] for v in video_ids]

    def channel(self, ref: dict) -> dict:
        data = self._get("channels", part="snippet,statistics,contentDetails", **ref)
        items = data.get("items") or []
        if not items:
            raise CoachError("Channel not found. Check the @handle or channel ID.")
        return items[0]

    def latest_uploads(self, channel: dict, count: int) -> List[str]:
        uploads = channel["contentDetails"]["relatedPlaylists"]["uploads"]
        data = self._get("playlistItems", part="contentDetails",
                         playlistId=uploads, maxResults=max(1, min(count, 50)))
        return [it["contentDetails"]["videoId"] for it in data.get("items", [])]


def fetch_transcript(video_id: str, languages: List[str]) -> Optional[str]:
    """Return the transcript as timestamped lines, or None if there isn't one."""
    try:
        from youtube_transcript_api import YouTubeTranscriptApi
    except ImportError:
        raise CoachError("Run: pip install -r requirements.txt") from None
    try:
        api = YouTubeTranscriptApi()
        try:
            fetched = api.fetch(video_id, languages=languages)
        except Exception:
            # No captions in the wanted languages: take whatever the video has.
            fetched = next(iter(api.list(video_id))).fetch()
        snippets = [(s.start, s.text) for s in fetched]
    except Exception as e:
        print(f"  (no transcript: {type(e).__name__}; reviewing without it)")
        return None
    return format_transcript(snippets)


def format_transcript(snippets, every_seconds: int = 15) -> str:
    """Join caption snippets into one line per ~15 seconds: "[1:05] words words"."""
    lines, start, words = [], None, []
    for t, text in snippets:
        text = " ".join(text.split())
        if not text:
            continue
        if start is None:
            start = t
        elif t - start >= every_seconds:
            lines.append(f"[{clock(start)}] {' '.join(words)}")
            start, words = t, []
        words.append(text)
    if words:
        lines.append(f"[{clock(start)}] {' '.join(words)}")
    return "\n".join(lines)


def fetch_thumbnail(video: dict, opener=urllib.request.urlopen) -> Optional[dict]:
    """Download the largest thumbnail and return it as a Claude image block."""
    thumbs = video["snippet"].get("thumbnails", {})
    for size in ("maxres", "standard", "high", "medium", "default"):
        if size in thumbs:
            try:
                with opener(thumbs[size]["url"], timeout=30) as resp:
                    data = resp.read()
                    headers = getattr(resp, "headers", None)
                    media_type = headers.get_content_type() if headers else "image/jpeg"
                if media_type not in ("image/jpeg", "image/png", "image/webp", "image/gif"):
                    media_type = "image/jpeg"
                return {"type": "image", "source": {
                    "type": "base64", "media_type": media_type,
                    "data": base64.standard_b64encode(data).decode("ascii")}}
            except Exception:
                continue
    return None


def video_facts(video: dict, now: Optional[dt.datetime] = None) -> dict:
    """The numbers and text Claude needs, in a plain dictionary."""
    now = now or dt.datetime.now(dt.timezone.utc)
    sn, st = video["snippet"], video.get("statistics", {})
    published = dt.datetime.fromisoformat(sn["publishedAt"].replace("Z", "+00:00"))
    days = max((now - published).total_seconds() / 86400, 1 / 24)
    views = int(st.get("viewCount", 0))
    likes = int(st["likeCount"]) if "likeCount" in st else None
    comments = int(st["commentCount"]) if "commentCount" in st else None
    seconds = parse_duration(video.get("contentDetails", {}).get("duration", ""))
    return {
        "id": video["id"],
        "url": f"https://www.youtube.com/watch?v={video['id']}",
        "title": sn.get("title", ""),
        "channel": sn.get("channelTitle", ""),
        "published": published.date().isoformat(),
        "days_since_published": round(days, 1),
        "length": clock(seconds),
        "is_short": 0 < seconds <= 180,
        "views": views,
        "views_per_day": round(views / days, 1),
        "likes": likes,
        "comments": comments,
        "like_rate_percent": round(100 * likes / views, 2) if likes is not None and views else None,
        "description": sn.get("description", ""),
        "tags": sn.get("tags", []),
    }


# ---------------------------------------------------------------- what Claude returns

from pydantic import BaseModel, Field  # noqa: E402  (after the stdlib helpers on purpose)


class Score(BaseModel):
    area: str = Field(description="One of: Hook, Title, Thumbnail, Pacing, Search (SEO)")
    score: int = Field(description="1 (poor) to 10 (excellent)")
    why: str


class Moment(BaseModel):
    timestamp: str = Field(description="Like 2:15, taken from the transcript")
    problem: str = Field(description="Why viewers may click away here")
    fix: str


class Chapter(BaseModel):
    timestamp: str = Field(description="Like 0:00; the first chapter must be 0:00")
    title: str


class VideoReview(BaseModel):
    summary: str = Field(description="Two or three sentences: what the video is and how it is doing")
    top_changes: List[str] = Field(description="The three changes that would bring the most new viewers, most important first")
    scores: List[Score]
    title_ideas: List[str] = Field(description="Five better titles, under 70 characters each")
    thumbnail_feedback: str
    thumbnail_ideas: List[str] = Field(description="Three concrete thumbnail concepts")
    hook_feedback: str = Field(description="How well the first 30 seconds hold attention")
    hook_rewrite: str = Field(description="A rewritten opening script for the first 30 seconds, in the creator's voice")
    drop_off_moments: List[Moment]
    chapters: List[Chapter] = Field(description="Empty if the video is a Short or under 3 minutes")
    description: str = Field(description="A new YouTube description ready to paste, with the chapters at the end")
    tags: List[str] = Field(description="10 to 15 search tags")
    next_video_ideas: List[str] = Field(description="Three follow-up video ideas for viewers of this one")


class ChannelPlan(BaseModel):
    summary: str
    what_works: List[str] = Field(description="Patterns shared by the best-performing videos")
    what_hurts: List[str] = Field(description="Patterns shared by the weakest videos")
    action_plan: List[str] = Field(description="Five steps for the next month, most important first")
    next_video_ideas: List[str] = Field(description="Five video ideas with a working title each")


SYSTEM_PROMPT = """You are an experienced YouTube growth coach. A creator has asked you to watch \
one of their videos and tell them, honestly and specifically, how to reach more viewers.

You get the video's public numbers, title, description, tags, thumbnail and a transcript with \
timestamps. Use the numbers for context: views per day and like rate show how the video is doing \
relative to its age. When the transcript is missing, say so and base pacing and hook advice on \
the title, description and thumbnail only.

Ground every point in this video: quote or point to the timestamp, the words in the title or \
what is in the thumbnail. Prefer changes the creator can make today in YouTube Studio (title, \
thumbnail, description, chapters, pinned comment) over advice that needs a re-shoot, and say \
which is which. Recommend only honest, policy-compliant tactics: no clickbait that the video \
does not deliver, no fake engagement, no misleading thumbnails. Write in the language of the video."""


def build_video_message(facts: dict, transcript: Optional[str], thumbnail: Optional[dict],
                        goal: str = "") -> list:
    info = {k: v for k, v in facts.items() if k not in ("description", "tags")}
    parts = [
        "Video details:\n" + json.dumps(info, indent=2, ensure_ascii=False),
        "Description:\n" + (facts["description"] or "(empty)"),
        "Tags: " + (", ".join(facts["tags"]) or "(none)"),
    ]
    if goal:
        parts.append("The creator's goal for the channel: " + goal)
    parts.append("Transcript:\n" + (transcript or "(no transcript available)"))
    content = []
    if thumbnail:
        content.append(thumbnail)
        parts.insert(0, "The image above is the video's current thumbnail.")
    content.append({"type": "text", "text": "\n\n".join(parts)})
    return [{"role": "user", "content": content}]


def ask_claude(client, messages: list, output_format, effort: str = "high"):
    """One structured request to Claude, falling back to another model if declined."""
    import anthropic

    try:
        response = client.beta.messages.parse(
            model=MODEL,
            max_tokens=16000,
            system=SYSTEM_PROMPT,
            messages=messages,
            output_format=output_format,
            output_config={"effort": effort},
            betas=["server-side-fallback-2026-07-01"],
            fallbacks="default",
        )
    except anthropic.AuthenticationError:
        raise CoachError("Claude rejected the API key. Check anthropic_api_key in config.json.") from None
    except anthropic.PermissionDeniedError as e:
        raise CoachError(f"Claude API permission problem: {e.message}") from None
    except anthropic.RateLimitError:
        raise CoachError("Claude API rate limit reached. Wait a minute and try again.") from None
    except anthropic.APIStatusError as e:
        raise CoachError(f"Claude API error ({e.status_code}): {e.message}") from None
    except anthropic.APIConnectionError:
        raise CoachError("Could not reach the Claude API. Check your internet connection.") from None

    if response.stop_reason == "refusal":
        raise CoachError("Claude declined to review this video.")
    if response.stop_reason == "max_tokens" or response.parsed_output is None:
        raise CoachError("Claude's answer was cut off or incomplete. Try again.")
    return response.parsed_output


# ---------------------------------------------------------------- reports


def _num(n) -> str:
    return "hidden" if n is None else f"{n:,}"


def render_video_report(facts: dict, review: VideoReview, had_transcript: bool) -> str:
    out = [
        f"# {facts['title']}",
        "",
        f"[Watch on YouTube]({facts['url']}) · {facts['channel']} · published {facts['published']} · {facts['length']}",
        "",
        "| Views | Views per day | Likes | Like rate | Comments |",
        "|---|---|---|---|---|",
        f"| {_num(facts['views'])} | {facts['views_per_day']:,} | {_num(facts['likes'])} | "
        f"{'hidden' if facts['like_rate_percent'] is None else str(facts['like_rate_percent']) + '%'} | "
        f"{_num(facts['comments'])} |",
        "",
    ]
    if not had_transcript:
        out += ["> No transcript was available, so hook and pacing advice is based on the "
                "title, description and thumbnail only.", ""]
    out += ["## Summary", "", review.summary, "", "## Do these three things first", ""]
    out += [f"{i}. {c}" for i, c in enumerate(review.top_changes, 1)]
    out += ["", "## Scores", "", "| Area | Score | Why |", "|---|---|---|"]
    out += [f"| {s.area} | {s.score}/10 | {s.why} |" for s in review.scores]
    out += ["", "## Better titles", ""] + [f"- {t}" for t in review.title_ideas]
    out += ["", "## Thumbnail", "", review.thumbnail_feedback, "", "Ideas:", ""]
    out += [f"- {t}" for t in review.thumbnail_ideas]
    out += ["", "## The first 30 seconds", "", review.hook_feedback, "",
            "**Suggested opening:**", "", "> " + review.hook_rewrite.replace("\n", "\n> ")]
    if review.drop_off_moments:
        out += ["", "## Where viewers may leave", "", "| Time | Problem | Fix |", "|---|---|---|"]
        out += [f"| {m.timestamp} | {m.problem} | {m.fix} |" for m in review.drop_off_moments]
    if review.chapters:
        out += ["", "## Chapters", "", "```"] + [f"{c.timestamp} {c.title}" for c in review.chapters] + ["```"]
    out += ["", "## New description (paste into YouTube Studio)", "", "```", review.description, "```",
            "", "## Tags", "", "```", ", ".join(review.tags), "```",
            "", "## Next video ideas", ""] + [f"- {i}" for i in review.next_video_ideas]
    return "\n".join(out) + "\n"


def render_channel_report(channel: dict, rows: List[dict], plan: ChannelPlan) -> str:
    sn, st = channel["snippet"], channel.get("statistics", {})
    subs = "hidden" if st.get("hiddenSubscriberCount") else _num(int(st.get("subscriberCount", 0)))
    out = [
        f"# Channel plan: {sn.get('title', '')}",
        "",
        f"{subs} subscribers · {_num(int(st.get('viewCount', 0)))} total views · "
        f"{_num(int(st.get('videoCount', 0)))} videos",
        "",
        "## Videos reviewed", "",
        "| Video | Published | Views | Views per day | Report |", "|---|---|---|---|---|",
    ]
    for r in rows:
        f = r["facts"]
        out.append(f"| {f['title']} | {f['published']} | {_num(f['views'])} | "
                   f"{f['views_per_day']:,} | [{r['file']}]({r['file']}) |")
    out += ["", "## Summary", "", plan.summary,
            "", "## What works", ""] + [f"- {x}" for x in plan.what_works]
    out += ["", "## What holds videos back", ""] + [f"- {x}" for x in plan.what_hurts]
    out += ["", "## Plan for the next month", ""] + [f"{i}. {x}" for i, x in enumerate(plan.action_plan, 1)]
    out += ["", "## Next video ideas", ""] + [f"- {x}" for x in plan.next_video_ideas]
    return "\n".join(out) + "\n"


def safe_filename(text: str, video_id: str) -> str:
    slug = re.sub(r"[^A-Za-z0-9]+", "-", text).strip("-").lower()[:50] or "video"
    return f"{dt.date.today().isoformat()}_{slug}_{video_id}.md"


# ---------------------------------------------------------------- the agent


def make_claude(config: dict):
    import anthropic

    if config.get("anthropic_api_key"):
        return anthropic.Anthropic(api_key=config["anthropic_api_key"])
    return anthropic.Anthropic()  # ANTHROPIC_API_KEY or an `ant auth login` profile


def review_video(video: dict, config: dict, claude, transcript_fn=fetch_transcript,
                 thumbnail_fn=fetch_thumbnail):
    facts = video_facts(video)
    print(f"Watching: {facts['title']}")
    transcript = transcript_fn(facts["id"], config["transcript_languages"])
    thumbnail = thumbnail_fn(video)
    print("  asking Claude for a review...")
    review = ask_claude(claude, build_video_message(facts, transcript, thumbnail,
                                                    config.get("channel_goal", "")), VideoReview)
    return facts, review, transcript is not None


def save(out_dir: pathlib.Path, name: str, text: str) -> pathlib.Path:
    out_dir.mkdir(parents=True, exist_ok=True)
    path = out_dir / name
    path.write_text(text, encoding="utf-8")
    print(f"  saved {path}")
    return path


def cmd_review(args, config, youtube, claude):
    ids = [parse_video_id(v) for v in args.videos]
    for video in youtube.videos(ids):
        facts, review, had = review_video(video, config, claude)
        save(args.out, safe_filename(facts["title"], facts["id"]),
             render_video_report(facts, review, had))


def cmd_channel(args, config, youtube, claude):
    channel = youtube.channel(parse_channel(args.channel))
    print(f"Channel: {channel['snippet']['title']}")
    ids = youtube.latest_uploads(channel, args.latest)
    if not ids:
        raise CoachError("This channel has no public videos yet.")
    rows = []
    for video in youtube.videos(ids):
        facts, review, had = review_video(video, config, claude)
        name = safe_filename(facts["title"], facts["id"])
        save(args.out, name, render_video_report(facts, review, had))
        rows.append({"facts": facts, "review": review, "file": name})

    print("Comparing the videos to build a channel plan...")
    digest = [{
        "title": r["facts"]["title"], "published": r["facts"]["published"],
        "length": r["facts"]["length"], "views": r["facts"]["views"],
        "views_per_day": r["facts"]["views_per_day"],
        "like_rate_percent": r["facts"]["like_rate_percent"],
        "review_summary": r["review"].summary,
        "scores": {s.area: s.score for s in r["review"].scores},
        "top_changes": r["review"].top_changes,
    } for r in rows]
    stats = channel.get("statistics", {})
    text = ("Here are reviews of this channel's latest videos. Compare them, find what the "
            "stronger videos do differently, and write a plan for the channel.\n\n"
            f"Channel: {channel['snippet']['title']}\n"
            f"Subscribers: {stats.get('subscriberCount', 'hidden')}\n"
            + (f"Creator's goal: {config['channel_goal']}\n" if config.get("channel_goal") else "")
            + "\n" + json.dumps(digest, indent=2, ensure_ascii=False))
    plan = ask_claude(claude, [{"role": "user", "content": text}], ChannelPlan)
    handle = re.sub(r"[^A-Za-z0-9]+", "-", channel["snippet"]["title"]).strip("-").lower() or "channel"
    save(args.out, f"{dt.date.today().isoformat()}_channel-plan_{handle}.md",
         render_channel_report(channel, rows, plan))


def cmd_check(config, youtube, claude):
    youtube.videos(["jNQXAC9IVRw"])  # "Me at the zoo", the first YouTube video
    print("YouTube API key: OK")
    try:
        claude.models.retrieve(MODEL)
    except Exception as e:
        raise CoachError(f"Claude API key problem: {e}") from None
    print("Claude API key: OK")


def main(argv=None) -> int:
    parser = argparse.ArgumentParser(description="AI coach that reviews YouTube videos to grow your channel.")
    parser.add_argument("--out", type=pathlib.Path, default=DEFAULT_REPORTS_DIR,
                        help="folder for the reports (default: reports)")
    sub = parser.add_subparsers(dest="command", required=True)
    r = sub.add_parser("review", help="review one or more videos")
    r.add_argument("videos", nargs="+", help="video links or IDs")
    c = sub.add_parser("channel", help="review a channel's latest videos and write a plan")
    c.add_argument("channel", help="@handle, youtube.com/@handle link, or channel ID (UC...)")
    c.add_argument("--latest", type=int, default=5, help="how many recent videos (default 5, max 50)")
    sub.add_parser("check", help="check config.json and both API keys")
    args = parser.parse_args(argv)

    try:
        config = load_config()
        youtube = YouTube(config["youtube_api_key"])
        claude = make_claude(config)
        if args.command == "review":
            cmd_review(args, config, youtube, claude)
        elif args.command == "channel":
            cmd_channel(args, config, youtube, claude)
        else:
            cmd_check(config, youtube, claude)
    except CoachError as e:
        print(f"Error: {e}", file=sys.stderr)
        return 1
    except KeyboardInterrupt:
        return 130
    return 0


if __name__ == "__main__":
    sys.exit(main())
