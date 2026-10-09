"""Background sampling of a process group: memory, CPU, disk I/O, files and network.

Everything here is local: data comes from the operating system through psutil,
and nothing is sent over the network.
"""

from __future__ import annotations

import os
import socket
import time
from collections import deque
from dataclasses import dataclass, field

import psutil

CPU_COUNT = psutil.cpu_count(logical=True) or 1
IS_WINDOWS = os.name == "nt"


@dataclass
class ProcInfo:
    pid: int
    ppid: int
    name: str
    exe: str
    cmdline: str
    username: str
    status: str
    create_time: float
    cpu_percent: float  # 0-100 across all cores, like Task Manager
    memory: int  # private working set: Task Manager's "Memory" column
    working_set: int  # all physical RAM in use, including pages shared with other processes
    private_bytes: int  # private committed memory: Task Manager's "Commit size"
    read_bytes: int  # cumulative
    write_bytes: int  # cumulative
    other_bytes: int  # cumulative non read/write I/O (Windows only)
    read_rate: float  # bytes per second since last sample
    write_rate: float
    other_rate: float
    threads: int
    handles: int
    is_root: bool  # matched by name, as opposed to being a child of a match

    @property
    def key(self) -> tuple[int, float]:
        return (self.pid, self.create_time)


@dataclass
class OpenFile:
    pid: int
    process: str
    path: str


@dataclass
class Connection:
    pid: int
    process: str
    kind: str
    local: str
    remote: str
    status: str


@dataclass
class Event:
    time: float
    kind: str
    pid: int
    process: str
    detail: str


@dataclass
class Totals:
    time: float
    count: int
    cpu_percent: float
    memory: int
    working_set: int
    private_bytes: int
    read_rate: float
    write_rate: float
    other_rate: float
    threads: int
    handles: int


@dataclass
class Snapshot:
    time: float
    procs: list[ProcInfo]
    totals: Totals
    files: list[OpenFile]
    connections: list[Connection]
    events: list[Event]
    deep_scanned: bool = False
    errors: list[str] = field(default_factory=list)


def parse_patterns(text: str) -> list[str]:
    """Split "claude, node" into lowercase terms."""
    return [p.strip().lower() for p in text.split(",") if p.strip()]


def matches(name: str, exe: str, patterns: list[str]) -> bool:
    name = (name or "").lower()
    exe_base = os.path.basename(exe or "").lower()
    return any(p in name or p in exe_base for p in patterns)


def _fmt_addr(addr) -> str:
    if not addr:
        return ""
    try:
        return f"{addr.ip}:{addr.port}"
    except AttributeError:
        return str(addr)


def list_running_apps() -> list[tuple[str, int, int]]:
    """Return (name, process count, total working set) sorted by memory, largest first."""
    groups: dict[str, list[int]] = {}
    for p in psutil.process_iter(["name", "memory_info"]):
        name = p.info.get("name") or ""
        mem = p.info.get("memory_info")
        if not name or mem is None:
            continue
        g = groups.setdefault(name, [0, 0])
        g[0] += 1
        g[1] += mem.rss
    rows = [(n, c, m) for n, (c, m) in groups.items()]
    rows.sort(key=lambda r: r[2], reverse=True)
    return rows


class ProcessMonitor:
    """Tracks every process whose name matches a pattern, plus (optionally) its descendants.

    Call sample() periodically. Each call returns a Snapshot with per-process
    stats, group totals, and the events that happened since the previous call.
    """

    def __init__(
        self,
        pattern: str,
        include_children: bool = True,
        deep_scan_every: float = 3.0,
        spike_mb: float = 100.0,
        history: int = 600,
    ):
        self.patterns = parse_patterns(pattern)
        self.include_children = include_children
        self.deep_scan_every = deep_scan_every
        self.spike_bytes = int(spike_mb * 1024 * 1024)
        self.history: deque[Totals] = deque(maxlen=history)

        self._procs: dict[tuple[int, float], psutil.Process] = {}
        self._names: dict[tuple[int, float], str] = {}
        self._prev_io: dict[tuple[int, float], tuple[float, int, int, int]] = {}
        self._prev_mem: dict[tuple[int, float], int] = {}
        self._files: dict[tuple[int, float], set[str]] = {}
        self._conns: dict[tuple[int, float], set[tuple[str, str, str, str]]] = {}
        self._last_deep = 0.0
        self._last_files: list[OpenFile] = []
        self._last_conns: list[Connection] = []
        self._first = True

    # -- discovery ---------------------------------------------------------

    def _find_targets(self) -> dict[int, bool]:
        """Return {pid: is_root} for every process in the tracked group."""
        parent_of: dict[int, int] = {}
        roots: set[int] = set()
        for p in psutil.process_iter(["pid", "ppid", "name", "exe"]):
            info = p.info
            pid = info["pid"]
            parent_of[pid] = info.get("ppid") or 0
            if matches(info.get("name") or "", info.get("exe") or "", self.patterns):
                roots.add(pid)

        group = {pid: True for pid in roots}
        if self.include_children and roots:
            children: dict[int, list[int]] = {}
            for pid, ppid in parent_of.items():
                if pid != ppid:
                    children.setdefault(ppid, []).append(pid)
            stack = list(roots)
            while stack:
                for child in children.get(stack.pop(), []):
                    if child not in group:
                        group[child] = False
                        stack.append(child)
        return group

    # -- sampling ----------------------------------------------------------

    def sample(self) -> Snapshot:
        now = time.time()
        events: list[Event] = []
        errors: list[str] = []
        group = self._find_targets()

        seen: set[tuple[int, float]] = set()
        procs: list[ProcInfo] = []

        for pid, is_root in group.items():
            try:
                proc = psutil.Process(pid)
                key = (pid, proc.create_time())
                cached = self._procs.get(key)
                if cached is None:
                    cached = proc
                    self._procs[key] = proc
                    cached.cpu_percent(None)  # prime; first reading is always 0
                info = self._read(cached, key, now, is_root)
            except (psutil.NoSuchProcess, psutil.ZombieProcess):
                continue
            except psutil.AccessDenied:
                errors.append(f"Access denied to PID {pid} (try running as administrator)")
                continue

            seen.add(key)
            procs.append(info)

            if key not in self._names:
                self._names[key] = info.name
                kind = "running" if self._first else "started"
                events.append(Event(now, kind, pid, info.name, info.cmdline or info.exe))
            else:
                prev_mem = self._prev_mem.get(key, info.memory)
                grew = info.memory - prev_mem
                if self.spike_bytes and grew >= self.spike_bytes:
                    events.append(
                        Event(now, "memory spike", pid, info.name,
                              f"+{grew / 1048576:.0f} MB (now {info.memory / 1048576:.0f} MB)")
                    )
            self._prev_mem[key] = info.memory

        for key in list(self._names):
            if key not in seen:
                events.append(Event(now, "exited", key[0], self._names[key], ""))
                self._forget(key)

        deep = now - self._last_deep >= self.deep_scan_every
        if deep:
            self._last_deep = now
            self._last_files, self._last_conns = self._deep_scan(procs, now, events)

        procs.sort(key=lambda p: p.memory, reverse=True)
        totals = Totals(
            time=now,
            count=len(procs),
            cpu_percent=sum(p.cpu_percent for p in procs),
            memory=sum(p.memory for p in procs),
            working_set=sum(p.working_set for p in procs),
            private_bytes=sum(p.private_bytes for p in procs),
            read_rate=sum(p.read_rate for p in procs),
            write_rate=sum(p.write_rate for p in procs),
            other_rate=sum(p.other_rate for p in procs),
            threads=sum(p.threads for p in procs),
            handles=sum(p.handles for p in procs),
        )
        self.history.append(totals)
        self._first = False
        return Snapshot(now, procs, totals, list(self._last_files), list(self._last_conns),
                        events, deep, errors)

    def _read(self, proc: psutil.Process, key, now: float, is_root: bool) -> ProcInfo:
        with proc.oneshot():
            name = proc.name()
            try:
                exe = proc.exe()
            except (psutil.AccessDenied, OSError):
                exe = ""
            try:
                cmdline = " ".join(proc.cmdline())
            except (psutil.AccessDenied, OSError):
                cmdline = ""
            try:
                username = proc.username()
            except (psutil.AccessDenied, OSError, KeyError):
                username = ""
            mem = proc.memory_info()
            # On Windows "private" is the private commit; elsewhere approximate it.
            private = getattr(mem, "private", None)
            if private is None:
                private = mem.rss - getattr(mem, "shared", 0)
            try:
                # USS is the private working set on Windows, matching Task Manager.
                memory = proc.memory_full_info().uss
            except (psutil.AccessDenied, AttributeError, OSError):
                memory = mem.rss
            cpu = proc.cpu_percent(None) / CPU_COUNT
            try:
                io = proc.io_counters()
                rb, wb = io.read_bytes, io.write_bytes
                ob = getattr(io, "other_bytes", 0)
            except (psutil.AccessDenied, AttributeError, OSError):
                rb = wb = ob = 0
            try:
                handles = proc.num_handles() if IS_WINDOWS else proc.num_fds()
            except (psutil.AccessDenied, AttributeError, OSError):
                handles = 0
            info = ProcInfo(
                pid=proc.pid,
                ppid=proc.ppid(),
                name=name,
                exe=exe,
                cmdline=cmdline,
                username=username,
                status=proc.status(),
                create_time=key[1],
                cpu_percent=cpu,
                memory=memory,
                working_set=mem.rss,
                private_bytes=private,
                read_bytes=rb,
                write_bytes=wb,
                other_bytes=ob,
                read_rate=0.0,
                write_rate=0.0,
                other_rate=0.0,
                threads=proc.num_threads(),
                handles=handles,
                is_root=is_root,
            )

        prev = self._prev_io.get(key)
        if prev:
            dt = now - prev[0]
            if dt > 0:
                info.read_rate = max(0, rb - prev[1]) / dt
                info.write_rate = max(0, wb - prev[2]) / dt
                info.other_rate = max(0, ob - prev[3]) / dt
        self._prev_io[key] = (now, rb, wb, ob)
        return info

    def _deep_scan(self, procs: list[ProcInfo], now: float, events: list[Event]):
        """Collect open files and network connections, emitting open/close events."""
        files: list[OpenFile] = []
        conns: list[Connection] = []
        for info in procs:
            key = info.key
            proc = self._procs.get(key)
            if proc is None:
                continue

            try:
                paths = {f.path for f in proc.open_files()}
            except (psutil.Error, OSError):
                paths = None
            if paths is not None:
                files.extend(OpenFile(info.pid, info.name, p) for p in sorted(paths))
                old = self._files.get(key)
                if old is None:
                    if paths:
                        events.append(Event(now, "files", info.pid, info.name,
                                            f"{len(paths)} files already open"))
                else:
                    for p in sorted(paths - old):
                        events.append(Event(now, "file opened", info.pid, info.name, p))
                    for p in sorted(old - paths):
                        events.append(Event(now, "file closed", info.pid, info.name, p))
                self._files[key] = paths

            try:
                raw = proc.net_connections(kind="inet")
            except AttributeError:  # psutil < 6
                try:
                    raw = proc.connections(kind="inet")
                except (psutil.Error, OSError):
                    raw = None
            except (psutil.Error, OSError):
                raw = None
            if raw is not None:
                current: set[tuple[str, str, str, str]] = set()
                for c in raw:
                    kind = "TCP" if c.type == socket.SOCK_STREAM else "UDP"
                    current.add((kind, _fmt_addr(c.laddr), _fmt_addr(c.raddr), c.status or ""))
                conns.extend(Connection(info.pid, info.name, *c) for c in sorted(current))
                old_c = self._conns.get(key)
                if old_c is not None:
                    old_remote = {(c[0], c[2]) for c in old_c if c[2]}
                    new_remote = {(c[0], c[2]) for c in current if c[2]}
                    for kind, remote in sorted(new_remote - old_remote):
                        events.append(Event(now, "connection opened", info.pid, info.name,
                                            f"{kind} {remote}"))
                    for kind, remote in sorted(old_remote - new_remote):
                        events.append(Event(now, "connection closed", info.pid, info.name,
                                            f"{kind} {remote}"))
                elif any(c[2] for c in current):
                    n = sum(1 for c in current if c[2])
                    events.append(Event(now, "network", info.pid, info.name,
                                        f"{n} connections already open"))
                self._conns[key] = current
        return files, conns

    def _forget(self, key) -> None:
        for d in (self._procs, self._names, self._prev_io, self._prev_mem, self._files, self._conns):
            d.pop(key, None)
