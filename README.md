# Low-Latency Pose Estimation Project Pipeline

This repository contains the implementation workspace for the low-latency body-pose pipeline and Unity integrations.

## Repository Scope

This repo is for implementation and collaboration only:

- Python runtime and OCR/pose tools: `app/`
- Main Unity integration project: `architect/` (includes the **Coin Collector** body-tilt mini-game; the old standalone CollectCoins tree was removed to avoid duplication)

## Prerequisites

- Windows 10/11
- Python 3.10 (recommended for current runtime compatibility)
- Unity Hub + matching Unity version used by the project
- Git

Optional:

- NVIDIA GPU + CUDA/cuDNN (for accelerated ONNX runtime)
- FLIR/Spinnaker SDK for FLIR camera mode

## Initial Setup

1. Clone repository:

```powershell
git clone https://github.com/matouspesice/low-latency-pose-estimation.git
cd "low-latency-pose-estimation"
```

2. Python environment (recommended):

```powershell
cd app
python -m venv .venv310
.\.venv310\Scripts\activate
pip install -r requirements.txt
```

3. Unity:

- Open `architect/` in Unity Hub.
- Let Unity import packages and generate local cache folders.

## Running the Pipeline

From `app/`:

```powershell
.\.venv310\Scripts\activate
python pose.py
```

Adjust runtime options/config in `app/pose.json` as needed.

**Per-frame latency CSV:** disabled by default (`log_latency: false`, empty `latency_csv`). To record a run, set `"log_latency": true` and e.g. `"latency_csv": "latency_live.csv"`, or use `--log-latency --latency-csv path.csv` on the command line.

## Coin Collector (Architect) — tuning from the original prototype

The former standalone CollectCoins project documented **low-latency body tilt → ball** behaviour. The same ideas apply to `CoinMineGameManager` in `architect/`:

- **BodyTiltInput → Output Smoothing:** keep at **0** on `PoseBridge` for the snappiest mapping (smoothing adds lag).
- **VSync / frame rate:** disable VSync in **Quality** settings if you want uncapped FPS; optionally set **Coin Mine → Target Frame Rate While Playing** on `CoinMineGameManager` (e.g. 120) so `Application.targetFrameRate` is raised only during a round.
- **Fixed Timestep:** lower **Edit → Project Settings → Time → Fixed Timestep** (e.g. 0.0083 ≈ 120 Hz) for slightly snappier physics at CPU cost.
- **Warm-up lane:** the first coin row is placed farther ahead (~2 s of travel at default forward speed) so you can settle before collecting.

More detail: `docs/COIN_COLLECTOR_LATENCY.md`.

## Pose processing optimization (keypoint conditioning)

The runtime now includes an optional, latency-safe processing stage before sending keypoints to Unity:

- confidence gating for weak joints,
- outlier jump clamping,
- adaptive smoothing for jitter reduction.

Default is enabled in `app/pose.json` (`proc_enable: true`).  
Use `--proc-disable` for raw baseline runs.

Detailed description and benchmark numbers: `docs/POSE_PROCESSING_OPTIMIZATION.md`.

## Live clock ROI image stream (Unity)

The **Architect** Unity project can show a **low-latency camera crop** (where a wall clock sits) while pose keypoints still drive games and the avatar.

**Python (`pose.py`)**

- Enable streaming: `--clock-stream-enable` (or `"clock_stream_enable": true` in `pose.json`).
- **ROI** in normalized coordinates `[0,1]`: `--clock-roi x,y,w,h` or interactive selection in the preview window (**C** = clock ROI, **O** = OCR ROI, **X** = clear).
- **UDP** must match Unity: `--udp-port 5555` and `--udp-host 127.0.0.1` (or your PC’s IP).
- **Latency-oriented defaults** (also in `pose.json`): ROI is JPEG-encoded, optionally **downscaled** (`--clock-downscale`, default longest side 192 px), and by default sent as a **separate small UDP datagram right after capture** (`--clock-stream-separate-udp`, default on) so the image is not delayed until pose inference finishes. Pose JSON is still sent on its usual cadence.
- Tunables: `--clock-stream-max-fps`, `--clock-stream-jpeg-quality`, `--no-clock-stream-separate-udp` to bundle ROI in the pose packet (legacy).

**Unity (`architect/`)**

- `PoseReceiver` listens on its **Port** field (e.g. 5555) and parses JSON; large datagrams use a **128 KB** receive buffer so Windows does not drop oversized JPEG payloads.
- `PoseMessage` includes `roiImageBase64` (JPEG, base64).
- **Clock** game mode (`ClockMode.cs`) decodes the latest ROI into a `RawImage` when that mode is active.

Run `pose.py` with clock streaming enabled, press **Play** in Unity, choose **Clock** from the Architect menu, and align the Python ROI with the clock on the wall.

## Collaboration Workflow

- Create a feature branch per task:

```powershell
git checkout -b feature/<short-name>
```

- Keep commits focused and small.
- Open a Pull Request for review before merging to `main`.
- Do not commit local environments or generated Unity cache/build folders.

