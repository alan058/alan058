"""A small dependency-free line chart drawn on a Tk canvas."""

from __future__ import annotations

import tkinter as tk
from typing import Callable, Sequence


def _blend(fg: str, bg: str, alpha: float) -> str:
    """Mix two #rrggbb colours; used instead of stipple, which Tk renders poorly on Windows."""
    a = [int(fg[i:i + 2], 16) for i in (1, 3, 5)]
    b = [int(bg[i:i + 2], 16) for i in (1, 3, 5)]
    return "#" + "".join(f"{round(x * alpha + y * (1 - alpha)):02x}" for x, y in zip(a, b))


class LineChart(tk.Canvas):
    """Plots one or more series that share a y axis.

    set_data() takes one list of numbers per series; the newest value is last.
    """

    def __init__(self, master, title: str, series: Sequence[tuple[str, str]],
                 fmt: Callable[[float], str], colors: dict, capacity: int = 120,
                 fixed_max: float | None = None, **kw):
        super().__init__(master, highlightthickness=0, bg=colors["panel"], height=150, **kw)
        self.title = title
        self.series = list(series)
        self.fmt = fmt
        self.c = colors
        self.capacity = capacity
        self.fixed_max = fixed_max
        self.data: list[list[float]] = [[] for _ in self.series]
        self.bind("<Configure>", lambda e: self.redraw())

    def set_data(self, data: list[list[float]]) -> None:
        self.data = [d[-self.capacity:] for d in data]
        self.redraw()

    def redraw(self) -> None:
        self.delete("all")
        w, h = self.winfo_width(), self.winfo_height()
        if w < 50 or h < 50:
            return
        left, right, top, bottom = 8, 8, 26, 8
        pw, ph = w - left - right, h - top - bottom

        self.create_text(left, 6, anchor="nw", text=self.title, fill=self.c["fg"],
                         font=("Segoe UI", 10, "bold"))

        peak = max((max(d) for d in self.data if d), default=0)
        ymax = self.fixed_max if self.fixed_max else max(peak * 1.15, 1e-9)

        self.create_rectangle(left, top, left + pw, top + ph, outline=self.c["grid"])
        for i in range(1, 4):
            y = top + ph * i / 4
            self.create_line(left, y, left + pw, y, fill=self.c["grid"], dash=(2, 4))
        self.create_text(left + pw - 4, top + 3, anchor="ne", text=self.fmt(ymax),
                         fill=self.c["muted"], font=("Segoe UI", 8))

        # Legend with the latest value of each series, right-aligned in the title row.
        x = w - right
        for (label, color), d in reversed(list(zip(self.series, self.data))):
            txt = f"{label} {self.fmt(d[-1]) if d else '-'}"
            item = self.create_text(x, 6, anchor="ne", text=txt, fill=color,
                                    font=("Segoe UI", 9))
            bbox = self.bbox(item)
            x = (bbox[0] if bbox else x) - 14

        step = pw / max(self.capacity - 1, 1)
        lines = []
        for (label, color), d in zip(self.series, self.data):
            if len(d) < 2:
                continue
            offset = self.capacity - len(d)
            pts = []
            for i, v in enumerate(d):
                pts.append(left + (offset + i) * step)
                pts.append(top + ph - min(v / ymax, 1.0) * ph)
            fill_pts = [pts[0], top + ph] + pts + [pts[-2], top + ph]
            self.create_polygon(fill_pts, fill=_blend(color, self.c["panel"], 0.2), outline="")
            lines.append((pts, color))
        # Lines go on top of every fill so no series hides another.
        for pts, color in lines:
            self.create_line(pts, fill=color, width=2)
