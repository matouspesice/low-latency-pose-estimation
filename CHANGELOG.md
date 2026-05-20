# Changelog

All notable project changes are documented in this file.

## 20.05.2026

- `architect`: hid OCR, Pose Dodge, Single-Leg Balance, and Lean Balance from the start menu (kept Coin Collector, Pose Test, Clock ROI). Clock ROI description no longer references OCR; split view keeps live clock region and pose avatar visible for latency measurement.
- `app/pose.py`: clock ROI is always JPEG-encoded after pose inference and bundled in the same UDP JSON packet as keypoints (`roiImageBase64`), so motion-to-photon measurement reflects the full pipeline. Removed separate clock datagrams and `--clock-stream-separate-udp` / `clock_stream_separate_udp` config.
- `README.md`, `architect/Assets/Scripts/ClockMode.cs`, `PoseReceiver.cs`: documentation and comments aligned with bundled clock+pose UDP.

## 06.05.2026

- `app/README.md`: added a dedicated Windows FLIR setup section covering Spinnaker installation, PySpin wheel installation in `.venv310`, validation command, and the exact `--camera-mode flir` runtime command. This closes a documentation gap that caused confusion when SpinView was installed but PySpin was missing in the active Python environment.
- `app/README.md`: corrected the local Spinnaker wheel filename in the install command to match the bundled SDK package (`spinnaker_python-4.3.0.189-cp310-cp310-win_amd64.whl`), and added a note for version-dependent wheel names.
- `app/requirements.txt`: clarified that Spinnaker/PySpin is not installed by `pip install -r requirements.txt` and must be installed manually from the local SDK wheel for FLIR mode.
- `README.md`: added explicit pointers to the GPU setup guide (`app/SETUP_GPU.md`) and FLIR setup instructions (`app/README.md`).
- `docs/POSE_PROCESSING_OPTIMIZATION.md`: added a dedicated technical note describing the new keypoint-processing stage (confidence gate, low-confidence hold, outlier clamp, adaptive smoothing), parameter semantics, and webcam A/B latency results.
- `README.md` and `app/README.md`: integrated links and short summaries of the processing pipeline so operators can discover the feature and quickly switch between processed and raw (`--proc-disable`) modes.
- `app/pose_abtest.json`: added a reproducible webcam benchmark profile (`no_window`, `no_viz`, `log_latency`) for quick raw-vs-processed comparisons without changing the main runtime config.

## 05.05.2026

- `app/pose.py`: added a latency-safe pose post-processing stage before UDP send with confidence gating, per-joint outlier jump clamping, and adaptive EMA smoothing. New CLI/config controls: `proc_enable`, `proc_min_confidence`, `proc_max_jump`, `proc_hold_low_conf`, `proc_alpha`, and `proc_min_alpha`.
- `app/pose.json`: added defaults for the new processing controls so the runtime is stable by default while still allowing raw-keypoint (`--proc-disable`) A/B latency testing.
