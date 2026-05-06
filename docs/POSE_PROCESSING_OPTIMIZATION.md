# Pose Data Processing Optimization

This document explains the pose data processing layer added in `app/pose.py`, why it exists, and how to tune it.

## Purpose

Raw per-frame keypoints can contain confidence dropouts and one-frame spikes (especially wrists/ankles during fast motion or partial occlusion).  
The processing layer improves avatar/game stability while keeping latency impact low.

The stage runs after inference and before UDP send to Unity.

## Processing pipeline

For each keypoint `(x, y, s)`:

1. **Confidence gate**
   - If `s < proc_min_confidence`, the sample is considered unreliable.
2. **Low-confidence handling**
   - If `proc_hold_low_conf=true`, keep the previous valid point.
   - Otherwise, pass the raw point through.
3. **Outlier clamp**
   - Compute frame-to-frame jump distance from previous point.
   - If distance exceeds `proc_max_jump`, clamp movement to the limit.
4. **Adaptive EMA smoothing**
   - Blend previous and current point with a dynamic alpha in `[proc_min_alpha, proc_alpha]`.
   - Alpha increases for high confidence and larger intentional motion.

This uses only current + previous frame state (no multi-frame buffering), so compute cost stays small.

## Runtime controls

All options can be set in `pose.json` or via CLI.

- `proc_enable` / `--proc-enable` / `--proc-disable`
- `proc_min_confidence` / `--proc-min-confidence`
- `proc_max_jump` / `--proc-max-jump`
- `proc_hold_low_conf` / `--proc-hold-low-conf` / `--no-proc-hold-low-conf`
- `proc_alpha` / `--proc-alpha`
- `proc_min_alpha` / `--proc-min-alpha`

## Defaults (current)

From `app/pose.json`:

- `proc_enable: true`
- `proc_min_confidence: 0.25`
- `proc_max_jump: 0.12`
- `proc_hold_low_conf: true`
- `proc_alpha: 0.55`
- `proc_min_alpha: 0.25`

These values are set to balance responsiveness and stability for webcam use.

## Latency impact (webcam A/B, 300 frames)

Benchmark settings:

- `camera_mode=webcam`, `backend=rtmlib`, `device=cuda`
- `width=640`, `height=480`, `det_frequency=10`
- `no_window=true`, `no_viz=true`, `log_latency=true`

Results:

- **Raw (`--proc-disable`)**
  - mean: `13.82 ms`
  - median: `11.02 ms`
  - p95: `38.16 ms`
- **Processed (default)**
  - mean: `15.04 ms`
  - median: `12.60 ms`
  - p95: `44.57 ms`

Observed overhead in this run:

- mean: `+1.22 ms`
- median: `+1.58 ms`
- p95: `+6.41 ms`

## Recommended usage

- Keep processing **enabled** for normal Unity gameplay (more stable avatar control).
- Use `--proc-disable` only for raw baseline measurements or debugging.
- Keep clock image streaming (`clock_stream_enable`) off by default unless running Clock mode experiments.

## Tuning guidance

- Increase `proc_min_confidence` if joints are noisy (can increase hold/freeze behavior).
- Decrease `proc_max_jump` to suppress spikes more aggressively.
- Increase `proc_alpha` for more responsiveness (less smoothing).
- Decrease `proc_min_alpha` for stronger smoothing in uncertain frames.
