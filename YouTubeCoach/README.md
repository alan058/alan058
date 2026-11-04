# YouTube Coach: an AI agent that watches your videos and tells you how to get more viewers

Give it a video link or your channel. For each video it reads:

* the **title, description and tags**
* the **thumbnail** (Claude looks at the image)
* the **full transcript** with timestamps (everything that is said in the video)
* the public **views, likes and comments**, and how many views it gets per day

Claude then reviews it like a YouTube growth coach and writes a report you can
open in any Markdown viewer, VS Code or Notepad:

| Section | What you get |
|---|---|
| **Do these three things first** | The changes most likely to bring in new viewers |
| **Scores** | Hook, title, thumbnail, pacing and search (SEO), each out of 10, with reasons |
| **Better titles** | Five alternatives under 70 characters |
| **Thumbnail** | What works and what doesn't, plus three new ideas |
| **The first 30 seconds** | Feedback on the opening and a rewritten opening script |
| **Where viewers may leave** | Timestamps where people are likely to click away, and how to fix each one |
| **Chapters, description, tags** | Ready to paste into YouTube Studio |
| **Next video ideas** | Follow-up videos for the same viewers |

In **channel** mode it reviews your latest videos, compares them, and adds a
**channel plan**: what your best videos have in common, what holds the others
back, and a five-step plan for the next month.

It only *reads* public data. It never plays, likes, comments on or uploads
anything, so it can't get your channel into trouble. (Bots that play videos to
raise the view count break YouTube's rules: YouTube removes those views and can
penalise the channel. This tool grows views the honest way, by making the
videos better.)

## 1. Get the two keys (one time)

**YouTube Data API key** (free):

1. Go to [console.cloud.google.com](https://console.cloud.google.com), sign in, and create a project.
2. **APIs & Services → Library**, search **YouTube Data API v3**, click **Enable**.
3. **APIs & Services → Credentials → Create credentials → API key.** Copy it.
   You can restrict the key to "YouTube Data API v3".

The free quota (10,000 units a day) covers thousands of reviews: each video costs about 1–3 units.

**Claude API key:**

1. Go to [platform.claude.com](https://platform.claude.com) and sign in.
2. Add credit under **Billing**, then create a key under **API keys**.

Each review is one Claude request. Cost depends on the video's length; a
10-minute video costs a few cents.

## 2. Set it up

1. Install **Python 3.10 or newer** from [python.org](https://www.python.org/downloads/).
2. Copy `config.example.json` to `config.json` and fill in:
   * `youtube_api_key` and `anthropic_api_key`
   * `transcript_languages`: the caption languages to try first, such as `["en"]` or `["hi", "en"]`
   * `channel_goal` (optional): one sentence about your channel and goal. The advice gets more specific.
3. Install the packages:

   ```
   pip install -r requirements.txt
   ```

4. Check both keys work:

   ```
   python video_coach.py check
   ```

You can set `YOUTUBE_API_KEY` and `ANTHROPIC_API_KEY` as environment variables
instead of putting them in `config.json`.

## 3. Use it

**On Windows, the easy way:** double-click `review.bat` and paste a video link or your
@handle. The reports folder opens when it's done.

**From a terminal:**

```
python video_coach.py review https://youtu.be/VIDEO_ID
python video_coach.py review LINK1 LINK2 LINK3
python video_coach.py channel @YourHandle
python video_coach.py channel @YourHandle --latest 10
```

Reports are saved in the `reports` folder (change it with `--out`), one file per
video plus `..._channel-plan_....md` in channel mode.

## Good to know

* **No transcript?** Some videos have captions turned off. The review still runs
  using the title, description, tags and thumbnail, and the report says so.
* Long videos are sent whole; nothing is cut out of the transcript.
* The model is Claude Opus 5.5 (`MODEL` at the top of `video_coach.py`). If
  Claude declines a request, the API passes it to a fallback model automatically.
* Your keys stay in `config.json`, which git ignores.

## Tests

```
pip install -r requirements.txt pytest
python -m pytest -v tests
```

The tests use fake YouTube and Claude responses, so they need no keys or internet.
