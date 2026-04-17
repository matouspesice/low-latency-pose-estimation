# App plan — real-time pose from webcam

## Goal

- **Now:** One app that shows your webcam stream with **pose estimation (RTMPose)** overlaid in real time. No Unity; just a window.
- **Later:** Same pipeline can drive the Architect game (Unity or other); camera source switch (home webcam vs lab USB) without code changes.

## Phases

| Phase | What | Status |
|-------|------|--------|
| **1** | Webcam → pose (rtmlib/RTMPose) → draw skeleton → display in window | This plan |
| **2** | Camera source configurable (default = 0 for built-in; lab = 1 or by name) | In Phase 1 |
| **3** | Optional: FPS/latency display for testing | Optional |
| **4** | Later: Connect pose stream to Unity / Architect game | Not in scope yet |

## PC / tech

- **No special specs needed** for the first version. The app uses:
  - Default webcam (device index `0`).
  - **rtmlib** (RTMPose models via ONNX) — runs on **CPU** by default; add GPU later if you have CUDA.
- **Camera switching:** Change one setting (e.g. `--camera 0` at home, `--camera 1` in lab) or a config file (`pose.json`; see `pose.example.json`). No code change.

## Where things live

- All app code in **`app/`** (this folder).
- **Pose stack:** rtmlib (RTMPose) — you do **not** need to download the full MMPose repo; rtmlib installs with `pip install rtmlib` and downloads ONNX models on first run.
- **Unity** (when you add it) can live in a subfolder (e.g. `app/unity/`) and receive pose via a small bridge (e.g. UDP/JSON or shared file); not part of this initial step.

## Run (after implementation)

```bash
cd app
pip install -r requirements.txt
python pose.py
# Or with lab camera:
python pose.py --camera 1
```

Press **Q** in the window to quit.
