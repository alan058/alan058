"""Tests for YouTube Coach. They need no API keys and never touch the internet."""

import datetime as dt
import io
import json
import pathlib
import sys
import types
import urllib.error

import pytest

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent.parent))
import video_coach as vc  # noqa: E402

VIDEO = {
    "id": "abcdefghijk",
    "snippet": {
        "title": "How I Bake Bread in 10 Minutes!",
        "channelTitle": "Quick Kitchen",
        "publishedAt": "2026-10-25T12:00:00Z",
        "description": "Easy bread.",
        "tags": ["bread", "baking"],
        "thumbnails": {"high": {"url": "https://i.ytimg.com/vi/abcdefghijk/hqdefault.jpg"}},
    },
    "contentDetails": {"duration": "PT8M30S"},
    "statistics": {"viewCount": "1000", "likeCount": "50", "commentCount": "7"},
}


def make_review():
    return vc.VideoReview(
        summary="A short bread tutorial.",
        top_changes=["Shorter title", "Brighter thumbnail", "Start with the finished loaf"],
        scores=[vc.Score(area="Hook", score=6, why="Slow intro")],
        title_ideas=["10-Minute Bread Anyone Can Make"],
        thumbnail_feedback="Too dark.",
        thumbnail_ideas=["Close-up of the loaf"],
        hook_feedback="The first 20 seconds are a greeting.",
        hook_rewrite="This loaf took 10 minutes.\nHere's how.",
        drop_off_moments=[vc.Moment(timestamp="2:15", problem="Long pause", fix="Cut it")],
        chapters=[vc.Chapter(timestamp="0:00", title="Intro")],
        description="New description",
        tags=["bread"],
        next_video_ideas=["No-knead pizza"],
    )


# ---------------------------------------------------------------- links


@pytest.mark.parametrize("text", [
    "abcdefghijk",
    "https://www.youtube.com/watch?v=abcdefghijk",
    "https://youtube.com/watch?v=abcdefghijk&t=42s",
    "youtube.com/watch?v=abcdefghijk",
    "https://m.youtube.com/watch?v=abcdefghijk",
    "https://youtu.be/abcdefghijk?si=xyz",
    "https://www.youtube.com/shorts/abcdefghijk",
    "https://www.youtube.com/live/abcdefghijk",
    "https://www.youtube.com/embed/abcdefghijk",
])
def test_parse_video_id_accepts_common_links(text):
    assert vc.parse_video_id(text) == "abcdefghijk"


@pytest.mark.parametrize("text", ["", "hello", "https://example.com/watch?v=abcdefghijk",
                                  "https://youtube.com/watch?v=short"])
def test_parse_video_id_rejects_other_text(text):
    with pytest.raises(vc.CoachError):
        vc.parse_video_id(text)


def test_parse_channel():
    cid = "UC" + "a" * 22
    assert vc.parse_channel("@QuickKitchen") == {"forHandle": "@QuickKitchen"}
    assert vc.parse_channel("https://www.youtube.com/@QuickKitchen/videos") == {"forHandle": "@QuickKitchen"}
    assert vc.parse_channel(cid) == {"id": cid}
    assert vc.parse_channel(f"https://youtube.com/channel/{cid}") == {"id": cid}
    with pytest.raises(vc.CoachError):
        vc.parse_channel("https://youtube.com/c/oldstyle")


@pytest.mark.parametrize("iso,seconds", [("PT8M30S", 510), ("PT1H2M3S", 3723), ("PT45S", 45),
                                         ("P1DT1S", 86401), ("", 0), ("junk", 0)])
def test_parse_duration(iso, seconds):
    assert vc.parse_duration(iso) == seconds


def test_clock():
    assert vc.clock(65) == "1:05"
    assert vc.clock(3723) == "1:02:03"


# ---------------------------------------------------------------- data


def test_format_transcript_groups_lines_every_15_seconds():
    snippets = [(0.0, "hi  there"), (4.0, "welcome"), (16.0, "step one"), (20.5, ""), (31.0, "done\n")]
    assert vc.format_transcript(snippets) == "[0:00] hi there welcome\n[0:16] step one\n[0:31] done"
    assert vc.format_transcript([]) == ""


def test_video_facts():
    now = dt.datetime(2026, 11, 4, 12, 0, tzinfo=dt.timezone.utc)
    f = vc.video_facts(VIDEO, now=now)
    assert f["views_per_day"] == 100.0
    assert f["like_rate_percent"] == 5.0
    assert f["length"] == "8:30"
    assert f["is_short"] is False
    assert f["url"] == "https://www.youtube.com/watch?v=abcdefghijk"


def test_video_facts_with_hidden_likes():
    video = json.loads(json.dumps(VIDEO))
    video["statistics"] = {"viewCount": "0"}
    f = vc.video_facts(video)
    assert f["likes"] is None and f["like_rate_percent"] is None


def test_load_config_env_overrides_file(tmp_path):
    path = tmp_path / "config.json"
    path.write_text(json.dumps({"youtube_api_key": "file-key"}), encoding="utf-8")
    assert vc.load_config(path, env={})["youtube_api_key"] == "file-key"
    cfg = vc.load_config(path, env={"YOUTUBE_API_KEY": "env-key"})
    assert cfg["youtube_api_key"] == "env-key"
    assert cfg["transcript_languages"] == ["en"]


def test_load_config_needs_youtube_key(tmp_path):
    with pytest.raises(vc.CoachError, match="YouTube API key"):
        vc.load_config(tmp_path / "missing.json", env={})
    bad = tmp_path / "config.json"
    bad.write_text("{not json", encoding="utf-8")
    with pytest.raises(vc.CoachError, match="not valid JSON"):
        vc.load_config(bad, env={})


# ---------------------------------------------------------------- YouTube API (faked)


class FakeResponse(io.BytesIO):
    def __init__(self, body: bytes, content_type="application/json"):
        super().__init__(body)
        self.headers = types.SimpleNamespace(get_content_type=lambda: content_type)

    def __enter__(self):
        return self

    def __exit__(self, *a):
        self.close()


def fake_opener(routes, calls):
    def opener(url, timeout=None):
        calls.append(url)
        for prefix, body in routes.items():
            if url.startswith(prefix):
                if isinstance(body, Exception):
                    raise body
                return FakeResponse(json.dumps(body).encode())
        raise AssertionError(f"unexpected URL {url}")
    return opener


def test_youtube_videos_keeps_order_and_reports_missing():
    calls = []
    other = dict(VIDEO, id="zzzzzzzzzzz")
    yt = vc.YouTube("KEY", fake_opener({vc.YOUTUBE_API + "/videos": {"items": [other, VIDEO]}}, calls))
    assert [v["id"] for v in yt.videos(["abcdefghijk", "zzzzzzzzzzz"])] == ["abcdefghijk", "zzzzzzzzzzz"]
    assert "key=KEY" in calls[0]
    with pytest.raises(vc.CoachError, match="private"):
        yt.videos(["abcdefghijk", "missingmiss"])


def test_youtube_channel_and_uploads():
    calls = []
    channel = {"snippet": {"title": "Quick Kitchen"},
               "contentDetails": {"relatedPlaylists": {"uploads": "UUxyz"}}}
    yt = vc.YouTube("KEY", fake_opener({
        vc.YOUTUBE_API + "/channels": {"items": [channel]},
        vc.YOUTUBE_API + "/playlistItems": {"items": [{"contentDetails": {"videoId": "abcdefghijk"}}]},
    }, calls))
    ch = yt.channel({"forHandle": "@QuickKitchen"})
    assert "forHandle=%40QuickKitchen" in calls[0]
    assert yt.latest_uploads(ch, 3) == ["abcdefghijk"]
    assert "playlistId=UUxyz" in calls[1] and "maxResults=3" in calls[1]


def test_youtube_http_error_message_is_readable():
    err = urllib.error.HTTPError("u", 403, "Forbidden", {},
                                 io.BytesIO(b'{"error": {"message": "API key not valid."}}'))
    yt = vc.YouTube("KEY", fake_opener({vc.YOUTUBE_API: err}, []))
    with pytest.raises(vc.CoachError, match="403.*API key not valid"):
        yt.videos(["abcdefghijk"])


def test_fetch_thumbnail_returns_base64_image_block():
    def opener(url, timeout=None):
        assert url.endswith("hqdefault.jpg")
        return FakeResponse(b"\xff\xd8jpeg", content_type="image/jpeg")
    block = vc.fetch_thumbnail(VIDEO, opener)
    assert block["type"] == "image"
    assert block["source"]["media_type"] == "image/jpeg"
    assert block["source"]["data"] == "/9hqcGVn"


def test_fetch_thumbnail_gives_up_quietly():
    def opener(url, timeout=None):
        raise OSError("offline")
    assert vc.fetch_thumbnail(VIDEO, opener) is None


# ---------------------------------------------------------------- Claude (faked)


class FakeMessages:
    def __init__(self, outputs, stop_reason="end_turn"):
        self.outputs = list(outputs)
        self.stop_reason = stop_reason
        self.requests = []

    def parse(self, **kwargs):
        self.requests.append(kwargs)
        return types.SimpleNamespace(stop_reason=self.stop_reason, parsed_output=self.outputs.pop(0))


def fake_claude(outputs, stop_reason="end_turn"):
    messages = FakeMessages(outputs, stop_reason)
    return types.SimpleNamespace(beta=types.SimpleNamespace(messages=messages)), messages


def test_build_video_message_puts_thumbnail_first():
    facts = vc.video_facts(VIDEO)
    image = {"type": "image", "source": {}}
    msg = vc.build_video_message(facts, "[0:00] hi", image, goal="10k subs")
    content = msg[0]["content"]
    assert content[0] is image
    text = content[1]["text"]
    assert "thumbnail" in text and "[0:00] hi" in text and "10k subs" in text and "bread, baking" in text
    no_extras = vc.build_video_message(facts, None, None)[0]["content"]
    assert len(no_extras) == 1 and "(no transcript available)" in no_extras[0]["text"]


def test_ask_claude_request_shape():
    claude, messages = fake_claude([make_review()])
    out = vc.ask_claude(claude, [{"role": "user", "content": "x"}], vc.VideoReview)
    assert out.summary == "A short bread tutorial."
    req = messages.requests[0]
    assert req["model"] == "claude-opus-5-5"
    assert req["output_format"] is vc.VideoReview
    assert req["fallbacks"] == "default"
    assert req["betas"] == ["server-side-fallback-2026-07-01"]
    assert "thinking" not in req and "temperature" not in req


@pytest.mark.parametrize("stop,output,match", [("refusal", None, "declined"),
                                               ("max_tokens", None, "cut off")])
def test_ask_claude_bad_endings(stop, output, match):
    claude, _ = fake_claude([output], stop_reason=stop)
    with pytest.raises(vc.CoachError, match=match):
        vc.ask_claude(claude, [], vc.VideoReview)


def test_render_video_report():
    facts = vc.video_facts(VIDEO)
    md = vc.render_video_report(facts, make_review(), had_transcript=False)
    assert md.startswith("# How I Bake Bread in 10 Minutes!")
    assert "No transcript was available" in md
    assert "1. Shorter title" in md
    assert "| Hook | 6/10 | Slow intro |" in md
    assert "> This loaf took 10 minutes.\n> Here's how." in md
    assert "| 2:15 | Long pause | Cut it |" in md
    assert "0:00 Intro" in md


def test_safe_filename():
    name = vc.safe_filename("How I Bake Bread in 10 Minutes! 🍞", "abcdefghijk")
    assert name.endswith("_how-i-bake-bread-in-10-minutes_abcdefghijk.md")
    assert vc.safe_filename("🍞🍞", "abcdefghijk").endswith("_video_abcdefghijk.md")


def test_channel_command_end_to_end(tmp_path, monkeypatch):
    channel = {"snippet": {"title": "Quick Kitchen"},
               "statistics": {"subscriberCount": "1200", "viewCount": "50000", "videoCount": "30"},
               "contentDetails": {"relatedPlaylists": {"uploads": "UUxyz"}}}
    youtube = types.SimpleNamespace(
        channel=lambda ref: channel,
        latest_uploads=lambda ch, n: ["abcdefghijk"],
        videos=lambda ids: [VIDEO],
    )
    plan = vc.ChannelPlan(summary="Growing.", what_works=["Food close-ups"], what_hurts=["Long intros"],
                          action_plan=["Post weekly"], next_video_ideas=["Focaccia"])
    claude, messages = fake_claude([make_review(), plan])
    monkeypatch.setattr(vc, "fetch_transcript", lambda vid, langs: "[0:00] hi")
    monkeypatch.setattr(vc, "fetch_thumbnail", lambda video: None)
    args = types.SimpleNamespace(channel="@QuickKitchen", latest=1, out=tmp_path)
    config = {"transcript_languages": ["en"], "channel_goal": "10k subs"}

    vc.cmd_channel(args, config, youtube, claude)

    files = sorted(p.name for p in tmp_path.iterdir())
    assert len(files) == 2
    plan_md = (tmp_path / [f for f in files if "channel-plan" in f][0]).read_text(encoding="utf-8")
    assert "# Channel plan: Quick Kitchen" in plan_md and "1. Post weekly" in plan_md
    assert "1,200 subscribers" in plan_md
    assert "10k subs" in messages.requests[1]["messages"][0]["content"]


def test_main_reports_config_errors(tmp_path, monkeypatch, capsys):
    real_load = vc.load_config
    monkeypatch.setattr(vc, "load_config", lambda: real_load(tmp_path / "none.json", env={}))
    assert vc.main(["review", "abcdefghijk"]) == 1
    assert "No YouTube API key" in capsys.readouterr().err


def test_pydantic_models_produce_a_json_schema():
    # The SDK sends this schema to Claude as the required output format.
    schema = vc.VideoReview.model_json_schema()
    assert set(schema["required"]) == set(vc.VideoReview.model_fields)
