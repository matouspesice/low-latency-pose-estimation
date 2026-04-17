# GPU setup for pose.py

Using the GPU makes pose inference faster and the video smoother. Follow these steps on **Windows**.

## 1. Check your GPU

You need an **NVIDIA** GPU. In a terminal:

```bash
nvidia-smi
```

If that works, your driver is installed. Note the "CUDA Version" at the top right (e.g. 12.4). If `nvidia-smi` is not found, install the latest [NVIDIA Game Ready / Studio Driver](https://www.nvidia.com/Download/index.aspx) for your card.

---

## 2. Install CUDA Toolkit 12

- Go to: https://developer.nvidia.com/cuda-downloads  
- Choose: **Windows → x86_64 → your Windows version → exe (local)**  
- Run the installer. Use default options.  
- It installs to something like `C:\Program Files\NVIDIA GPU Computing Toolkit\CUDA\v12.x`

The installer usually adds CUDA to your **PATH**. If not, add manually:

- `C:\Program Files\NVIDIA GPU Computing Toolkit\CUDA\v12.x\bin`

---

## 3. Install cuDNN 9

- Create a free account if needed: https://developer.nvidia.com/cudnn  
- Download **cuDNN** for **CUDA 12.x** (e.g. "cuDNN v9.x for CUDA 12.x").  
- You get a zip. Unzip it.  
- Copy the contents into the CUDA folder:
  - From the zip: `bin\*`  → to  `C:\Program Files\NVIDIA GPU Computing Toolkit\CUDA\v12.x\bin`
  - From the zip: `include\*`  → to  `C:\Program Files\NVIDIA GPU Computing Toolkit\CUDA\v12.x\include`
  - From the zip: `lib\x64\*`  → to  `C:\Program Files\NVIDIA GPU Computing Toolkit\CUDA\v12.x\lib\x64`

So the DLLs (e.g. `cudnn64_9.dll`) end up in `CUDA\v12.x\bin`. That folder must be in your **PATH** (step 2).  
*Alternative:* If you install cuDNN to a separate folder (e.g. `C:\Program Files\NVIDIA\CUDNN\v9.x\bin`), the script will try to find it automatically; otherwise set **CUDNN_PATH** to the cuDNN root and ensure its `bin` contains `cudnn64_9.dll`.

---

## 4. Restart terminal and install onnxruntime-gpu

Close and reopen your terminal (or Cursor) so PATH is updated. Then:

```bash
cd "c:\Users\matou\Documents\MCI\Master Thesis\app"
pip install onnxruntime-gpu
```

If you had `onnxruntime` (CPU) installed, `onnxruntime-gpu` will replace it for that environment.

---

## 5. Run with GPU

```bash
python pose.py --device cuda
```

You should see no CUDA errors and higher FPS / lower "Pose: X ms". If you see "GPU failed, falling back to CPU", the app will still run on CPU; check that PATH includes the CUDA `bin` folder and that the cuDNN DLLs are in that folder (or that cuDNN is installed in `C:\Program Files\NVIDIA\CUDNN\v9.x` — the script auto-detects that).

For **lowest latency** (resolution, threaded mode, lightweight model), see the **Low latency / optimization** section in [README.md](README.md).

---

## Quick checklist

- [ ] NVIDIA driver installed (`nvidia-smi` works)
- [ ] CUDA Toolkit 12 installed and its `bin` in PATH
- [ ] cuDNN 9 unzipped and copied into the CUDA folder (so `bin` has the DLLs)
- [ ] Terminal restarted after PATH changes
- [ ] `pip install onnxruntime-gpu` done
- [ ] `python pose.py --device cuda` runs without CUDA errors
