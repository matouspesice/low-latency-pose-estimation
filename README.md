# Low-Latency Pose Estimation Project Pipeline

This repository contains the implementation workspace for the low-latency body-pose pipeline and Unity integrations.

## Repository Scope

This repo is for implementation and collaboration only:

- Python runtime and OCR/pose tools: `app/`
- Main Unity integration project: `architect/`
- Secondary Unity project experiments: `Coin-Collector-main/`

Thesis writing/documentation is maintained in a separate private repository.

## Prerequisites

- Windows 10/11
- Python 3.10 (recommended for current runtime compatibility)
- Unity Hub + matching Unity version used by the project
- Git

Optional:

- NVIDIA GPU + CUDA/cuDNN (for accelerated ONNX runtime)
- FLIR/Spinnaker SDK for FLIR camera mode

## Initial Setup

1. Clone repository

```powershell
git clone https://github.com/matouspesice/low-latency-pose-estimation.git
cd "low-latency-pose-estimation"
```

2. Python environment (recommended)

```powershell
cd app
python -m venv .venv310
.\.venv310\Scripts\activate
pip install -r requirements.txt
```

3. Unity

- Open `architect/` in Unity Hub.
- Let Unity import packages and generate local cache folders.

## Running the Pipeline

From `app/`:

```powershell
.\.venv310\Scripts\activate
python pose.py
```

Adjust runtime options/config in `app/pose.json` as needed.

## Collaboration Workflow

- Create a feature branch per task:

```powershell
git checkout -b feature/<short-name>
```

- Keep commits focused and small.
- Open a Pull Request for review before merging to `main`.
- Do not commit local environments or generated Unity cache/build folders.

## Notes on Large Assets

- This repository currently avoids broad Git LFS tracking to prevent missing-object push failures.
- If you need to add large binary assets, coordinate first and add tracking rules intentionally.

## Common Windows Path Tip

If a folder name contains spaces, quote it:

```powershell
cd "project pipeline"
```

or

```powershell
Set-Location "project pipeline"
```
