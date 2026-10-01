# Privacy

**NudgeNest collects nothing and sends nothing.**

- The app makes **no internet connections** of any kind. There are no accounts, no analytics, no
  telemetry, no ads and no update checks.
- It never sees what you type. To tell whether you're busy, it only asks Windows *when* the last
  keyboard or mouse input happened, and whether a mouse button or Shift, Ctrl, Alt or the Windows key
  is held down right now. (The one exception: when you pick a custom key, its own little window reads
  the single key you press.)
- It doesn't read, change or inject anything into Roblox's files, memory or process.

## What it stores on your PC

Only two things, in `%APPDATA%\NudgeNest`:

| File | What's in it |
| --- | --- |
| `settings.ini` | Your choices (interval, activity mode, custom key, switches) |
| `log.txt` | Times of nudges, Roblox opening and closing, and problems. It never grows past about 256 KB (older entries move to `log.old.txt` and then are dropped). |

If you turn on **Start with Windows**, it also adds one entry named `NudgeNest` to your user's
`Run` list in the registry. Turning the switch off removes it.

## Removing everything

Right-click the tray icon and choose **Exit**, then delete `NudgeNest.exe` and the
`%APPDATA%\NudgeNest` folder. Turn off **Start with Windows** first if you turned it on.

## The website

[bloxnest.github.io/nudge-nest](https://bloxnest.github.io/nudge-nest/) is a static page hosted by
GitHub Pages. It has no cookies, trackers or outside scripts. GitHub may keep standard server logs; see
[GitHub's privacy statement](https://docs.github.com/site-policy/privacy-policies/github-general-privacy-statement).
