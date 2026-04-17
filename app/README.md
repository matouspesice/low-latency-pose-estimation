# App — ML + Display Source Code

Real-time pose from webcam (RTMPose) and, later, the “Architect” game and avatar/visual feedback.

## Quick start — pose

1. **Install**
   ```bash
   cd app
   pip install -r requirements.txt
   ```
   Default is CPU (no CUDA setup needed). For GPU see **[SETUP_GPU.md](SETUP_GPU.md)** (CUDA 12 + cuDNN 9 + `pip install onnxruntime-gpu`, then `python pose.py --device cuda`).
2. **Run**
   ```bash
   python pose.py
   ```
   Uses default camera (index 0). Press **Q** to quit.
3. **Switch camera** (e.g. lab USB)
   ```bash
   python pose.py --camera 1
   ```
4. **Config file (optional)**  
   Put a `pose.json` in the app folder or current directory to set defaults (camera, device, mode, etc.). Command-line flags override the config. Copy from **`pose.example.json`** and edit. Example for lab (USB camera + lightweight):
   ```json
   { "camera": 1, "mode": "lightweight", "device": "cuda" }
   ```
   Use `--config /path/to/file.json` to load a different file.
5. **Compare backends and measure latency (test both)**
   - **rtmlib (default):** works after `pip install -r requirements.txt` (any supported Python).
   - **MMPose (optional):** needs **Python 3.10 or 3.11** (MMPose/mmcv do not support Python 3.12+ yet). On Python 3.14, use a separate env:
     ```bash
     conda create -n pose python=3.11 -y
     conda activate pose
     cd path\to\Master Thesis\app
     pip install -r requirements.txt -r requirements-mmpose.txt
     ```
   - **Test both backends:**
     ```bash
     # Test 1: rtmlib (ONNX)
     python pose.py --backend rtmlib --show-fps

     # Test 2: MMPose (PyTorch) — only if mmpose installed
     python pose.py --backend mmpose --show-fps
     ```
   - Compare the FPS and “Pose: X ms” shown on the window to see which is faster on your machine.

See **`PLAN.md`** for the full plan (phases, camera switching, no Unity for now).

---

## Low latency / optimization

The app is tuned for minimal latency (e.g. for a high-framerate camera or Unity later). These options help:

| Option | Effect |
|--------|--------|
| **`--device cuda`** | GPU inference (see [SETUP_GPU.md](SETUP_GPU.md)). |
| **`--mode lightweight`** | Smallest rtmlib models (detector 416×416, pose 192×256) → fastest inference. |
| **`--det-frequency N`** | Run person detector every N frames (e.g. `10` or `15`); higher = less detector work, lower latency. |
| **`--width W --height H`** | Capture at lower resolution (e.g. `640`×`480`) so less data is processed. Use `0` for camera default. |
| **`--threaded`** | Run capture + inference in a background thread; main thread only displays. Reduces lag from `imshow`/window. |
| **`--no-viz`** | Skip skeleton overlay (raw frame + stats). Slightly faster; useful for keypoints-only (e.g. piping to Unity). |

**Camera / pipeline behaviour (no flags needed):**

- **Buffer:** The script sets the camera buffer to 1 frame so each `read()` returns the newest frame.
- **Warmup:** A few dummy inferences run at startup so the first real frame isn't slowed by ONNX/GPU init.

**Suggested command for lowest latency:**

```bash
python pose.py --device cuda --mode lightweight --det-frequency 10 --width 640 --height 480 --threaded
```

For keypoints-only (e.g. sending to Unity), add **`--no-viz`**.

**Send pose to Unity (Architect game):** use **`--udp-port 5555`** (or another port). The app will broadcast each pose as JSON to `127.0.0.1:5555`. In Unity, set **PoseReceiver** to the same port. Example: `python pose.py --udp-port 5555 --no-viz`

---

## Contents

- **`pose.py`** — Webcam → pose (rtmlib or MMPose) → skeleton overlay. Use `--backend rtmlib|mmpose`; see **Low latency / optimization** above for flags like `--threaded`, `--mode lightweight`, `--width`/`--height`.
- **`pose.example.json`** — Example config file; copy to `pose.json` and edit for camera/device/mode defaults.
- **`requirements.txt`** — opencv-python, rtmlib, onnxruntime (optional: onnxruntime-gpu).
- **`requirements-mmpose.txt`** — Optional MMPose stack for `--backend mmpose`.
- **`PLAN.md`** — Phases, camera/config, and what comes next.

Pose stack uses **rtmlib** (RTMPose models); you do **not** need to download the full MMPose repo. Models are downloaded automatically on first run.

**Console messages:** You may see rtmlib lines like “load … onnx” and “Tracking is on” (normal), and sometimes OpenCV/videoio warnings (e.g. MSMF on Windows). These are usually harmless; the app reduces OpenCV log level to limit noise.

Build outputs and large binaries stay out of version control (see root `.gitignore`).
