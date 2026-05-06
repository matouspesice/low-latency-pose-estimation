# Changelog

All notable project changes are documented in this file.

## 06.05.2026

- `app/README.md`: added a dedicated Windows FLIR setup section covering Spinnaker installation, PySpin wheel installation in `.venv310`, validation command, and the exact `--camera-mode flir` runtime command. This closes a documentation gap that caused confusion when SpinView was installed but PySpin was missing in the active Python environment.
- `docs/POSE_PROCESSING_OPTIMIZATION.md`: added a dedicated technical note describing the new keypoint-processing stage (confidence gate, low-confidence hold, outlier clamp, adaptive smoothing), parameter semantics, and webcam A/B latency results.
- `README.md` and `app/README.md`: integrated links and short summaries of the processing pipeline so operators can discover the feature and quickly switch between processed and raw (`--proc-disable`) modes.
- `app/pose_abtest.json`: added a reproducible webcam benchmark profile (`no_window`, `no_viz`, `log_latency`) for quick raw-vs-processed comparisons without changing the main runtime config.

## 05.05.2026

- `app/pose.py`: added a latency-safe pose post-processing stage before UDP send with confidence gating, per-joint outlier jump clamping, and adaptive EMA smoothing. New CLI/config controls: `proc_enable`, `proc_min_confidence`, `proc_max_jump`, `proc_hold_low_conf`, `proc_alpha`, and `proc_min_alpha`.
- `app/pose.json`: added defaults for the new processing controls so the runtime is stable by default while still allowing raw-keypoint (`--proc-disable`) A/B latency testing.
