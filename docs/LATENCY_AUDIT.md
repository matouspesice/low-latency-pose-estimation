# Latency audit of the pose pipeline (November 2026)

An outside review of the whole chain, camera → `pose.py` → UDP → Unity → screen, looking for latency that the existing benchmarks do not show. Nothing in the code was changed. Section 8 is a test plan for checking the findings on the real setup (FLIR camera, GPU, Unity).

## 1. Summary

Inference is not the bottleneck. It takes 6–10 ms per frame, while the estimated lag between a body lean and the ball moving in Coin Collector is about 170 ms at a 30 fps camera. Most of that comes from stages the current instrumentation does not cover:

1. **Three smoothing filters in series on the ball path.** One in `pose.py` (`proc_enable`) and two in Unity. Together they add close to 90 ms.
2. **Unity frame structure.** Poses are polled once per rendered frame, the game reads the tilt in `FixedUpdate` (which runs before the pose is read), and the frame is presented at the next VSync. About 50 ms.
3. **Camera cadence.** The committed traces show about 30 fps where 60 was requested, and about 10 fps in two runs.
4. **Inference may be running on the CPU.** The committed "cuda" traces are no faster than a CPU-only machine, and `pose.py` cannot tell when ONNX Runtime silently falls back.
5. **The measurement tools stop short.** `--log-latency` and the trace start after the camera read and end before smoothing, rendering and display. The Unity half of the trace has never been recorded.

## 2. How to read this document

Every finding carries one of these tags.

| Tag | Meaning |
|---|---|
| `[trace]` | Computed from the traces committed in `app/traces/` (Windows, `device=cuda`). |
| `[measured]` | Measured by running the unmodified `pose.py` on a Linux VM (4 vCPU, no GPU) with a synthetic camera that replays a photo of a person moved by a known amount. |
| `[model]` | Numeric model of the Unity side, built from the C# formulas and the values serialized in `MainScene.unity` and `ProjectSettings`. Not run in Unity. |
| `[code]` | Read from the code only. Not executed. |

Not tested at all: Unity itself, a real webcam, the FLIR camera, a real GPU, the MMPose backend, Genies avatars.

**The lag metric.** Lag numbers are the delay between a 0.5 Hz side-to-side sway and what is shown, taken from the phase of the fundamental. This is the average age of the displayed value, so it includes the staleness between samples (half a camera period, half a display period). It is larger than a "first visible reaction" measurement and it is the right metric for smoothing, which slows the response rather than delaying its onset. The model excludes camera sensor and transport latency and the monitor's own response time, so real values will be higher by that amount.

## 3. The pipeline, stage by stage

| # | Stage | Where | Covered by `--log-latency` / trace? |
|---|---|---|---|
| 1 | Sensor exposure, transport, driver buffer | camera, `cap.read()` | No. `t_capture` and `image_received` are taken after `cap.read()` returns. |
| 2 | Inference: YOLOX detector every `det_frequency` frames, RTMPose every frame | `run_rtmlib_frame` | Yes. |
| 3 | Keypoint processing (gate, hold, clamp, adaptive EMA) | `_process_pose_data` | Trace: yes. `--log-latency`: no (`t_after` is taken before it). |
| 4 | Optional EMA | `--smooth-pose` in `send_pose_udp` | Compute time only. |
| 5 | Optional clock ROI JPEG + base64 | `_resolve_clock_roi_payload` | Trace: yes. `--log-latency`: no. |
| 6 | JSON + one UDP datagram | `send_pose_udp` | Trace: yes. `--log-latency`: no. |
| 7 | Wait until Unity's next `Update`, read and parse | `PoseReceiver.Update` | Only with the Unity trace, which was never recorded. |
| 8 | Torso lean EMA, gesture hold | `PoseGestureDetector.Update` | No. |
| 9 | Tilt → ball position (EMA, physics step) | `CoinMineGameManager.FixedUpdate` | No. |
| 9' | Keypoints → avatar joints (EMA) | `PoseAvatarDriver.Update` | No. |
| 10 | Render, present at VSync, monitor | Unity, GPU, display | No. |

## 4. Where the latency is

### 4.1 Budget for the Coin Collector ball `[model]`

Camera 30 fps, Python compute 8 ms, 60 Hz VSync, Fixed Timestep 0.02, repository defaults.

| Stage | Adds | Running total |
|---|---|---|
| Latest pose at Python output (frame sampling + compute) | | 25 ms |
| `proc_enable` adaptive EMA in `pose.py` | +34 ms | 59 ms |
| Unity: poll in `Update`, render, present at next VSync | +29 ms | 88 ms |
| `PoseGestureDetector.torsoLeanSmoothing = 0.25` | +49 ms | 138 ms |
| `FixedUpdate` runs before `Update`, plus `positionSmoothing = 0.15` | +23 ms | 161 ms |
| Rigidbody interpolation | 0 to +10 ms | 161–171 ms |

The same model with raw keypoints and every smoothing stage off gives 76 ms for the ball and 54 ms for the avatar.

The interpolation row is uncertain. The ball's X is written with `rb.position` and `MovePosition` every physics step, and whether Unity still interpolates a position set that way could not be checked without running it. Totals below use the upper value.

The avatar (Pose Test, Clock) skips the torso and physics stages: 100 ms with defaults, 54 ms with everything off. If `PoseAvatarDriver.Update` happens to run before `PoseReceiver.Update`, add 17 ms; no script execution order is set, so the order is not guaranteed.

Other camera rates, defaults: ball 145 ms at 60 fps and 265 ms at 10 fps; avatar 74 ms and 193 ms.

### 4.2 Predicted effect of each setting `[model]`

Ball lag in ms by camera frame rate, changing one setting from the defaults:

| Configuration | 30 fps | 60 fps | 120 fps |
|---|---|---|---|
| Repository defaults | 171 | 145 | 128 |
| `--proc-disable` | 137 | 128 | 120 |
| `torsoLeanSmoothing` 0.5 (slider maximum) | 138 | 113 | 96 |
| `positionSmoothing` 0 | 167 | 142 | 125 |
| Fixed Timestep 0.0083 | 156 | 131 | 114 |

Ball lag in ms, changes applied cumulatively. Runs A–E are the ones in the test plan (section 8.4).

| Run | Configuration | 30 fps | 60 fps | 120 fps |
|---|---|---|---|---|
| A | Repository defaults | 171 | 145 | 128 |
| B | A + processing EMA off | 137 | 128 | 120 |
| C | B + `torsoLeanSmoothing` 0.5 | 104 | 95 | 87 |
| D | C + `positionSmoothing` 0 | 101 | 92 | 84 |
| E | D + Fixed Timestep 0.0083 | 87 | 79 | 71 |
| | E + torso smoothing removed (needs a code change) | 71 | 62 | 54 |

Avatar lag in ms:

| Configuration | 30 fps | 60 fps | 120 fps |
|---|---|---|---|
| Repository defaults | 100 | 74 | 57 |
| `--proc-disable` | 65 | 57 | 49 |
| `--proc-disable` and `PoseAvatarDriver.smoothing` 0 | 54 | 46 | 37 |

All rows assume 8 ms of Python compute. At 120 fps that only holds if inference stays under the 8.3 ms frame period.

### 4.3 The smoothing stages

| Filter | Setting | Default | Notes |
|---|---|---|---|
| `pose.py` adaptive EMA | `proc_alpha` / `proc_min_alpha` | 0.55 / 0.25 | Per camera frame, so the lag grows as the camera slows down. |
| `pose.py` EMA | `--smooth-pose` | off | Stacks on top of the first one if enabled. |
| Torso lean EMA | `PoseGestureDetector.torsoLeanSmoothing` | 0.25 | Weight of the **new** sample, so higher means less smoothing. The slider stops at 0.5. Applied once per Unity frame, not once per new pose, so its strength depends on the frame rate. |
| Ball position EMA | `CoinMineGameManager.positionSmoothing` | 0.15 | Higher means more smoothing. |
| Avatar joint EMA | `PoseAvatarDriver.smoothing` | 0.4 | Higher means more smoothing. |

`BodyTiltInput.outputSmoothing`, the one setting `docs/COIN_COLLECTOR_LATENCY.md` says to keep at 0, is already 0 and contributes nothing.

Measured through the real `pose.py` with a 0.5 Hz sway `[measured]`. These are lags of the keypoint samples when they arrive over UDP, so the raw column is essentially the compute time:

| Camera | Raw keypoints | `proc_enable` (default) | Added |
|---|---|---|---|
| 60 fps | 12.9 ms | 34.1 ms | +21 ms |
| 30 fps | 11.6 ms | 45.8 ms | +34 ms |
| 10 fps | 9.3 ms | 105.3 ms | +96 ms |

`docs/POSE_PROCESSING_OPTIMIZATION.md` reports this stage as +1.22 ms. That figure is compute time (measured here at 23 µs per frame); the signal lag is what the table above shows.

`--proc-alpha 1 --proc-min-alpha 1` keeps the confidence gate, the hold and the outlier clamp but removes the EMA. Measured lag is then the same as raw (10.6 ms against 44.7 ms with defaults in the same session) `[measured]`.

### 4.4 Unity frame structure `[code]` `[model]`

- `PoseReceiver` reads the socket only in `Update`, so a packet waits on average half a frame before anyone sees it.
- `CoinMineGameManager` reads `TiltAxis` in `FixedUpdate`. Unity runs `FixedUpdate` before `Update`, so the physics step always uses the lean computed in the previous frame. That is one full frame (17 ms at 60 Hz).
- With Fixed Timestep 0.02 and a 60 Hz display, one rendered frame in six has no physics step at all.
- `FrameSyncIndicator` sets `QualitySettings.vSyncCount = 1` at start (`forceVSyncCount: 1` in the scene), and both quality levels have VSync on. Turning VSync off in Quality settings, as `docs/COIN_COLLECTOR_LATENCY.md` suggests, is overridden.
- `gestureHoldFrames = 5`: a new gesture counts only after five consecutive Unity frames, so the delay is about 70 ms at 60 Hz and depends on the frame rate.

### 4.5 Camera `[trace]`

- `camera_fps: 60` is requested, but `image_received` intervals are 32 ms with about 12 % at 48 ms: 29.5 fps.
- In `12_res_1280x720` and `06_smooth_pose` the intervals are 96 and 112 ms: about 10 fps.
- The pipeline is busy 8–10 ms out of every 34 ms and waits for the camera the rest of the time.
- The analyzer reports the 720p run as 12.9 ms because frame cadence and frame age are invisible to it.

The cause is not known. 10 fps at 1280×720 is what uncompressed YUY2 over USB 2.0 usually allows, and `pose.py` never requests MJPG and leaves auto exposure on. Both are hypotheses.

### 4.6 Is inference running on the GPU?

Per-frame inference time, lightweight mode, 640×480:

| | Committed traces, `device=cuda` `[trace]` | CPU-only VM `[measured]` |
|---|---|---|
| Pose-only frame, median | 5.7–6.6 ms in most runs, 9–11 ms in others | 4.7–5.7 ms |
| Detector frame | 21–25 ms | 24–29 ms |
| Balanced mode, pose-only | 9.2 ms | 10.0 ms |
| Balanced mode, detector frame | 57 ms | 137 ms |

Only the balanced detector is clearly faster in the traces. So either the GPU is not used, or it brings nothing for the lightweight models.

The silent fallback was reproduced `[measured]`. With `onnxruntime-gpu` installed and the CUDA libraries not loadable, `onnxruntime.get_available_providers()` still lists `CUDAExecutionProvider`. `pose.py` checks only that list, prints `Device: cuda`, and writes `device=cuda` into the trace, while both sessions actually run on `['CPUExecutionProvider']`. The only sign is an ONNX Runtime warning at startup: `Failed to create CUDAExecutionProvider. Require cuDNN 9.* and CUDA 12.*`.

Version notes:

- On Python 3.10 (the documented `.venv310`), `pip install onnxruntime-gpu` currently resolves to 1.23.2, which needs CUDA 12 and cuDNN 9. That matches `app/SETUP_GPU.md`.
- On Python 3.11 or newer it resolves to 1.32.0, which needs CUDA 13. Following `SETUP_GPU.md` there gives CPU inference.
- `rtmlib` depends on the CPU package `onnxruntime`, so `pip install -r requirements.txt` installs both. With both installed on the VM the CUDA provider was not listed at all, and `pose.py` printed its CPU fallback message.
- `onnxruntime-gpu` is not pinned in `requirements.txt`.

### 4.7 Smaller items

- **Detector spike.** Every `det_frequency`-th frame costs about 17 ms more: a hitch three times a second at 30 fps `[trace]`.
- **Several people in view.** RTMPose runs once per detected person but only the first is used. With four people a pose-only frame cost 18.6 ms instead of 6.3 ms `[measured]`.
- **Packet parsing.** `PoseReceiver` fully parses every queued datagram (up to 10 per frame) instead of only the newest `[code]`.
- **Threading.** Threaded and non-threaded mode showed no measurable difference without a window (8.3 against 8.0 ms mean) `[measured]`.

## 5. Why the existing measurements do not show this

- **`--log-latency` is inference only.** `loop_ms` runs from after `cap.read()` to before ROI encoding, processing and sending. With the clock stream on it read 7.34 ms where the real frame-in to sent time was 8.07 ms `[measured]`.
- **"End-to-end" in the analyzer** runs from `image_received` to `unity_pose_ready`. Camera latency, every Unity smoothing stage, rendering and display are outside it `[code]`.
- **No Unity trace exists.** All 15 committed traces contain zero Unity lines `[trace]`, and `pipelineTracePath` is empty in the scene.
- **`unity_udp_received` is logged without a sequence number.** The analyzer pairs events by sequence, so "Unity: network" and "Unity: JSON parse" can never be produced. Confirmed with an emulated Unity trace: the two rows are missing from the report `[measured]`.
- **Sharing one trace file probably fails on Windows.** `PipelineTrace.cs` opens the file with `FileShare.Read`, which conflicts with Python holding it open for writing, in either start order `[code]`.
- **Run-to-run noise is larger than the effects being compared.** Runs with the same inference configuration range from 7.2 to 14.6 ms mean (`07_clock_stream` against `02_proc_on`) `[trace]`.
- **The clock ROI is not a proxy for the pose path.** It skips all pose smoothing and has two brakes of its own:
  - `clock_stream_max_fps: 30` against a 30 fps camera lets only 61 % of packets carry a new image; the rest repeat the previous JPEG. With the limit at 60 it is 97 % `[measured]`.
  - `ClockMode.maxRoiDecodeFps = 12` caps the display at 12 updates per second `[code]`.
  - Modelled age of the image on screen: 104 ms with defaults (5–95 %: 51–161 ms), 92 ms with the Python limit at 60, 54 ms with both limits out of the way `[model]`.
- **Trace events on the ROI path are optimistic.** `roi_encoded` is logged for repeated JPEGs too, and `unity_roi_displayed` carries the sequence number of the latest pose, not of the frame the image came from `[code]`.

## 6. Functional problems

Reproduced `[measured]`:

| Problem | Detail |
|---|---|
| Threaded mode + window + `--max-frames` hangs | After the last frame the process waits for **Q**. `bench_pipeline_runs.py` therefore stalls at run `03_window_on`; the committed trace shows a 137 s gap there. |
| Camera stops delivering frames | Threaded with `--no-window`: hangs forever with no message. Non-threaded: exits with code 0 and no message. |
| `bench_pipeline_runs.py --dry-run` deletes traces | Existing trace files are removed before the dry-run check. Restore with `git checkout -- app/traces`. |
| `bench_runs.json` uses `"python": "python"` | Fails where only `python3` or a venv interpreter exists. |
| Oversized UDP datagram is dropped silently | A full-frame ROI without downscaling is 93–121 KB. `sendto` fails, the exception is swallowed, and the pose in that packet is lost too. Defaults (2–6 KB) are fine. |
| `--ocr-enable` does nothing | `pose.py` forces it off and prints a note. |

From code reading `[code]`:

- **Fresh clone does not compile in Unity.** `packages-lock.json` references the Genies Avatar SDK as a local package that is in `.gitignore`. The root README does not mention it.
- **`runInBackground: 0`.** A built player pauses when its window loses focus.
- **FLIR: one bad frame ends the stream.** `_FlirCapture.read()` returns `False` for an incomplete image or a 1 s timeout, and the capture loop treats that as end of stream (see the hang above).
- **FLIR: `--width` / `--height` are probably ignored.** They are applied after `BeginAcquisition()`, when the nodes are normally locked, and they would crop rather than scale.
- **FLIR: nothing is configured.** Frame rate, exposure and pixel format are whatever the camera currently holds. A 2-D image is treated as grayscale, so a Bayer format would feed the mosaic to the model.
- **FLIR: 3-channel frames are returned without a copy** and the image is released right after, so the array may point at a reused buffer.

## 7. What works `[measured]`

- `pose.py` end to end: camera → inference → UDP. Packets are valid JSON of about 1.4 KB with 17 keypoints and `frameSeq`.
- `--pipeline-trace`, `--log-latency`, and `analyze_pipeline_trace.py` on both the committed and new traces.
- Non-threaded mode with a window, and `--no-window` runs with `--max-frames`.
- Frames with no person: no crash; keypoints arrive with confidence below 0.2 and Unity filters them at 0.3.
- Clock ROI stream with default settings (packets of 2–6 KB).
- CPU fallback when `onnxruntime-gpu` is absent, with a clear message.

## 8. Test plan for the real setup (FLIR + GPU + Unity)

Do the steps in order; the first two decide how to read everything else.

Before starting:

- Use a new trace file name for every run. `pose.py` appends, and a file with several sessions distorts the statistics.
- Do not run `bench_pipeline_runs.py --dry-run`.
- Inspector changes made in Play mode are reverted when Play stops.
- Keep the Unity window focused while measuring.

### 8.1 Is inference on the GPU?

Save as `app/check_providers.py` and run it with the same interpreter as `pose.py`:

```python
from win_cuda_path import _ensure_cuda_in_path
_ensure_cuda_in_path()
import onnxruntime as ort
from pose import create_rtmlib_tracker

t = create_rtmlib_tracker("cuda", "lightweight", 10)
print("onnxruntime", ort.__version__, ort.get_available_providers())
print("detector:", t.det_model.session.get_providers())
print("pose    :", t.pose_model.session.get_providers())
```

- Good: both lines start with `CUDAExecutionProvider`.
- Bad: `['CPUExecutionProvider']`. Look for `Failed to create CUDAExecutionProvider` in the console and check `pip list` for a second `onnxruntime` package.
- Cross-check: `nvidia-smi` while `pose.py` runs should list the Python process.

If it was on the CPU, fix it and compare `infer_ms` before and after. All committed traces would then be CPU numbers.

### 8.2 FLIR: frame rate, resolution, format

```powershell
.\.venv310\Scripts\python.exe pose.py --camera-mode flir --camera 0 --no-window --no-viz --proc-disable --max-frames 600 --pipeline-trace traces\flir_baseline.txt --run-label flir_baseline
```

If packets stop and the process does not exit, that is the incomplete-frame hang from section 6; press Ctrl+C.

The analyzer does not report frame cadence. Save as `app/trace_cadence.py`:

```python
import statistics, sys
from datetime import datetime

ts = [datetime.strptime(l[:23], "%Y-%m-%d %H:%M:%S.%f")
      for l in open(sys.argv[1], encoding="utf-8") if "stage=image_received" in l]
d = [(b - a).total_seconds() * 1000 for a, b in zip(ts, ts[1:])]
d = [x for x in d if x < 1000]  # drop gaps between sessions in one file
print(f"frames={len(ts)}  mean interval={statistics.mean(d):.1f} ms "
      f"({1000 / statistics.mean(d):.1f} fps)  median={statistics.median(d):.0f} ms  max={max(d):.0f} ms")
```

```powershell
.\.venv310\Scripts\python.exe trace_cadence.py traces\flir_baseline.txt
.\.venv310\Scripts\python.exe analyze_pipeline_trace.py traces\flir_baseline.txt
```

Check:

- **Frame rate** against the acquisition frame rate shown in SpinView. If inference is slower than the frame period, the cadence follows inference instead.
- **Frame size.** Run once with a window; the overlay shows width × height. A full-resolution sensor image costs more in preprocessing than the 640×480 the config asks for.
- **Pixel format** in SpinView: Mono8, or a colour format that arrives as three channels.
- **`infer_ms`** for pose-only and detector frames, against section 4.6.

### 8.3 Record the Unity half of the trace

1. In Unity, before pressing Play, select the `PoseBridge` object and set **Pipeline Trace Path** on `PoseReceiver` to a separate file, for example `C:\temp\unity_trace.txt`.
2. Start `pose.py` with `--pipeline-trace traces\run1_py.txt`, press Play, let it run for 20 s, stop both.
3. Merge the two files. Plain concatenation does not work, because the analyzer treats each `session_start` as a new run. Save as `app/merge_traces.py`:

```python
import sys

py_path, unity_path, out_path = sys.argv[1:4]
lines = [l for l in open(py_path, encoding="utf-8") if "stage=session_end" not in l]
lines += [l for l in open(unity_path, encoding="utf-8-sig") if "stage=session_" not in l]
open(out_path, "w", encoding="utf-8").writelines(lines)
```

```powershell
.\.venv310\Scripts\python.exe merge_traces.py traces\run1_py.txt C:\temp\unity_trace.txt traces\run1_merged.txt
.\.venv310\Scripts\python.exe analyze_pipeline_trace.py traces\run1_merged.txt
```

Expect "End-to-end: frame in → pose ready" to be the Python time plus roughly half a Unity frame. In an emulation with 60 Hz polling it was 19 ms against 7.4 ms for Python alone. The "network" and "parse" rows will be missing until `unity_udp_received` is logged with a sequence number.

### 8.4 Motion to photon on the pose path

The trace cannot see smoothing, rendering or the display, so this needs a camera.

1. Film the player and the monitor in one shot with a phone in slow motion (240 fps gives 4 ms per frame).
2. Sway side to side at a steady pace, about one full cycle every two seconds. Keep the sway small enough that the ball does not reach the lane edge.
3. For each reversal, count the frames between the body turning around and the ball turning around. Average at least ten reversals.

Compare turning points, not the first visible reaction: a reaction measurement mostly misses the smoothing lag.

Run it for these configurations and compare the differences with the A–E table in section 4.2. The absolute values will be higher than the model by the camera and monitor latency; the differences between runs should match.

| Run | Change | Where |
|---|---|---|
| A | Defaults | |
| B | A + processing EMA off | `--proc-alpha 1 --proc-min-alpha 1` (keeps gate and clamp) or `--proc-disable` |
| C | B + torso smoothing 0.5 | `PoseGestureDetector` → Torso Lean Smoothing |
| D | C + position smoothing 0 | `CoinMineGameManager` → Position Smoothing |
| E | D + Fixed Timestep 0.0083 | Project Settings → Time |

For the avatar, the equivalent pair is defaults against `--proc-disable` with `PoseAvatarDriver.smoothing` at 0.

To test without VSync, set `forceVSyncCount` to 0 on `FrameSyncIndicator` as well as in Quality settings.

### 8.5 Clock ROI path

Repeat the wall-clock measurement with defaults, then with `--clock-stream-max-fps 120` and `ClockMode.maxRoiDecodeFps` raised to at least the camera frame rate. The model predicts about 50 ms less at 30 fps. Whatever the result, this path measures image transport, not the pose path of 8.4.

## 9. Suggested changes

None of these are implemented.

Settings only, largest predicted effect first:

1. `pose.json`: `proc_alpha` and `proc_min_alpha` to 1.0 for latency-critical modes.
2. `PoseGestureDetector.torsoLeanSmoothing` to 0.5.
3. Fixed Timestep to 0.0083.
4. `CoinMineGameManager.positionSmoothing` to 0.
5. Clock measurements: `clock_stream_max_fps` to at least twice the camera rate, and raise `maxRoiDecodeFps`.

Code. Items 1–3 are about latency, 4–7 about measurement and robustness, 8–10 are smaller optimizations:

1. **One smoothing stage, time-based.** Apply it once per new pose rather than once per Unity frame, and let `torsoLeanSmoothing` go up to 1.
2. **Read the tilt in the same frame it arrives.** Steer the ball from `Update`, or give `PoseReceiver` and `PoseGestureDetector` an explicit script execution order ahead of their consumers. Saves about one frame.
3. **Report the real execution provider.** Print `session.get_providers()` at startup, write it to the trace, and stop or warn loudly when `cuda` was requested but is not active. Pin `onnxruntime-gpu`.
4. **Make the trace cover the gaps.** Log the frame interval, log `unity_udp_received` with its sequence number, tag `unity_roi_displayed` with the frame the image came from, and add an event when the pose is consumed by the game.
5. **Fix the hangs.** Set the stop flag when the capture loop ends, exit the main loop when the worker is dead, and skip an incomplete FLIR frame instead of ending the stream.
6. **`bench_pipeline_runs.py`:** check `--dry-run` before deleting, and default to `sys.executable`.
7. **`send_pose_udp`:** report a failed send once, and cap the ROI payload size.
8. **Detector cost:** estimate pose only for the tracked person, and consider a higher `det_frequency`.
9. **Webcam:** request MJPG and a fixed exposure, then re-check the frame rate.
10. **Unity receive:** drain the socket and parse only the newest datagram.

## Appendix A. Additional measurements `[measured]`

Step response of the keypoint stream, 30 fps camera. Frames until the keypoints reach 90 % of a sudden shift of 0.10 of the frame width:

| Configuration | 90 % reached |
|---|---|
| Raw | same frame |
| `proc_enable` (default) | 3 frames later (about 110 ms) |
| `--smooth-pose 0.6` | 2 frames later |
| `proc_enable` + `--smooth-pose 0.6` | 4 frames later |
| `proc_enable`, shift of 0.25 | 4 frames later (174 ms) |

For the 0.10 shift, `proc_enable` already shows half of it in the first frame, which is why a "first reaction" measurement does not reveal this lag.

Other figures:

- `_process_pose_data`: 23 µs per frame. `send_pose_udp`: 18 µs. ROI encode: 0.2–0.8 ms. Trace logging: 3.5 µs per event.
- 1 Hz sway at 30 fps: 9.9 ms raw, 43.3 ms with `proc_enable`.
- At 10 fps `proc_enable` also reduces the sway amplitude to 0.90.

## Appendix B. Method

- **Environment:** Linux VM, 4 vCPU, no GPU, Python 3.12, `onnxruntime` 1.32.0 (CPU), `rtmlib` 0.0.16, `opencv-python` 4.11, `numpy` 1.26.4.
- **Synthetic camera:** `pose._open_camera_with_mode` was replaced at runtime by an object that returns frames at a fixed rate, showing a photo of a person shifted horizontally by a known amount (static, step or sine). `pose.py` itself was not modified. A UDP listener recorded arrival time and keypoints.
- **Lag:** least-squares fit of the sway frequency to the true motion and to the received keypoints; the phase difference is the lag.
- **Unity model:** a frame-by-frame simulation of `PoseReceiver`, `PoseGestureDetector`, `BodyTiltInput`, `CoinMineGameManager` and `PoseAvatarDriver` using their formulas and scene values, fed by the real `_process_pose_data`. Each frame is assumed to appear one refresh after its `Update`. The dead zone `minTiltToMove` is ignored.
- **GPU fallback:** `onnxruntime-gpu` 1.23.2 and 1.32.0 installed on the VM without CUDA libraries.
- The harness scripts are not part of the repository.
