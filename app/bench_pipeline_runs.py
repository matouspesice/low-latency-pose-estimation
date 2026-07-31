#!/usr/bin/env python3
"""
Run multiple pose.py configurations back-to-back (Unity can stay open) and build a comparison report.

Usage:
  1. Start Unity Play with PoseReceiver on port 5555 and pipeline trace path set (optional).
  2. Edit bench_runs.json (copy from bench_runs.example.json).
  3. python bench_pipeline_runs.py
     python bench_pipeline_runs.py --config my_bench.json

Each run writes traces/<label>.txt with the full command line logged at session_start.
Default: 200 frames per run (~7 s at 30 FPS) — enough for stable mean latency (see analyzer notes).
"""

from __future__ import annotations

import argparse
import json
import subprocess
import sys
import time
from pathlib import Path

_SCRIPT_DIR = Path(__file__).resolve().parent


def _resolve_python(cfg: dict) -> str:
    py = cfg.get("python") or sys.executable
    p = Path(py)
    if not p.is_absolute():
        p = _SCRIPT_DIR / p
    if p.exists():
        return str(p.resolve())
    return py


def main() -> int:
    p = argparse.ArgumentParser(description="Benchmark multiple pose.py setups sequentially.")
    p.add_argument(
        "--config",
        type=str,
        default=str(_SCRIPT_DIR / "bench_runs.json"),
        help="JSON config (default: bench_runs.json next to this script)",
    )
    p.add_argument("--dry-run", action="store_true", help="Print commands only.")
    args = p.parse_args()

    cfg_path = Path(args.config)
    if not cfg_path.is_file():
        print(f"Config not found: {cfg_path}", file=sys.stderr)
        print("Copy bench_runs.example.json to bench_runs.json and edit it.", file=sys.stderr)
        return 1

    with cfg_path.open("r", encoding="utf-8") as f:
        cfg = json.load(f)

    frames = int(cfg.get("frames_per_run", 200))
    pause = float(cfg.get("pause_between_runs_sec", 3))
    trace_dir = _SCRIPT_DIR / cfg.get("trace_dir", "traces")
    trace_dir.mkdir(parents=True, exist_ok=True)
    report_out = _SCRIPT_DIR / cfg.get("report_output", "pipeline_trace_report.html")
    base_args = list(cfg.get("base_args", ["--udp-port", "5555", "--no-window"]))
    runs = cfg.get("runs", [])
    if not runs:
        print("No runs defined in config.", file=sys.stderr)
        return 1

    python_exe = _resolve_python(cfg)
    pose_py = str(_SCRIPT_DIR / "pose.py")
    trace_files: list[Path] = []

    def expand_base_args(trace_path: Path) -> list[str]:
        """Insert trace file path immediately after --pipeline-trace."""
        out: list[str] = []
        i = 0
        while i < len(base_args):
            tok = base_args[i]
            out.append(tok)
            if tok == "--pipeline-trace":
                out.append(str(trace_path))
            i += 1
        return out

    print(f"Benchmark: {len(runs)} runs × {frames} frames, trace dir = {trace_dir}")
    print("Keep Unity running between runs.\n")

    for i, run in enumerate(runs):
        label = run.get("label") or f"run_{i+1}"
        trace_path = trace_dir / f"{label}.txt"
        if trace_path.exists():
            trace_path.unlink()

        run_args = list(run.get("args", []))
        if "--run-label" not in run_args and "-run-label" not in " ".join(run_args):
            run_args = ["--run-label", label] + run_args

        cmd = [
            python_exe,
            pose_py,
            *expand_base_args(trace_path),
            "--max-frames",
            str(frames),
            *run_args,
        ]
        print(f"[{i+1}/{len(runs)}] {label}")
        print(f"  {' '.join(cmd)}")

        if args.dry_run:
            trace_files.append(trace_path)
            continue

        rc = subprocess.run(cmd, cwd=str(_SCRIPT_DIR))
        if rc.returncode != 0:
            print(f"  Run failed (exit {rc.returncode}). Stopping.", file=sys.stderr)
            return rc.returncode
        trace_files.append(trace_path)
        if i < len(runs) - 1 and pause > 0:
            print(f"  Pause {pause:.0f}s before next run…")
            time.sleep(pause)

    if args.dry_run:
        print("\nDry run complete.")
        return 0

    analyze = _SCRIPT_DIR / "analyze_pipeline_trace.py"
    labels = [r.get("label") or f"run_{i+1}" for i, r in enumerate(runs)]
    analyze_cmd = [
        python_exe,
        str(analyze),
        *[str(p) for p in trace_files],
        "--labels",
        *labels,
        "--output",
        str(report_out),
        "--title",
        "Pipeline benchmark comparison",
    ]
    print("\nBuilding report…")
    print("  " + " ".join(analyze_cmd))
    rc = subprocess.run(analyze_cmd, cwd=str(_SCRIPT_DIR))
    if rc.returncode == 0:
        print(f"\nDone. Open: {report_out.resolve()}")
    return rc.returncode


if __name__ == "__main__":
    raise SystemExit(main())
