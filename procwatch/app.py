"""ProcWatch: a Task-Manager-style monitor for one app and everything it spawns.

Pick an app (for example "claude"), press Start, and ProcWatch shows live
memory, CPU, and disk I/O for every matching process and its children, the
files they hold open, their network connections, and a timeline of what
changed. Runs fully offline.
"""

from __future__ import annotations

import os
import queue
import subprocess
import sys
import threading
import time
import tkinter as tk
from collections import deque
from tkinter import filedialog, messagebox, ttk

import psutil

from .charts import LineChart
from .monitor import ProcessMonitor, Snapshot, list_running_apps
from .recorder import CsvRecorder

APP_NAME = "ProcWatch"
HISTORY = 120  # samples shown in the charts
MAX_EVENTS = 5000

COLORS = {
    "bg": "#202020",
    "panel": "#2b2b2b",
    "field": "#1c1c1c",
    "fg": "#f0f0f0",
    "muted": "#a0a0a0",
    "grid": "#3c3c3c",
    "accent": "#d97757",
    "select": "#4a3a33",
    "cpu": "#4cc2ff",
    "mem": "#c586f7",
    "read": "#6ccb5f",
    "write": "#fc9d4f",
    "other": "#a0a0a0",
}


def fmt_bytes(n: float) -> str:
    for unit in ("B", "KB", "MB", "GB"):
        if abs(n) < 1024 or unit == "GB":
            return f"{n:.0f} {unit}" if unit in ("B", "KB") else f"{n:.1f} {unit}"
        n /= 1024
    return f"{n:.1f} GB"


def fmt_rate(n: float) -> str:
    return fmt_bytes(n) + "/s"


def fmt_pct(n: float) -> str:
    return f"{n:.1f}%"


def fmt_time(t: float) -> str:
    return time.strftime("%H:%M:%S", time.localtime(t))


class Sampler(threading.Thread):
    """Runs ProcessMonitor.sample() off the UI thread and queues the results."""

    def __init__(self, monitor: ProcessMonitor, interval: float, out: queue.Queue):
        super().__init__(daemon=True)
        self.monitor = monitor
        self.interval = interval
        self.out = out
        self.stop_flag = threading.Event()

    def run(self) -> None:
        while not self.stop_flag.is_set():
            started = time.monotonic()
            try:
                self.out.put(self.monitor.sample())
            except Exception as exc:  # keep sampling even if one pass fails
                self.out.put(exc)
            self.stop_flag.wait(max(0.05, self.interval - (time.monotonic() - started)))


class App:
    PROC_COLUMNS = [
        # id, heading, width, anchor, sort key
        ("pid", "PID", 70, "e", lambda p: p.pid),
        ("cpu", "CPU", 70, "e", lambda p: p.cpu_percent),
        ("mem", "Memory", 95, "e", lambda p: p.memory),
        ("ws", "Working set", 105, "e", lambda p: p.working_set),
        ("private", "Commit", 95, "e", lambda p: p.private_bytes),
        ("read", "Disk read", 95, "e", lambda p: p.read_rate),
        ("write", "Disk write", 95, "e", lambda p: p.write_rate),
        ("other", "Other I/O", 90, "e", lambda p: p.other_rate),
        ("threads", "Threads", 72, "e", lambda p: p.threads),
        ("handles", "Handles", 70, "e", lambda p: p.handles),
        ("status", "Status", 80, "w", lambda p: p.status),
        ("cmd", "Command line", 400, "w", lambda p: p.cmdline.lower()),
    ]

    def __init__(self, root: tk.Tk):
        self.root = root
        self.queue: queue.Queue = queue.Queue()
        self.sampler: Sampler | None = None
        self.recorder: CsvRecorder | None = None
        self.snapshot: Snapshot | None = None
        self.history: deque = deque(maxlen=HISTORY)
        self.proc_history: dict[tuple, deque] = {}
        self.events: deque = deque(maxlen=MAX_EVENTS)
        self.peak_mem = 0
        self.sort_col = "mem"
        self.sort_desc = True
        self.selected_key: tuple | None = None
        self.started_at = 0.0

        root.title(f"{APP_NAME} - Process Activity Monitor")
        root.geometry("1280x860")
        root.minsize(900, 600)
        root.configure(bg=COLORS["bg"])
        self._style()
        self._build()
        self._refresh_apps()
        root.protocol("WM_DELETE_WINDOW", self._on_close)
        root.after(100, self._drain)

    # -- look and feel -----------------------------------------------------

    def _style(self) -> None:
        c = COLORS
        s = ttk.Style(self.root)
        s.theme_use("clam")
        base = ("Segoe UI", 10)
        s.configure(".", background=c["bg"], foreground=c["fg"], fieldbackground=c["field"],
                    bordercolor=c["grid"], lightcolor=c["panel"], darkcolor=c["panel"],
                    troughcolor=c["field"], font=base)
        s.configure("TFrame", background=c["bg"])
        s.configure("Panel.TFrame", background=c["panel"])
        s.configure("TLabel", background=c["bg"], foreground=c["fg"])
        s.configure("Muted.TLabel", foreground=c["muted"])
        s.configure("TileName.TLabel", background=c["panel"], foreground=c["muted"],
                    font=("Segoe UI", 9))
        s.configure("TileValue.TLabel", background=c["panel"], foreground=c["fg"],
                    font=("Segoe UI", 15, "bold"))
        s.configure("TButton", background=c["panel"], foreground=c["fg"], padding=(10, 4))
        s.map("TButton", background=[("active", c["grid"])])
        s.configure("Accent.TButton", background=c["accent"], foreground="#ffffff")
        s.map("Accent.TButton", background=[("active", "#c4664a")])
        s.configure("TCheckbutton", background=c["bg"], foreground=c["fg"])
        s.map("TCheckbutton", background=[("active", c["bg"])])
        s.configure("TEntry", foreground=c["fg"], insertcolor=c["fg"])
        s.configure("TSpinbox", foreground=c["fg"], arrowcolor=c["fg"], insertcolor=c["fg"])
        s.configure("TCombobox", foreground=c["fg"], arrowcolor=c["fg"])
        s.map("TCombobox", fieldbackground=[("readonly", c["field"])],
              foreground=[("readonly", c["fg"])])
        self.root.option_add("*TCombobox*Listbox.background", c["field"])
        self.root.option_add("*TCombobox*Listbox.foreground", c["fg"])
        s.configure("TNotebook", background=c["bg"], borderwidth=0)
        s.configure("TNotebook.Tab", background=c["bg"], foreground=c["muted"], padding=(14, 6))
        s.map("TNotebook.Tab", background=[("selected", c["panel"])],
              foreground=[("selected", c["fg"])])
        s.configure("Treeview", background=c["panel"], fieldbackground=c["panel"],
                    foreground=c["fg"], rowheight=24, borderwidth=0)
        s.map("Treeview", background=[("selected", c["select"])],
              foreground=[("selected", c["fg"])])
        s.configure("Treeview.Heading", background=c["bg"], foreground=c["muted"],
                    relief="flat", padding=(6, 4))
        s.map("Treeview.Heading", background=[("active", c["panel"])])

    # -- layout ------------------------------------------------------------

    def _build(self) -> None:
        pad = {"padx": 10, "pady": 6}

        bar = ttk.Frame(self.root)
        bar.pack(fill="x", **pad)
        ttk.Label(bar, text="Track app:").pack(side="left")
        self.pattern_var = tk.StringVar(value="claude")
        e = ttk.Entry(bar, textvariable=self.pattern_var, width=22)
        e.pack(side="left", padx=(6, 4))
        e.bind("<Return>", lambda ev: self._toggle())
        self.apps_var = tk.StringVar()
        self.apps_box = ttk.Combobox(bar, textvariable=self.apps_var, width=38, state="readonly")
        self.apps_box.pack(side="left", padx=4)
        self.apps_box.bind("<<ComboboxSelected>>", self._pick_app)
        ttk.Button(bar, text="↻", width=3, command=self._refresh_apps).pack(side="left")

        ttk.Label(bar, text="Every").pack(side="left", padx=(14, 4))
        self.interval_var = tk.StringVar(value="1")
        ttk.Spinbox(bar, from_=0.5, to=60, increment=0.5, width=5,
                    textvariable=self.interval_var).pack(side="left")
        ttk.Label(bar, text="s").pack(side="left", padx=(2, 10))
        self.children_var = tk.BooleanVar(value=True)
        ttk.Checkbutton(bar, text="Include child processes",
                        variable=self.children_var).pack(side="left")

        self.record_btn = ttk.Button(bar, text="Record to CSV…", command=self._toggle_record)
        self.record_btn.pack(side="right", padx=(6, 0))
        self.start_btn = ttk.Button(bar, text="Start", style="Accent.TButton", command=self._toggle)
        self.start_btn.pack(side="right")

        tiles = ttk.Frame(self.root)
        tiles.pack(fill="x", padx=10)
        self.tiles: dict[str, ttk.Label] = {}
        for key, label in [("count", "Processes"), ("cpu", "CPU"), ("mem", "Memory"),
                           ("peak", "Peak memory"), ("private", "Commit"),
                           ("read", "Disk read"), ("write", "Disk write"),
                           ("threads", "Threads"), ("handles", "Handles")]:
            f = ttk.Frame(tiles, style="Panel.TFrame", padding=(12, 6))
            f.pack(side="left", fill="x", expand=True, padx=(0, 6))
            ttk.Label(f, text=label, style="TileName.TLabel").pack(anchor="w")
            v = ttk.Label(f, text="-", style="TileValue.TLabel")
            v.pack(anchor="w")
            self.tiles[key] = v

        charts = ttk.Frame(self.root)
        charts.pack(fill="x", padx=10, pady=8)
        for i in range(3):
            charts.columnconfigure(i, weight=1, uniform="chart")
        self.cpu_chart = LineChart(charts, "CPU", [("CPU", COLORS["cpu"])], fmt_pct, COLORS,
                                   capacity=HISTORY)
        self.mem_chart = LineChart(charts, "Memory",
                                   [("Memory", COLORS["mem"]), ("Working set", COLORS["accent"])],
                                   fmt_bytes, COLORS, capacity=HISTORY)
        self.io_chart = LineChart(charts, "Disk I/O",
                                  [("Read", COLORS["read"]), ("Write", COLORS["write"])],
                                  fmt_rate, COLORS, capacity=HISTORY)
        for i, ch in enumerate((self.cpu_chart, self.mem_chart, self.io_chart)):
            ch.grid(row=0, column=i, sticky="nsew", padx=(0 if i == 0 else 6, 0))

        self.nb = ttk.Notebook(self.root)
        self.nb.pack(fill="both", expand=True, padx=10, pady=(0, 6))
        self._build_processes()
        self._build_files()
        self._build_network()
        self._build_events()
        self._build_details()

        self.status_var = tk.StringVar(value="Pick an app and press Start.")
        ttk.Label(self.root, textvariable=self.status_var, style="Muted.TLabel").pack(
            fill="x", padx=10, pady=(0, 6))

    def _tree(self, parent, columns, show="headings") -> ttk.Treeview:
        frame = ttk.Frame(parent)
        frame.pack(fill="both", expand=True)
        tree = ttk.Treeview(frame, columns=[c[0] for c in columns], show=show)
        ys = ttk.Scrollbar(frame, orient="vertical", command=tree.yview)
        xs = ttk.Scrollbar(frame, orient="horizontal", command=tree.xview)
        tree.configure(yscrollcommand=ys.set, xscrollcommand=xs.set)
        tree.grid(row=0, column=0, sticky="nsew")
        ys.grid(row=0, column=1, sticky="ns")
        xs.grid(row=1, column=0, sticky="ew")
        frame.rowconfigure(0, weight=1)
        frame.columnconfigure(0, weight=1)
        for cid, heading, width, anchor, *_ in columns:
            tree.heading(cid, text=heading, anchor=anchor)
            tree.column(cid, width=width, anchor=anchor, stretch=cid in ("cmd", "path", "detail"))
        return tree

    def _filter_bar(self, parent, var: tk.StringVar) -> ttk.Frame:
        f = ttk.Frame(parent)
        f.pack(fill="x", pady=(6, 4))
        ttk.Label(f, text="Filter:").pack(side="left")
        ent = ttk.Entry(f, textvariable=var, width=40)
        ent.pack(side="left", padx=6)
        return f

    def _build_processes(self) -> None:
        tab = ttk.Frame(self.nb)
        self.nb.add(tab, text="Processes")
        top = ttk.Frame(tab)
        top.pack(fill="x", pady=(6, 4))
        self.tree_mode = tk.BooleanVar(value=True)
        ttk.Checkbutton(top, text="Show as tree (child processes under their parent)",
                        variable=self.tree_mode, command=self._render_procs).pack(side="left")
        ttk.Label(top, text="Right-click a process for more actions.",
                  style="Muted.TLabel").pack(side="right")

        self.proc_tree = self._tree(tab, self.PROC_COLUMNS, show="tree headings")
        self.proc_tree.heading("#0", text="Name", anchor="w")
        self.proc_tree.column("#0", width=230, anchor="w", stretch=False)
        for cid, *_ in self.PROC_COLUMNS:
            self.proc_tree.heading(cid, command=lambda c=cid: self._sort_by(c))
        self.proc_tree.tag_configure("root", foreground=COLORS["fg"])
        self.proc_tree.tag_configure("child", foreground="#d0d0d0")
        self.proc_tree.bind("<<TreeviewSelect>>", self._on_select)
        self.proc_tree.bind("<Button-3>", self._proc_menu)
        self.proc_tree.bind("<Double-1>", lambda e: self.nb.select(4))

        self.menu = tk.Menu(self.root, tearoff=0, bg=COLORS["panel"], fg=COLORS["fg"],
                            activebackground=COLORS["select"], activeforeground=COLORS["fg"])
        self.menu.add_command(label="Show details", command=lambda: self.nb.select(4))
        self.menu.add_command(label="Copy command line", command=lambda: self._copy("cmdline"))
        self.menu.add_command(label="Copy executable path", command=lambda: self._copy("exe"))
        self.menu.add_command(label="Open file location", command=self._open_location)
        self.menu.add_separator()
        self.menu.add_command(label="End process…", command=self._kill_selected)

    def _build_files(self) -> None:
        tab = ttk.Frame(self.nb)
        self.nb.add(tab, text="Open files")
        self.files_filter = tk.StringVar()
        self.files_filter.trace_add("write", lambda *a: self._render_files())
        bar = self._filter_bar(tab, self.files_filter)
        self.files_count = ttk.Label(bar, style="Muted.TLabel")
        self.files_count.pack(side="right")
        self.files_tree = self._tree(tab, [("pid", "PID", 70, "e"), ("proc", "Process", 160, "w"),
                                           ("path", "File path", 800, "w")])

    def _build_network(self) -> None:
        tab = ttk.Frame(self.nb)
        self.nb.add(tab, text="Network")
        self.net_filter = tk.StringVar()
        self.net_filter.trace_add("write", lambda *a: self._render_net())
        bar = self._filter_bar(tab, self.net_filter)
        self.net_count = ttk.Label(bar, style="Muted.TLabel")
        self.net_count.pack(side="right")
        self.net_tree = self._tree(tab, [("pid", "PID", 70, "e"), ("proc", "Process", 160, "w"),
                                         ("kind", "Proto", 60, "w"), ("local", "Local", 200, "w"),
                                         ("remote", "Remote", 260, "w"),
                                         ("status", "State", 120, "w")])

    def _build_events(self) -> None:
        tab = ttk.Frame(self.nb)
        self.nb.add(tab, text="Activity log")
        self.events_filter = tk.StringVar()
        self.events_filter.trace_add("write", lambda *a: self._render_events(full=True))
        bar = self._filter_bar(tab, self.events_filter)
        self.follow_var = tk.BooleanVar(value=True)
        ttk.Checkbutton(bar, text="Auto-scroll", variable=self.follow_var).pack(side="left", padx=8)
        ttk.Button(bar, text="Clear", command=self._clear_events).pack(side="left")
        ttk.Label(bar, text="Spike alert ≥").pack(side="left", padx=(16, 4))
        self.spike_var = tk.StringVar(value="100")
        ttk.Spinbox(bar, from_=0, to=10000, increment=50, width=6,
                    textvariable=self.spike_var).pack(side="left")
        ttk.Label(bar, text="MB per sample (0 = off)").pack(side="left", padx=4)
        self.events_tree = self._tree(tab, [("time", "Time", 80, "w"), ("kind", "Event", 140, "w"),
                                            ("pid", "PID", 70, "e"), ("proc", "Process", 160, "w"),
                                            ("detail", "Detail", 700, "w")])
        for kind, color in [("started", COLORS["read"]), ("exited", "#ff6b6b"),
                            ("memory spike", COLORS["write"]),
                            ("connection opened", COLORS["cpu"]),
                            ("file opened", COLORS["mem"])]:
            self.events_tree.tag_configure(kind, foreground=color)

    def _build_details(self) -> None:
        tab = ttk.Frame(self.nb)
        self.nb.add(tab, text="Details")
        self.detail_chart = LineChart(tab, "Selected process memory",
                                      [("Memory", COLORS["mem"]),
                                       ("Working set", COLORS["accent"])],
                                      fmt_bytes, COLORS, capacity=HISTORY)
        self.detail_chart.pack(fill="x", pady=(8, 6))
        self.detail_text = tk.Text(tab, bg=COLORS["panel"], fg=COLORS["fg"], relief="flat",
                                   font=("Consolas", 10), wrap="word", padx=10, pady=8,
                                   insertbackground=COLORS["fg"])
        self.detail_text.pack(fill="both", expand=True)
        self.detail_text.insert("1.0", "Select a process in the Processes tab.")
        self.detail_text.configure(state="disabled")

    # -- actions -----------------------------------------------------------

    def _refresh_apps(self) -> None:
        rows = list_running_apps()
        self._app_rows = rows
        self.apps_box["values"] = [f"{n}   ({c} proc, {fmt_bytes(m)})" for n, c, m in rows[:60]]
        self.apps_var.set("Choose from running apps…")

    def _pick_app(self, _e=None) -> None:
        i = self.apps_box.current()
        if 0 <= i < len(self._app_rows):
            name = self._app_rows[i][0]
            stem = os.path.splitext(name)[0]
            self.pattern_var.set(stem)

    def _toggle(self) -> None:
        if self.sampler:
            self._stop()
        else:
            self._start()

    def _start(self) -> None:
        pattern = self.pattern_var.get().strip()
        if not pattern:
            messagebox.showwarning(APP_NAME, "Type an app name to track, e.g. claude")
            return
        try:
            interval = max(0.5, float(self.interval_var.get()))
        except ValueError:
            interval = 1.0
        monitor = ProcessMonitor(pattern, include_children=self.children_var.get(),
                                 spike_mb=self._spike_mb())
        self.history.clear()
        self.proc_history.clear()
        self.peak_mem = 0
        self.started_at = time.time()
        self.proc_tree.delete(*self.proc_tree.get_children())
        self.sampler = Sampler(monitor, interval, self.queue)
        self.sampler.start()
        self.start_btn.configure(text="Stop")
        self.status_var.set(f"Watching “{pattern}” every {interval:g}s…")

    def _stop(self) -> None:
        if self.sampler:
            self.sampler.stop_flag.set()
            self.sampler = None
        self.start_btn.configure(text="Start")
        self.status_var.set("Stopped.")

    def _spike_mb(self) -> float:
        try:
            return max(0.0, float(self.spike_var.get()))
        except ValueError:
            return 0.0

    def _toggle_record(self) -> None:
        if self.recorder:
            paths = (self.recorder.samples_path, self.recorder.events_path)
            self.recorder.close()
            self.recorder = None
            self.record_btn.configure(text="Record to CSV…")
            messagebox.showinfo(APP_NAME, "Recording saved:\n\n" + "\n".join(paths))
            return
        folder = filedialog.askdirectory(title="Folder for the CSV log files")
        if not folder:
            return
        try:
            self.recorder = CsvRecorder(folder, self.pattern_var.get().strip())
        except OSError as exc:
            messagebox.showerror(APP_NAME, f"Could not create log files:\n{exc}")
            return
        self.record_btn.configure(text="● Stop recording")
        if not self.sampler:
            self._start()

    def _selected_proc(self):
        if not self.snapshot or not self.selected_key:
            return None
        for p in self.snapshot.procs:
            if p.key == self.selected_key:
                return p
        return None

    def _copy(self, attr: str) -> None:
        p = self._selected_proc()
        if p:
            self.root.clipboard_clear()
            self.root.clipboard_append(getattr(p, attr) or "")

    def _open_location(self) -> None:
        p = self._selected_proc()
        if not p or not p.exe:
            return
        if sys.platform == "win32":
            subprocess.Popen(["explorer", "/select,", p.exe])
        else:
            subprocess.Popen(["xdg-open", os.path.dirname(p.exe)])

    def _kill_selected(self) -> None:
        p = self._selected_proc()
        if not p:
            return
        if not messagebox.askyesno(APP_NAME, f"End {p.name} (PID {p.pid})?\n\n"
                                             "Unsaved work in that process will be lost."):
            return
        try:
            psutil.Process(p.pid).terminate()
        except psutil.Error as exc:
            messagebox.showerror(APP_NAME, f"Could not end the process:\n{exc}")

    def _proc_menu(self, event) -> None:
        iid = self.proc_tree.identify_row(event.y)
        if iid:
            self.proc_tree.selection_set(iid)
            self.menu.tk_popup(event.x_root, event.y_root)

    def _on_select(self, _e=None) -> None:
        sel = self.proc_tree.selection()
        if sel:
            pid, ct = sel[0].split("|")
            self.selected_key = (int(pid), float(ct))
            self._render_details()

    def _sort_by(self, col: str) -> None:
        if self.sort_col == col:
            self.sort_desc = not self.sort_desc
        else:
            self.sort_col, self.sort_desc = col, col not in ("pid", "status", "cmd")
        self._render_procs()

    def _clear_events(self) -> None:
        self.events.clear()
        self._render_events(full=True)

    def _on_close(self) -> None:
        self._stop()
        if self.recorder:
            self.recorder.close()
        self.root.destroy()

    # -- updates -----------------------------------------------------------

    def _drain(self) -> None:
        latest = None
        new_events = []
        try:
            while True:
                item = self.queue.get_nowait()
                if isinstance(item, Exception):
                    self.status_var.set(f"Sampling error: {item}")
                    continue
                latest = item
                new_events.extend(item.events)
                if self.recorder:
                    self.recorder.write(item)
                self.history.append(item.totals)
                for p in item.procs:
                    h = self.proc_history.setdefault(p.key, deque(maxlen=HISTORY))
                    h.append((p.memory, p.working_set))
        except queue.Empty:
            pass
        if latest and self.sampler:
            self.sampler.monitor.spike_bytes = int(self._spike_mb() * 1048576)
            live = {p.key for p in latest.procs}
            for k in [k for k in self.proc_history if k not in live]:
                del self.proc_history[k]
            self._apply(latest, new_events)
        self.root.after(200, self._drain)

    def _apply(self, snap: Snapshot, new_events) -> None:
        self.snapshot = snap
        t = snap.totals
        self.peak_mem = max(self.peak_mem, t.memory)
        self.tiles["count"].configure(text=str(t.count))
        self.tiles["cpu"].configure(text=fmt_pct(t.cpu_percent))
        self.tiles["mem"].configure(text=fmt_bytes(t.memory))
        self.tiles["peak"].configure(text=fmt_bytes(self.peak_mem))
        self.tiles["private"].configure(text=fmt_bytes(t.private_bytes))
        self.tiles["read"].configure(text=fmt_rate(t.read_rate))
        self.tiles["write"].configure(text=fmt_rate(t.write_rate))
        self.tiles["threads"].configure(text=str(t.threads))
        self.tiles["handles"].configure(text=str(t.handles))

        h = list(self.history)
        self.cpu_chart.set_data([[x.cpu_percent for x in h]])
        self.mem_chart.set_data([[x.memory for x in h], [x.working_set for x in h]])
        self.io_chart.set_data([[x.read_rate for x in h], [x.write_rate for x in h]])

        self._render_procs()
        if snap.deep_scanned:
            self._render_files()
            self._render_net()
        if new_events:
            self.events.extend(new_events)
            self._render_events(new=new_events)
        self._render_details()

        elapsed = int(time.time() - self.started_at)
        msg = (f"Watching “{self.pattern_var.get()}” • {t.count} processes "
               f"• running {elapsed // 60}m {elapsed % 60:02d}s • last sample "
               f"{fmt_time(snap.time)}")
        if self.recorder:
            msg += " • recording to CSV"
        if snap.errors:
            msg += f" • {snap.errors[0]}"
        if not snap.procs:
            msg += " • no matching process running (it will appear here when it starts)"
        self.status_var.set(msg)

    def _proc_values(self, p) -> tuple:
        return (p.pid, fmt_pct(p.cpu_percent), fmt_bytes(p.memory), fmt_bytes(p.working_set),
                fmt_bytes(p.private_bytes), fmt_rate(p.read_rate), fmt_rate(p.write_rate),
                fmt_rate(p.other_rate), p.threads, p.handles, p.status, p.cmdline)

    def _render_procs(self) -> None:
        tree = self.proc_tree
        procs = self.snapshot.procs if self.snapshot else []
        keyfn = next(c[4] for c in self.PROC_COLUMNS if c[0] == self.sort_col)
        procs = sorted(procs, key=keyfn, reverse=self.sort_desc)
        iid_of = {p.key: f"{p.pid}|{p.create_time}" for p in procs}
        by_pid = {p.pid: p for p in procs}
        wanted = set(iid_of.values())

        # Deleting a parent row also deletes its subtree, so check existence before each delete.
        for iid in self._all_iids():
            if iid not in wanted and tree.exists(iid):
                tree.delete(iid)

        # Pass 1: create or update every row. Pass 2: put each under its parent, in sort order.
        for p in procs:
            iid = iid_of[p.key]
            tag = "root" if p.is_root else "child"
            if tree.exists(iid):
                tree.item(iid, text=p.name, values=self._proc_values(p), tags=(tag,))
            else:
                tree.insert("", "end", iid=iid, text=p.name, values=self._proc_values(p),
                            tags=(tag,), open=True)

        tree_mode = self.tree_mode.get()
        order: dict[str, int] = {}
        for p in procs:
            iid = iid_of[p.key]
            parent = ""
            if tree_mode and p.ppid in by_pid and p.ppid != p.pid:
                parent = iid_of[by_pid[p.ppid].key]
            idx = order.get(parent, 0)
            order[parent] = idx + 1
            if tree.parent(iid) != parent or tree.index(iid) != idx:
                try:
                    tree.move(iid, parent, idx)
                except tk.TclError:  # would create a cycle if PIDs were reused
                    tree.move(iid, "", "end")

        for cid, heading, *_ in self.PROC_COLUMNS:
            arrow = (" ▼" if self.sort_desc else " ▲") if cid == self.sort_col else ""
            tree.heading(cid, text=heading + arrow)

    def _all_iids(self) -> list[str]:
        out, stack = [], list(self.proc_tree.get_children(""))
        while stack:
            iid = stack.pop()
            out.append(iid)
            stack.extend(self.proc_tree.get_children(iid))
        return out

    def _render_files(self) -> None:
        tree = self.files_tree
        files = self.snapshot.files if self.snapshot else []
        q = self.files_filter.get().lower()
        rows = [f for f in files if not q or q in f.path.lower() or q in f.process.lower()]
        tree.delete(*tree.get_children())
        for f in rows:
            tree.insert("", "end", values=(f.pid, f.process, f.path))
        self.files_count.configure(text=f"{len(rows)} of {len(files)} open files "
                                        "(refreshed every 3s)")

    def _render_net(self) -> None:
        tree = self.net_tree
        conns = self.snapshot.connections if self.snapshot else []
        q = self.net_filter.get().lower()
        rows = [c for c in conns
                if not q or any(q in str(v).lower()
                                for v in (c.process, c.local, c.remote, c.status, c.pid))]
        tree.delete(*tree.get_children())
        for c in rows:
            tree.insert("", "end", values=(c.pid, c.process, c.kind, c.local, c.remote, c.status))
        self.net_count.configure(text=f"{len(rows)} of {len(conns)} connections")

    def _event_row(self, e):
        return (fmt_time(e.time), e.kind, e.pid, e.process, e.detail)

    def _event_match(self, e, q: str) -> bool:
        return not q or q in f"{e.kind} {e.pid} {e.process} {e.detail}".lower()

    def _render_events(self, new=None, full=False) -> None:
        tree = self.events_tree
        q = self.events_filter.get().lower()
        if full or new is None:
            tree.delete(*tree.get_children())
            source = self.events
        else:
            source = new
        for e in source:
            if self._event_match(e, q):
                tree.insert("", "end", values=self._event_row(e), tags=(e.kind,))
        extra = len(tree.get_children()) - MAX_EVENTS
        if extra > 0:
            tree.delete(*tree.get_children()[:extra])
        if self.follow_var.get():
            kids = tree.get_children()
            if kids:
                tree.see(kids[-1])

    def _render_details(self) -> None:
        p = self._selected_proc()
        if not p:
            return
        hist = list(self.proc_history.get(p.key, []))
        self.detail_chart.title = f"{p.name} (PID {p.pid}) memory"
        self.detail_chart.set_data([[h[0] for h in hist], [h[1] for h in hist]])
        files = [f.path for f in (self.snapshot.files if self.snapshot else []) if f.pid == p.pid]
        conns = [c for c in (self.snapshot.connections if self.snapshot else []) if c.pid == p.pid]
        lines = [
            f"Name            {p.name}",
            f"PID / Parent    {p.pid} / {p.ppid}",
            f"Tracked because {'name matches' if p.is_root else 'child of a tracked process'}",
            f"Status          {p.status}",
            f"User            {p.username or '-'}",
            f"Started         {time.strftime('%Y-%m-%d %H:%M:%S', time.localtime(p.create_time))}",
            f"Executable      {p.exe or '-'}",
            f"Command line    {p.cmdline or '-'}",
            "",
            f"CPU             {fmt_pct(p.cpu_percent)}",
            f"Memory          {fmt_bytes(p.memory)}   (private working set, as in Task Manager)",
            f"Working set     {fmt_bytes(p.working_set)}   (all RAM in use, incl. shared DLLs)",
            f"Commit size     {fmt_bytes(p.private_bytes)}   (private memory reserved, RAM + page file)",
            f"Disk read       {fmt_rate(p.read_rate)}   total {fmt_bytes(p.read_bytes)}",
            f"Disk write      {fmt_rate(p.write_rate)}   total {fmt_bytes(p.write_bytes)}",
            f"Other I/O       {fmt_rate(p.other_rate)}   total {fmt_bytes(p.other_bytes)}",
            f"Threads         {p.threads}",
            f"Handles         {p.handles}",
            "",
            f"Open files ({len(files)}):",
            *[f"  {f}" for f in files[:200]],
            "",
            f"Network connections ({len(conns)}):",
            *[f"  {c.kind} {c.local} -> {c.remote or '(listening)'} {c.status}" for c in conns],
        ]
        txt = self.detail_text
        y = txt.yview()[0]
        txt.configure(state="normal")
        txt.delete("1.0", "end")
        txt.insert("1.0", "\n".join(lines))
        txt.configure(state="disabled")
        txt.yview_moveto(y)


def _enable_dpi_awareness() -> None:
    """Keep text sharp on high-DPI Windows 11 displays."""
    if sys.platform != "win32":
        return
    try:
        import ctypes
        ctypes.windll.shcore.SetProcessDpiAwareness(1)
    except Exception:
        pass


def main() -> None:
    _enable_dpi_awareness()
    root = tk.Tk()
    App(root)
    root.mainloop()


if __name__ == "__main__":
    main()
