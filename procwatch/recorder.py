"""Write samples and events to CSV files so a session can be reviewed later."""

from __future__ import annotations

import csv
import os
import time

from .monitor import Snapshot


def _ts(t: float) -> str:
    return time.strftime("%Y-%m-%d %H:%M:%S", time.localtime(t)) + f".{int(t % 1 * 1000):03d}"


class CsvRecorder:
    """Appends every snapshot to <folder>/<prefix>_samples.csv and _events.csv."""

    SAMPLE_HEADER = [
        "timestamp", "pid", "ppid", "name", "cpu_percent", "memory_mb", "working_set_mb",
        "commit_mb", "read_kb_s", "write_kb_s", "other_kb_s", "threads", "handles", "status", "cmdline",
    ]
    EVENT_HEADER = ["timestamp", "event", "pid", "process", "detail"]

    def __init__(self, folder: str, prefix: str):
        os.makedirs(folder, exist_ok=True)
        stamp = time.strftime("%Y%m%d_%H%M%S")
        safe = "".join(c if c.isalnum() or c in "-_" else "_" for c in prefix) or "procwatch"
        self.samples_path = os.path.join(folder, f"{safe}_{stamp}_samples.csv")
        self.events_path = os.path.join(folder, f"{safe}_{stamp}_events.csv")
        self._sf = open(self.samples_path, "w", newline="", encoding="utf-8")
        self._ef = open(self.events_path, "w", newline="", encoding="utf-8")
        self._sw = csv.writer(self._sf)
        self._ew = csv.writer(self._ef)
        self._sw.writerow(self.SAMPLE_HEADER)
        self._ew.writerow(self.EVENT_HEADER)

    def write(self, snap: Snapshot) -> None:
        ts = _ts(snap.time)
        for p in snap.procs:
            self._sw.writerow([
                ts, p.pid, p.ppid, p.name, f"{p.cpu_percent:.1f}",
                f"{p.memory / 1048576:.1f}", f"{p.working_set / 1048576:.1f}",
                f"{p.private_bytes / 1048576:.1f}",
                f"{p.read_rate / 1024:.1f}", f"{p.write_rate / 1024:.1f}",
                f"{p.other_rate / 1024:.1f}", p.threads, p.handles, p.status, p.cmdline,
            ])
        for e in snap.events:
            self._ew.writerow([_ts(e.time), e.kind, e.pid, e.process, e.detail])
        self._sf.flush()
        self._ef.flush()

    def close(self) -> None:
        self._sf.close()
        self._ef.close()
