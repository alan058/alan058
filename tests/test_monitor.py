import os
import subprocess
import sys
import time

import pytest

from procwatch.monitor import ProcessMonitor, matches, parse_patterns
from procwatch.recorder import CsvRecorder

# A uniquely named child-spawning script lets us match on the command's executable name
# without depending on what else is running on the machine.
PARENT_SRC = """
import subprocess, sys, time
child = subprocess.Popen([sys.executable, "-c", "import time; time.sleep(60)"])
path = sys.argv[1]
f = open(path, "w")
f.write("x" * 1_000_000)
f.flush()
time.sleep(60)
"""


def _same(a, b):
    # Windows may report a long path where the test holds a short (8.3) one, or vice versa.
    try:
        return os.path.samefile(a, b)
    except OSError:
        return False


def test_parse_and_match():
    assert parse_patterns(" Claude, node ,,") == ["claude", "node"]
    assert matches("claude.exe", "", ["claude"])
    assert matches("x", r"C:\\Apps\\Claude.exe", ["claude"])
    assert not matches("chrome.exe", "chrome.exe", ["claude"])


@pytest.fixture
def parent_proc(tmp_path):
    script = tmp_path / "pwtestparent.py"
    script.write_text(PARENT_SRC)
    target = tmp_path / "written.bin"
    proc = subprocess.Popen([sys.executable, str(script), str(target)])
    time.sleep(1.0)
    yield proc, str(target)
    for child in __import__("psutil").Process(proc.pid).children(recursive=True):
        child.kill()
    proc.kill()
    proc.wait()


def _monitor_for(pid, **kw):
    m = ProcessMonitor("unused", **kw)
    # Match by PID so the test is independent of the interpreter's process name.

    def find():
        import psutil
        group = {pid: True}
        if m.include_children:
            for c in psutil.Process(pid).children(recursive=True):
                group[c.pid] = False
        return group

    m._find_targets = find
    return m


def test_sample_tracks_children_files_and_exit(parent_proc):
    proc, target = parent_proc
    m = _monitor_for(proc.pid, deep_scan_every=0)
    s1 = m.sample()
    pids = {p.pid for p in s1.procs}
    assert proc.pid in pids and len(pids) == 2
    assert all(e.kind == "running" for e in s1.events if e.kind in ("running", "started"))
    root = next(p for p in s1.procs if p.pid == proc.pid)
    assert root.is_root and root.working_set > 0 and root.threads >= 1
    assert any(_same(f.path, target) for f in s1.files)
    assert s1.totals.count == 2

    child = next(p for p in s1.procs if p.pid != proc.pid)
    import psutil
    psutil.Process(child.pid).kill()
    time.sleep(0.5)
    s2 = m.sample()
    assert any(e.kind == "exited" and e.pid == child.pid for e in s2.events)
    assert s2.totals.count == 1
    assert len(m.history) == 2


def test_without_children(parent_proc):
    proc, _ = parent_proc
    m = _monitor_for(proc.pid, include_children=False)
    assert [p.pid for p in m.sample().procs] == [proc.pid]


def test_real_name_matching_finds_self():
    import psutil
    me = psutil.Process()
    m = ProcessMonitor(me.name(), include_children=False)
    assert me.pid in {p.pid for p in m.sample().procs}


def test_recorder_writes_csv(parent_proc, tmp_path):
    proc, _ = parent_proc
    m = _monitor_for(proc.pid, deep_scan_every=0)
    rec = CsvRecorder(str(tmp_path / "logs"), "claude test")
    rec.write(m.sample())
    rec.close()
    samples = open(rec.samples_path, encoding="utf-8").read().splitlines()
    events = open(rec.events_path, encoding="utf-8").read().splitlines()
    assert samples[0].startswith("timestamp,pid") and len(samples) == 3
    assert len(events) >= 3
    assert "claude_test_" in os.path.basename(rec.samples_path)


LATE_SRC = """
import subprocess, sys, time
time.sleep(1.5)
f = open(sys.argv[1], "w")
child = subprocess.Popen([sys.executable, "-c", "import time; time.sleep(60)"])
time.sleep(60)
"""


def test_events_for_new_files_and_children(tmp_path):
    import psutil
    script = tmp_path / "pwlate.py"
    script.write_text(LATE_SRC)
    target = str(tmp_path / "late.txt")
    proc = subprocess.Popen([sys.executable, str(script), target])
    try:
        time.sleep(0.5)
        m = _monitor_for(proc.pid, deep_scan_every=0)
        m.sample()
        time.sleep(2.0)
        s = m.sample()
        assert any(e.kind == "file opened" and _same(e.detail, target) for e in s.events)
        assert any(e.kind == "started" for e in s.events)
        assert s.totals.memory > 0 and all(p.memory > 0 for p in s.procs)
    finally:
        for c in psutil.Process(proc.pid).children(recursive=True):
            c.kill()
        proc.kill()
        proc.wait()
