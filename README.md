# ProcWatch: see what an app does in the background

ProcWatch is a small offline desktop app for Windows 11. It works like a focused
Task Manager for one app: type a name such as `claude`, press **Start**, and it
tracks every matching process and every child process it starts.

It reads everything from your own PC through Windows' process APIs (via
[psutil](https://github.com/giampaolo/psutil)) and works fully offline:
no internet is needed to install, build or run it.

## What it shows

| Area | What you see |
|---|---|
| **Summary tiles** | Process count, total CPU, total memory, peak memory, commit size, disk read/write speed, threads, handles |
| **Charts** | Live CPU, memory (memory and working set) and disk I/O (read and write) for the whole group |
| **Processes** | Every tracked process as a tree (parent and children) or a flat list. Click a column header to sort. Right-click for details, copy command line, open file location, or end the process |
| **Open files** | Every file each process has open right now, with a filter box |
| **Network** | Every TCP/UDP connection: local and remote address and state |
| **Activity log** | A timeline of what changed: processes started or exited, files opened or closed, connections opened or closed, and memory spikes above a threshold you set |
| **Details** | Full info for the selected process: executable, command line, user, start time, memory breakdown, I/O totals, its own memory chart, files and connections |
| **Record to CSV** | Saves every sample and every event to two CSV files you can open in Excel later |

### How the numbers compare to Task Manager

* **Memory** is the *private working set*, the same figure Task Manager's
  Memory column shows.
* **Working set** also counts memory shared with other processes (DLLs and
  so on), so it is higher.
* **Commit** is the private memory the process has reserved, in RAM plus the
  page file (Task Manager > Details > "Commit size").
* **CPU** is a percentage of the whole machine, like Task Manager.
* **Disk read/write** comes from Windows' per-process I/O counters, which count
  every read and write call (files, pipes, some devices). It can be higher
  than Task Manager's Disk column, which counts only physical disk traffic.
  **Other I/O** is control operations that are neither reads nor writes.

## Strictly offline

* The app never opens a network connection. It has no updates, telemetry,
  accounts or web views. The **Network** tab only *reads* the list of
  connections other apps have open; ProcWatch makes none itself.
* `tests/test_offline.py` fails if anyone adds code that imports a networking
  library or opens a socket.
* The one library it needs (psutil) is bundled in `wheels/`, along with the
  tools to build the `.exe`. Installing and building never touch the internet.

## Run it (pick one)

### 1. ProcWatch.exe (easiest, no Python needed)

Open the **Actions** tab of this repository, pick the latest
**Build Windows app** run, and download **ProcWatch-windows**. Unzip it and
double-click `ProcWatch.exe`. The `.exe` alone is the whole app. Copy it
anywhere, even to a PC with no internet. The zip also holds the source and the
scripts below.

Windows SmartScreen may warn about an unsigned app the first time. Click
**More info**, then **Run anyway**.

### 2. From the source code

1. Install Python 3.10 or newer from python.org. Keep "tcl/tk and IDLE"
   ticked in the installer.
2. Double-click `run.bat`. It installs psutil from the `wheels` folder (no
   internet) and opens the app.

### 3. Build your own .exe

With Python installed, double-click `build_exe.bat`. It installs PyInstaller
from `wheels\build` (no internet) and creates `dist\ProcWatch.exe`.

## Tips

* **Tracking Claude:** the default pattern `claude` picks up the Claude
  desktop app and Claude Code. With **Include child processes** on, it also
  picks up the shells, `node` and other tools they start.
* You can track several apps at once with commas: `claude, code, node`.
* The **Choose from running apps** list shows what is running, sorted by
  memory. Pick one to fill in the name.
* Leave ProcWatch running before you open the app you want to watch. The
  Activity log then shows every process it starts.
* System or elevated processes may show "Access denied". Run ProcWatch as
  administrator to see those. Your own apps such as Claude don't need it.

## Limits

ProcWatch samples on a timer (1 second by default). A file that is opened and
closed between two scans (every 3 seconds) won't appear in the log. Windows
also doesn't give per-process network bandwidth without kernel tracing, so the
Network tab lists connections, not bytes. For a complete trace of every file
operation, use Microsoft's free
[Process Monitor](https://learn.microsoft.com/sysinternals/downloads/procmon).

## Project layout

```
ProcWatch.pyw          double-click launcher (no console window)
procwatch/monitor.py   sampling engine: processes, memory, I/O, files, network, events
procwatch/app.py       the Tk user interface
procwatch/charts.py    the line charts
procwatch/recorder.py  CSV logging
wheels/                bundled psutil and PyInstaller, for offline install and build
tests/                 tests for the sampling engine and the offline guarantee (pytest)
```
