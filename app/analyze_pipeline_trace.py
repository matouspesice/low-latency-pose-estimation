#!/usr/bin/env python3
"""
Analyze pipeline_trace.txt logs: per-step latency stats and compare multiple runs.

Usage:
  python analyze_pipeline_trace.py pipeline_trace.txt
  python analyze_pipeline_trace.py run_webcam.txt run_flir.txt --labels "Webcam" "FLIR"
  python analyze_pipeline_trace.py traces/*.txt --output comparison.html

Rename or copy pipeline_trace.txt between runs (e.g. trace_webcam.txt) so each
configuration keeps its own file; pose.py appends to the same path by default.
"""

from __future__ import annotations

import argparse
import html
import re
import statistics
import sys
from dataclasses import dataclass, field
from datetime import datetime
from pathlib import Path
from typing import Iterable

LINE_RE = re.compile(
    r"^(?P<ts>\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3}) \| (?P<source>\w+) \| stage=(?P<stage>[\w_]+)"
    r"(?: \| seq=(?P<seq>\d+))?(?: \| (?P<extra>.+))?$"
)

TS_FMT = "%Y-%m-%d %H:%M:%S.%f"


@dataclass
class TraceEvent:
    ts: datetime
    source: str
    stage: str
    seq: int | None
    extra: dict[str, str] = field(default_factory=dict)


@dataclass
class IntervalStats:
    name: str
    label: str
    count: int
    mean_ms: float
    median_ms: float
    p95_ms: float
    min_ms: float
    max_ms: float

    @property
    def sort_key(self) -> float:
        return self.mean_ms


@dataclass
class RunMetadata:
    cmd: str = ""
    run_label: str = ""
    backend: str = ""
    device: str = ""
    proc_enable: str = ""
    camera_mode: str = ""
    extra: dict[str, str] = field(default_factory=dict)


# Non-overlapping segments that stack to the pipeline total (in order).
STACK_SEGMENTS: list[tuple[str, str, str]] = [
    ("python_inference", "Inference", "#4285f4"),
    ("python_postproc", "Post-processing", "#34a853"),
    ("python_udp_pack", "UDP pack/send", "#fbbc04"),
    ("unity_network", "Unity: network", "#ea4335"),
    ("unity_parse", "Unity: JSON parse", "#ab47bc"),
    ("unity_pose_ready", "Unity: pose ready", "#00acc1"),
]

RECOMMENDED_MIN_FRAMES = 150


def _parse_extra(extra: str | None) -> dict[str, str]:
    out: dict[str, str] = {}
    if not extra:
        return out
    for part in extra.split(" | "):
        if "=" in part:
            k, v = part.split("=", 1)
            out[k.strip()] = v.strip()
    return out


def parse_trace_file(path: Path) -> list[list[TraceEvent]]:
    """Return list of sessions (each session = list of events)."""
    sessions: list[list[TraceEvent]] = []
    current: list[TraceEvent] = []

    with path.open("r", encoding="utf-8", errors="replace") as f:
        for raw in f:
            line = raw.strip()
            if not line:
                continue
            m = LINE_RE.match(line)
            if not m:
                continue
            ts = datetime.strptime(m.group("ts"), TS_FMT)
            ev = TraceEvent(
                ts=ts,
                source=m.group("source"),
                stage=m.group("stage"),
                seq=int(m.group("seq")) if m.group("seq") else None,
                extra=_parse_extra(m.group("extra")),
            )
            if ev.stage == "session_start":
                if current:
                    sessions.append(current)
                current = [ev]
                continue
            if ev.stage == "session_end":
                if current:
                    current.append(ev)
                    sessions.append(current)
                    current = []
                continue
            current.append(ev)

    if current:
        sessions.append(current)
    if not sessions:
        # No explicit session markers — treat whole file as one session.
        events: list[TraceEvent] = []
        with path.open("r", encoding="utf-8", errors="replace") as f:
            for raw in f:
                m = LINE_RE.match(raw.strip())
                if m:
                    events.append(
                        TraceEvent(
                            ts=datetime.strptime(m.group("ts"), TS_FMT),
                            source=m.group("source"),
                            stage=m.group("stage"),
                            seq=int(m.group("seq")) if m.group("seq") else None,
                            extra=_parse_extra(m.group("extra")),
                        )
                    )
        if events:
            sessions = [events]
    return sessions


def _ms(a: datetime, b: datetime) -> float:
    return (b - a).total_seconds() * 1000.0


def _stats(name: str, label: str, values: list[float]) -> IntervalStats | None:
    if not values:
        return None
    vals = sorted(values)
    n = len(vals)
    p95_i = min(int(0.95 * n), n - 1)
    return IntervalStats(
        name=name,
        label=label,
        count=n,
        mean_ms=statistics.mean(vals),
        median_ms=statistics.median(vals),
        p95_ms=vals[p95_i],
        min_ms=vals[0],
        max_ms=vals[-1],
    )


def extract_metadata(sessions: list[list[TraceEvent]]) -> RunMetadata:
    for session in sessions:
        for ev in session:
            if ev.stage == "session_start":
                meta = RunMetadata(
                    cmd=ev.extra.get("cmd", ""),
                    run_label=ev.extra.get("run_label", ""),
                    backend=ev.extra.get("backend", ""),
                    device=ev.extra.get("device", ""),
                    proc_enable=ev.extra.get("proc_enable", ""),
                    camera_mode=ev.extra.get("camera_mode", ""),
                    extra=dict(ev.extra),
                )
                return meta
    return RunMetadata()


def analyze_file(
    path: Path, label: str | None = None
) -> tuple[str, dict[str, IntervalStats], int, RunMetadata]:
    sessions = parse_trace_file(path)
    all_intervals: dict[str, list[float]] = {}
    frame_count = 0

    for session in sessions:
        raw = _analyze_session_raw(session)
        frame_count += sum(1 for ev in session if ev.stage == "image_received" and ev.source == "python")
        for name, vals in raw.items():
            all_intervals.setdefault(name, []).extend(vals)

    labels_map = {
        "python_capture_to_send": "Python: frame in → UDP sent",
        "python_inference": "Python: inference",
        "python_postproc": "Python: post-processing",
        "python_udp_pack": "Python: pack + send",
        "unity_network": "Unity: UDP → received",
        "unity_parse": "Unity: parse JSON",
        "unity_pose_ready": "Unity: parse → pose ready",
        "e2e_pose_ready": "End-to-end: frame in → pose ready",
        "infer_ms_field": "Python: infer_ms (logged)",
    }
    result: dict[str, IntervalStats] = {}
    for name, vals in all_intervals.items():
        st = _stats(name, labels_map.get(name, name), vals)
        if st:
            result[name] = st
    meta = extract_metadata(sessions)
    run_label = label or meta.run_label or path.stem
    return run_label, result, frame_count, meta


def _total_metric(stats: dict[str, IntervalStats]) -> tuple[str, float] | None:
    if "e2e_pose_ready" in stats:
        return "e2e_pose_ready", stats["e2e_pose_ready"].mean_ms
    if "python_capture_to_send" in stats:
        return "python_capture_to_send", stats["python_capture_to_send"].mean_ms
    return None


def _stack_breakdown(stats: dict[str, IntervalStats]) -> list[tuple[str, str, str, float, float]]:
    """Return (key, label, color, mean_ms, percent) for stacked bar; includes gap if needed."""
    total_info = _total_metric(stats)
    if not total_info:
        return []
    _total_key, total_ms = total_info
    if total_ms <= 0:
        return []
    rows: list[tuple[str, str, str, float, float]] = []
    accounted = 0.0
    for key, label, color in STACK_SEGMENTS:
        if key not in stats:
            continue
        ms = stats[key].mean_ms
        accounted += ms
        pct = 100.0 * ms / total_ms
        rows.append((key, label, color, ms, pct))
    gap = max(0.0, total_ms - accounted)
    if gap >= 0.05:
        rows.append(("gap", "Other / gap", "#5f6368", gap, 100.0 * gap / total_ms))
    return rows


def _analyze_session_raw(events: list[TraceEvent]) -> dict[str, list[float]]:
    by_seq_py: dict[int, dict[str, datetime]] = {}
    by_seq_unity: dict[int, dict[str, datetime]] = {}
    infer_ms_logged: list[float] = []

    for ev in events:
        if ev.seq is None or ev.seq < 0:
            continue
        bucket = by_seq_py if ev.source == "python" else by_seq_unity
        bucket.setdefault(ev.seq, {})[ev.stage] = ev.ts
        if ev.stage == "inference_done" and "infer_ms" in ev.extra:
            try:
                infer_ms_logged.append(float(ev.extra["infer_ms"]))
            except ValueError:
                pass

    def py(s: int) -> dict[str, datetime]:
        return by_seq_py.get(s, {})

    def uni(s: int) -> dict[str, datetime]:
        return by_seq_unity.get(s, {})

    all_seqs = sorted(set(by_seq_py) | set(by_seq_unity))
    out: dict[str, list[float]] = {
        "python_capture_to_send": [],
        "python_inference": [],
        "python_postproc": [],
        "python_udp_pack": [],
        "unity_network": [],
        "unity_parse": [],
        "unity_pose_ready": [],
        "e2e_pose_ready": [],
        "infer_ms_field": infer_ms_logged,
    }

    for s in all_seqs:
        p, u = py(s), uni(s)
        if "image_received" in p and "sent_to_unity" in p:
            out["python_capture_to_send"].append(_ms(p["image_received"], p["sent_to_unity"]))
        if "processing_started" in p and "inference_done" in p:
            out["python_inference"].append(_ms(p["processing_started"], p["inference_done"]))
        if "inference_done" in p and "postproc_done" in p:
            out["python_postproc"].append(_ms(p["inference_done"], p["postproc_done"]))
        if "postproc_done" in p and "sent_to_unity" in p:
            out["python_udp_pack"].append(_ms(p["postproc_done"], p["sent_to_unity"]))
        if "sent_to_unity" in p and "unity_udp_received" in u:
            out["unity_network"].append(_ms(p["sent_to_unity"], u["unity_udp_received"]))
        if "unity_udp_received" in u and "unity_packet_parsed" in u:
            out["unity_parse"].append(_ms(u["unity_udp_received"], u["unity_packet_parsed"]))
        if "unity_packet_parsed" in u and "unity_pose_ready" in u:
            out["unity_pose_ready"].append(_ms(u["unity_packet_parsed"], u["unity_pose_ready"]))
        if "image_received" in p and "unity_pose_ready" in u:
            out["e2e_pose_ready"].append(_ms(p["image_received"], u["unity_pose_ready"]))

    return {k: v for k, v in out.items() if v}


DISPLAY_ORDER = [
    "python_capture_to_send",
    "python_inference",
    "infer_ms_field",
    "python_postproc",
    "python_udp_pack",
    "unity_network",
    "unity_parse",
    "unity_pose_ready",
    "e2e_pose_ready",
]


def _sample_guidance(frames: int) -> str:
    if frames >= RECOMMENDED_MIN_FRAMES:
        return (
            f"<p class='ok'>Sample size: <strong>{frames}</strong> frames — sufficient for stable means "
            f"(target ≥ {RECOMMENDED_MIN_FRAMES}, ~5–10 s at 30 FPS).</p>"
        )
    return (
        f"<p class='warn'>Sample size: <strong>{frames}</strong> frames — below recommended "
        f"{RECOMMENDED_MIN_FRAMES}. Use <code>bench_pipeline_runs.py</code> with "
        f"<code>frames_per_run: 200</code> or more for reliable comparison.</p>"
    )


def _stacked_breakdown_html(runs: list[tuple[str, dict[str, IntervalStats], int]]) -> str:
    """Stacked bars: outer width = total ms (proportional across runs), inner segments = % composition."""
    totals: list[float] = []
    for _lbl, stats, _n in runs:
        t = _total_metric(stats)
        if t:
            totals.append(t[1])
    max_total = max(totals) if totals else 1.0

    lines = [
        "<div class='chart-title'>Latency composition (bar length = total time, color = step)</div>",
        "<p class='muted'>Bar <strong>length</strong> is proportional to mean pipeline time (ms). "
        "Colors inside show how much of that time each step uses. "
        f"Longest run: <strong>{max_total:.2f} ms</strong>.</p>",
        "<div class='legend'>",
    ]
    seen: set[str] = set()
    for _lbl, stats, _n in runs:
        for key, label, color in STACK_SEGMENTS:
            if key in stats and key not in seen:
                lines.append(
                    f"<span class='legend-item'><span class='swatch' style='background:{color}'></span>"
                    f"{html.escape(label)}</span>"
                )
                seen.add(key)
    lines.append(
        "<span class='legend-item'><span class='swatch' style='background:#5f6368'></span>Other / gap</span>"
    )
    lines.append("</div>")
    lines.append(
        f"<div class='stack-chart-area'>"
        f"<div class='stack-scale-hint muted'>0 ms<span class='stack-scale-max'>{max_total:.1f} ms</span></div>"
    )

    for label, stats, _n in runs:
        rows = _stack_breakdown(stats)
        if not rows:
            continue
        total_info = _total_metric(stats)
        total_ms = total_info[1] if total_info else 0
        outer_pct = (total_ms / max_total) * 100.0 if max_total > 0 else 0
        segs = "".join(
            f"<span class='stack-seg' style='width:{pct:.1f}%;background:{color}' "
            f"title='{html.escape(lbl)}: {ms:.2f} ms ({pct:.1f}%)'></span>"
            for _k, lbl, color, ms, pct in rows
        )
        detail = " · ".join(f"{lbl} {pct:.0f}%" for _k, lbl, _c, _ms, pct in rows if pct >= 1.0)
        lines.append(
            f"<div class='stack-row'>"
            f"<div class='stack-head'><span class='bar-label'>{html.escape(label)}</span>"
            f"<span class='bar-val'>{total_ms:.2f} ms</span></div>"
            f"<div class='stack-bar-track'>"
            f"<div class='stack-bar-scaled' style='width:{outer_pct:.2f}%'>"
            f"<div class='stack-bar'>{segs}</div></div></div>"
            f"<div class='stack-detail muted'>{html.escape(detail)}</div></div>"
        )
    lines.append("</div>")
    return "\n".join(lines)


def _waterfall_html(label: str, stats: dict[str, IntervalStats]) -> str:
    """Waterfall chart for one run (mean ms per step, cumulative)."""
    rows = _stack_breakdown(stats)
    if not rows:
        return ""
    total_info = _total_metric(stats)
    total_ms = total_info[1] if total_info else sum(r[3] for r in rows)
    cum = 0.0
    bars = []
    for key, lbl, color, ms, pct in rows:
        bars.append(
            f"<div class='wf-step'>"
            f"<div class='wf-label'>{html.escape(lbl)}</div>"
            f"<div class='wf-bar-wrap'>"
            f"<div class='wf-offset' style='width:{(cum / total_ms) * 100 if total_ms else 0:.2f}%'></div>"
            f"<div class='wf-bar' style='width:{(ms / total_ms) * 100 if total_ms else 0:.2f}%;background:{color}' "
            f"title='{ms:.2f} ms ({pct:.1f}%)'></div>"
            f"</div>"
            f"<div class='wf-ms'>{ms:.2f} ms</div></div>"
        )
        cum += ms
    return (
        f"<div class='wf-run'><div class='chart-title'>Waterfall — {html.escape(label)}</div>"
        f"<div class='wf-total'>Total: <strong>{total_ms:.2f} ms</strong></div>"
        + "".join(bars)
        + "</div>"
    )


def _run_cards(runs: list[tuple[str, Path, dict[str, IntervalStats], int, RunMetadata]]) -> str:
    cards = []
    for lbl, path, stats, frames, meta in runs:
        cmd = meta.cmd or "(command not logged — re-run with current pose.py)"
        settings = []
        if meta.backend:
            settings.append(f"backend={meta.backend}")
        if meta.device:
            settings.append(f"device={meta.device}")
        if meta.proc_enable != "":
            settings.append(f"proc={meta.proc_enable}")
        if meta.camera_mode:
            settings.append(f"camera={meta.camera_mode}")
        settings_s = " · ".join(settings) if settings else ""
        cards.append(
            f"<details class='run-card' open>"
            f"<summary><strong>{html.escape(lbl)}</strong> — {path.name} · {frames} frames"
            f"{(' · ' + html.escape(settings_s)) if settings_s else ''}</summary>"
            f"<pre class='cmd'>{html.escape(cmd)}</pre>"
            f"{_sample_guidance(frames)}"
            f"</details>"
        )
    return "".join(cards)


def _table_rows(runs: list[tuple[str, dict[str, IntervalStats]]]) -> str:
    metrics_present: list[str] = []
    for key in DISPLAY_ORDER:
        if any(key in st for _, st in runs):
            metrics_present.append(key)

    header = "<tr><th>Step</th>" + "".join(f"<th>{html.escape(lbl)}</th>" for lbl, _ in runs) + "</tr>"
    body = []
    for key in metrics_present:
        label = runs[0][1][key].label if key in runs[0][1] else key
        cells = [f"<td>{html.escape(label)}</td>"]
        for _, st in runs:
            if key not in st:
                cells.append("<td class='muted'>—</td>")
            else:
                s = st[key]
                cells.append(
                    f"<td><strong>{s.mean_ms:.2f}</strong> ms<br>"
                    f"<span class='muted'>med {s.median_ms:.2f} · p95 {s.p95_ms:.2f} · n={s.count}</span></td>"
                )
        body.append("<tr>" + "".join(cells) + "</tr>")
    return f"<table>{header}{''.join(body)}</table>"


def _ranking(runs: list[tuple[str, dict[str, IntervalStats]]]) -> str:
    """Rank runs by best available end-to-end or python total metric."""
    scored: list[tuple[str, float, str]] = []
    for label, st in runs:
        if "e2e_pose_ready" in st:
            scored.append((label, st["e2e_pose_ready"].mean_ms, "end-to-end (pose ready)"))
        elif "python_capture_to_send" in st:
            scored.append((label, st["python_capture_to_send"].mean_ms, "Python capture → send"))
        else:
            continue
    if not scored:
        return "<p class='muted'>Enable Unity trace on the same file for end-to-end ranking.</p>"
    scored.sort(key=lambda x: x[1])
    lines = ["<ol>"]
    for i, (label, ms, metric) in enumerate(scored):
        medal = ["🥇", "🥈", "🥉"][i] if i < 3 else ""
        lines.append(f"<li>{medal} <strong>{html.escape(label)}</strong> — {ms:.2f} ms mean ({html.escape(metric)})</li>")
    lines.append("</ol>")
    return "\n".join(lines)


def build_html(
    runs: list[tuple[str, Path, dict[str, IntervalStats], int, RunMetadata]],
    title: str,
) -> str:
    run_pairs = [(lbl, st) for lbl, _, st, _, _ in runs]
    run_triples = [(lbl, st, n) for lbl, _, st, n, _ in runs]

    waterfalls = "".join(
        f'<section class="card">{_waterfall_html(lbl, st)}</section>'
        for lbl, st, _n in run_triples
        if _stack_breakdown(st)
    )

    return f"""<!DOCTYPE html>
<html lang="en" data-theme="dark">
<head>
  <meta charset="utf-8">
  <title>{html.escape(title)}</title>
  <style>
    html[data-theme="dark"] {{
      --bg: #0f1115; --card: #1a1d24; --border: #2d323c; --text: #e8eaed; --muted: #9aa0a6;
      --th: #252a33; --code-bg: #0a0c10; --code-text: #bdc1c6; --run-card: #14171c;
      --ok: #81c995; --warn: #f9ab00; --btn-bg: #2d323c; --btn-text: #e8eaed;
      --stack-track: #2d323c; --inset-shadow: inset 0 1px 2px rgba(0,0,0,.35);
    }}
    html[data-theme="light"] {{
      --bg: #f5f7fa; --card: #ffffff; --border: #dadce0; --text: #202124; --muted: #5f6368;
      --th: #f1f3f4; --code-bg: #f8f9fa; --code-text: #3c4043; --run-card: #f8f9fa;
      --ok: #137333; --warn: #b06000; --btn-bg: #e8eaed; --btn-text: #202124;
      --stack-track: #e8eaed; --inset-shadow: inset 0 1px 2px rgba(0,0,0,.08);
    }}
    body {{ font-family: "Segoe UI", system-ui, sans-serif; margin: 2rem; max-width: 1100px;
      background: var(--bg); color: var(--text); line-height: 1.45; transition: background .2s, color .2s; }}
    .top-bar {{ display: flex; align-items: center; justify-content: space-between; gap: 1rem; flex-wrap: wrap; }}
    #theme-toggle {{ font: inherit; font-size: 0.85rem; padding: 0.4rem 0.85rem; border-radius: 6px;
      border: 1px solid var(--border); background: var(--btn-bg); color: var(--btn-text); cursor: pointer; }}
    #theme-toggle:hover {{ filter: brightness(1.08); }}
    h1 {{ font-size: 1.6rem; font-weight: 600; margin: 0; }}
    h2 {{ font-size: 1.05rem; margin-top: 2rem; color: var(--muted); font-weight: 600; }}
    .card {{ background: var(--card); border-radius: 10px; padding: 1.25rem 1.5rem; margin: 1rem 0;
      border: 1px solid var(--border); transition: background .2s; }}
    table {{ border-collapse: collapse; width: 100%; font-size: 0.88rem; }}
    th, td {{ border: 1px solid var(--border); padding: 0.5rem 0.75rem; text-align: left; vertical-align: top; }}
    th {{ background: var(--th); }}
    .muted {{ color: var(--muted); font-size: 0.85rem; }}
    .ok {{ color: var(--ok); font-size: 0.9rem; margin: 0.5rem 0 0; }}
    .warn {{ color: var(--warn); font-size: 0.9rem; margin: 0.5rem 0 0; }}
    .chart-title {{ font-weight: 600; margin-bottom: 0.35rem; font-size: 1rem; }}
    .legend {{ display: flex; flex-wrap: wrap; gap: 0.75rem 1.25rem; margin: 0.75rem 0 1rem; font-size: 0.8rem; }}
    .legend-item {{ display: inline-flex; align-items: center; gap: 0.35rem; }}
    .swatch {{ width: 12px; height: 12px; border-radius: 2px; display: inline-block; }}
    .stack-chart-area {{ margin-top: 0.5rem; }}
    .stack-scale-hint {{ display: flex; justify-content: space-between; font-size: 0.72rem; margin-bottom: 0.35rem; }}
    .stack-row {{ margin: 1.25rem 0; }}
    .stack-head {{ display: flex; justify-content: space-between; margin-bottom: 0.35rem; font-size: 0.9rem; }}
    .stack-bar-track {{ width: 100%; background: var(--stack-track); border-radius: 6px;
      box-shadow: var(--inset-shadow); min-height: 32px; }}
    .stack-bar-scaled {{ min-width: 4px; height: 32px; transition: width .25s ease; }}
    .stack-bar {{ display: flex; height: 32px; width: 100%; border-radius: 6px; overflow: hidden; }}
    .stack-seg {{ display: block; height: 100%; min-width: 2px; transition: opacity .15s; }}
    .stack-seg:hover {{ opacity: 0.85; filter: brightness(1.08); }}
    .stack-detail {{ margin-top: 0.25rem; font-size: 0.78rem; }}
    .bar-label {{ font-weight: 500; }}
    .bar-val {{ font-variant-numeric: tabular-nums; color: var(--muted); }}
    .run-card {{ background: var(--run-card); border: 1px solid var(--border); border-radius: 8px;
      padding: 0.75rem 1rem; margin: 0.5rem 0; }}
    .run-card summary {{ cursor: pointer; }}
    pre.cmd {{ white-space: pre-wrap; word-break: break-all; font-size: 0.78rem; background: var(--code-bg);
      padding: 0.65rem; border-radius: 6px; margin: 0.5rem 0; color: var(--code-text); border: 1px solid var(--border); }}
    .wf-run {{ margin-top: 0.5rem; }}
    .wf-total {{ margin-bottom: 0.75rem; font-size: 0.95rem; }}
    .wf-step {{ display: grid; grid-template-columns: 140px 1fr 72px; gap: 0.5rem; align-items: center;
      margin: 0.35rem 0; font-size: 0.85rem; }}
    .wf-bar-wrap {{ display: flex; height: 22px; background: var(--stack-track); border-radius: 4px; overflow: hidden; }}
    .wf-offset {{ flex-shrink: 0; }}
    .wf-bar {{ height: 100%; border-radius: 0 4px 4px 0; }}
    .wf-ms {{ text-align: right; font-variant-numeric: tabular-nums; color: var(--muted); }}
    .intro {{ color: var(--muted); max-width: 52rem; margin-top: 0.75rem; }}
  </style>
</head>
<body>
  <div class="top-bar">
    <h1>{html.escape(title)}</h1>
    <button type="button" id="theme-toggle" aria-label="Toggle light or dark mode">Light mode</button>
  </div>
  <p class="intro">Automated benchmarks: <code>python bench_pipeline_runs.py</code> (Unity stays open).
  Recommended ≥ {RECOMMENDED_MIN_FRAMES} frames per run (~7 s @ 30 FPS).</p>

  <h2>Runs &amp; commands</h2>
  <div class="card">{_run_cards(runs)}</div>

  <div class="card">
    <h2>Ranking (fastest first)</h2>
    {_ranking(run_pairs)}
  </div>

  <h2>Latency composition</h2>
  <div class="card">{_stacked_breakdown_html(run_triples)}</div>

  <h2>Waterfall (per run)</h2>
  {waterfalls}

  <h2>Summary table</h2>
  <div class="card">{_table_rows(run_pairs)}</div>

  <p class="muted">Generated by analyze_pipeline_trace.py</p>
  <script>
  (function() {{
    var root = document.documentElement;
    var key = 'pipeline-trace-report-theme';
    var stored = localStorage.getItem(key);
    if (stored === 'light' || stored === 'dark') root.setAttribute('data-theme', stored);
    var btn = document.getElementById('theme-toggle');
    function label() {{
      btn.textContent = root.getAttribute('data-theme') === 'light' ? 'Dark mode' : 'Light mode';
    }}
    btn.addEventListener('click', function() {{
      var next = root.getAttribute('data-theme') === 'light' ? 'dark' : 'light';
      root.setAttribute('data-theme', next);
      localStorage.setItem(key, next);
      label();
    }});
    label();
  }})();
  </script>
</body>
</html>"""


def main(argv: Iterable[str] | None = None) -> int:
    p = argparse.ArgumentParser(description="Analyze pipeline trace logs and compare runs.")
    p.add_argument("traces", nargs="*", help="One or more pipeline_trace.txt files")
    p.add_argument(
        "--labels",
        nargs="*",
        help="Display name per trace file (same order as files). Default: file stem.",
    )
    p.add_argument(
        "--output",
        "-o",
        type=str,
        default="",
        help="Write HTML report to this path (default: pipeline_trace_report.html next to first file)",
    )
    p.add_argument("--title", type=str, default="Pipeline latency comparison")
    p.add_argument(
        "--traces-dir",
        type=str,
        default="",
        help="Analyze all *.txt in this directory (ignores positional files if set).",
    )
    args = p.parse_args(list(argv) if argv is not None else None)

    if args.traces_dir:
        dir_path = Path(args.traces_dir)
        paths = sorted(dir_path.glob("*.txt"))
        if not paths:
            print(f"No .txt traces in {dir_path}", file=sys.stderr)
            return 1
    else:
        paths = [Path(t) for t in args.traces]
    if not paths:
        print("Provide trace file(s) or --traces-dir.", file=sys.stderr)
        return 1
    for path in paths:
        if not path.is_file():
            print(f"Error: not found: {path}", file=sys.stderr)
            return 1

    labels = args.labels or []
    runs_out: list[tuple[str, Path, dict[str, IntervalStats], int, RunMetadata]] = []
    for i, path in enumerate(paths):
        label = labels[i] if i < len(labels) else path.stem
        lbl, stats, frames, meta = analyze_file(path, label)
        runs_out.append((lbl, path, stats, frames, meta))
        print(f"\n=== {lbl} ({path.name}, {frames} frames) ===")
        for key in DISPLAY_ORDER:
            if key not in stats:
                continue
            s = stats[key]
            print(f"  {s.label:40}  mean={s.mean_ms:7.2f} ms  median={s.median_ms:7.2f}  p95={s.p95_ms:7.2f}  n={s.count}")

    out_path = Path(args.output) if args.output else paths[0].parent / "pipeline_trace_report.html"
    html_doc = build_html(runs_out, args.title)
    out_path.write_text(html_doc, encoding="utf-8")
    print(f"\nWrote HTML report: {out_path.resolve()}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
