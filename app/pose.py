#!/usr/bin/env python3
"""
Real-time pose estimation from webcam — RTMPose (rtmlib) or MMPose.
Shows the camera stream with skeleton overlay. Tuned for low latency (Unity / high-FPS camera later).

Low-latency tips: --mode lightweight --det-frequency 10 --width 640 --height 480 --device cuda --threaded
(lightweight = smaller models; det-frequency = run detector less often; resolution = less data; threaded = display doesn't block inference)

Usage:
  python pose.py
  python pose.py --device cuda --threaded --width 640 --height 480
  python pose.py --no-viz   # keypoints only (e.g. for piping to Unity)

Press Q in the window to quit.
"""

import argparse
import base64
import json
import os
import re
import socket
import subprocess
import sys
import threading
import time

_script_dir = os.path.dirname(os.path.abspath(__file__))
if _script_dir not in sys.path:
    sys.path.insert(0, _script_dir)

from win_cuda_path import _ensure_cuda_in_path

import cv2

# Reduce OpenCV console noise (e.g. MSMF camera warnings on Windows)
try:
    cv2.utils.logging.setLogLevel(cv2.utils.logging.LOG_LEVEL_ERROR)
except Exception:
    pass


def _default_config_path():
    """Resolve default config path: script dir or cwd, file pose.json."""
    script_dir = os.path.dirname(os.path.abspath(__file__))
    for base in (script_dir, os.getcwd()):
        path = os.path.join(base, "pose.json")
        if os.path.isfile(path):
            return path
    return None


def _load_config(path):
    """Load options from a JSON config file. Unknown keys are ignored."""
    with open(path, "r", encoding="utf-8") as f:
        raw = json.load(f)
    # Map to argparse-style names; only include known keys with valid types
    out = {}
    int_keys = ("camera", "det_frequency", "width", "height", "udp_port", "ocr_interval", "max_frames")
    str_keys = (
        "backend",
        "device",
        "mode",
        "camera_mode",
        "camera_api",
        "udp_host",
        "ocr_roi",
        "ocr_whitelist",
        "latency_csv",
        "ocr_backend",
        "clock_roi",
        "camera_auto_exposure",
        "camera_auto_wb",
    )
    bool_keys = (
        "threaded",
        "no_viz",
        "show_fps",
        "no_window",
        "ocr_enable",
        "ocr_print_console",
        "ocr_flip_x",
        "ocr_debug",
        "ocr_exhaustive",
        "log_latency",
        "ocr_rapid_use_text_det",
        "clock_stream_enable",
        "clock_stream_separate_udp",
    )
    float_keys = ("smooth_pose", "ocr_max_fps", "ocr_print_interval", "camera_fps", "camera_exposure", "clock_stream_max_fps")
    int_keys = int_keys + ("clock_stream_jpeg_quality", "clock_downscale")
    for k in int_keys:
        if k in raw and isinstance(raw[k], (int, float)):
            out[k] = int(raw[k])
    for k in str_keys:
        if k in raw and isinstance(raw[k], str):
            out[k] = raw[k]
    for k in bool_keys:
        if k in raw and isinstance(raw[k], bool):
            out[k] = raw[k]
    for k in float_keys:
        if k in raw and isinstance(raw[k], (int, float)):
            out[k] = float(raw[k])
    return out


def parse_args():
    # First pass: get --config so we can load defaults (no -h so full parser shows full help)
    pre = argparse.ArgumentParser(add_help=False)
    pre.add_argument("--config", type=str, default=None, help="Path to JSON config file (default: pose.json in app dir or cwd)")
    pre_args, remaining = pre.parse_known_args()
    config_path = pre_args.config or _default_config_path()
    config = {}
    if config_path:
        try:
            config = _load_config(config_path)
        except (OSError, json.JSONDecodeError) as e:
            if pre_args.config:
                print(f"Warning: could not load config from {config_path}: {e}", file=sys.stderr)

    # Defaults: config overrides built-in, then CLI overrides config
    defaults = {
        "camera": 0,
        "camera_mode": "webcam",
        "camera_api": "auto",
        "backend": "rtmlib",
        "device": "cuda",
        "mode": "balanced",
        "det_frequency": 5,
        "width": 0,
        "height": 0,
        "threaded": False,
        "no_viz": False,
        "show_fps": False,
        "no_window": False,
        "udp_port": 0,
        "udp_host": "127.0.0.1",
        "smooth_pose": 0,
        "log_latency": False,
        "latency_csv": "",
        "ocr_enable": False,
        "ocr_roi": "",
        "ocr_interval": 1,
        "ocr_whitelist": "0123456789:.",
        "ocr_max_fps": 8.0,
        "ocr_print_console": False,
        "ocr_print_interval": 0.25,
        "camera_fps": 0.0,
        "camera_exposure": 0.0,
        "camera_auto_exposure": "keep",
        "camera_auto_wb": "keep",
        "max_frames": 0,
        "ocr_flip_x": False,
        "ocr_debug": False,
        "ocr_exhaustive": False,
        "ocr_backend": "tesseract",
        "ocr_rapid_use_text_det": False,
        "clock_stream_enable": False,
        "clock_roi": "",
        "clock_stream_max_fps": 30.0,
        "clock_stream_jpeg_quality": 55,
        "clock_downscale": 192,
        "clock_stream_separate_udp": True,
    }
    for k, v in config.items():
        if k in defaults:
            defaults[k] = v

    p = argparse.ArgumentParser(
        description="Webcam pose estimation (rtmlib or MMPose); compare with --backend"
    )
    p.add_argument(
        "--config",
        type=str,
        default=None,
        help="Path to JSON config file (default: pose.json in app dir or cwd)",
    )
    p.add_argument(
        "--camera",
        type=int,
        default=defaults["camera"],
        help="Camera device index (0 = default, 1 = first USB, etc.)",
    )
    p.add_argument(
        "--camera-mode",
        type=str,
        default=defaults["camera_mode"],
        choices=("webcam", "flir"),
        help="Camera mode profile: webcam (default) or flir (tries FLIR-friendly backends/index fallback).",
    )
    p.add_argument(
        "--camera-api",
        type=str,
        default=defaults["camera_api"],
        choices=("auto", "msmf", "dshow", "default"),
        help="OpenCV camera API on Windows. auto tries mode-specific order; dshow is often better for manual exposure/FPS.",
    )
    p.add_argument(
        "--backend",
        type=str,
        default=defaults["backend"],
        choices=("rtmlib", "mmpose"),
        help="Pose backend: rtmlib (ONNX, default) or mmpose (PyTorch). Use both to find fastest.",
    )
    p.add_argument(
        "--device",
        type=str,
        default=defaults["device"],
        choices=("cpu", "cuda"),
        help="Device for inference (default: cpu; use --device cuda only if CUDA 12 + cuDNN 9 are installed)",
    )
    p.add_argument(
        "--mode",
        type=str,
        default=defaults["mode"],
        choices=("performance", "lightweight", "balanced"),
        help="rtmlib only: mode (lightweight=lowest latency, performance=most accurate)",
    )
    p.add_argument(
        "--det-frequency",
        type=int,
        default=defaults["det_frequency"],
        help="rtmlib only: run person detector every N frames (higher = lower latency, e.g. 10)",
    )
    p.add_argument(
        "--width",
        type=int,
        default=defaults["width"],
        help="Capture width (0 = camera default). Lower = less data, e.g. 640 for latency.",
    )
    p.add_argument(
        "--height",
        type=int,
        default=defaults["height"],
        help="Capture height (0 = camera default). e.g. 480 for latency.",
    )
    p.add_argument(
        "--threaded",
        action="store_true",
        default=defaults["threaded"],
        help="Run capture+inference in a background thread; main thread only displays. Reduces latency by not blocking on imshow.",
    )
    p.add_argument(
        "--no-window",
        action="store_true",
        default=defaults["no_window"],
        help="Run without preview window (useful for headless tests and automated latency runs).",
    )
    p.add_argument(
        "--no-viz",
        action="store_true",
        default=defaults["no_viz"],
        help="Skip skeleton overlay (raw frame + stats only). Slightly faster; use for keypoints-only (e.g. Unity).",
    )
    p.add_argument(
        "--show-fps",
        action="store_true",
        default=defaults["show_fps"],
        help="(Deprecated: stats are always shown.) Show FPS and inference time on the window",
    )
    p.add_argument(
        "--udp-port",
        type=int,
        default=defaults["udp_port"],
        help="If set, broadcast pose JSON to this port (e.g. for Unity Architect game). 0 = disabled.",
    )
    p.add_argument(
        "--udp-host",
        type=str,
        default=defaults["udp_host"],
        help="Target host for pose UDP (default: 127.0.0.1 for local Unity).",
    )
    p.add_argument(
        "--smooth-pose",
        type=float,
        default=defaults.get("smooth_pose", 0),
        metavar="ALPHA",
        help="Optional one-tap pose smoothing 0=off (lowest latency), 0.5-0.7=light. Weight of new sample.",
    )
    p.add_argument(
        "--camera-fps",
        type=float,
        default=defaults.get("camera_fps", 0.0),
        metavar="FPS",
        help="Requested camera FPS (0 = do not set). Driver may clamp or ignore this value.",
    )
    p.add_argument(
        "--camera-exposure",
        type=float,
        default=defaults.get("camera_exposure", 0.0),
        metavar="VAL",
        help="Requested camera exposure value (driver-specific units; 0 = do not set).",
    )
    p.add_argument(
        "--camera-auto-exposure",
        type=str,
        default=defaults.get("camera_auto_exposure", "keep"),
        choices=("keep", "auto", "manual"),
        help="Camera auto-exposure mode override for webcam profile.",
    )
    p.add_argument(
        "--camera-auto-wb",
        type=str,
        default=defaults.get("camera_auto_wb", "keep"),
        choices=("keep", "on", "off"),
        help="Camera auto white balance override for webcam profile.",
    )
    p.add_argument(
        "--max-frames",
        type=int,
        default=defaults.get("max_frames", 0),
        metavar="N",
        help="Stop automatically after N processed frames (0 = run until Q).",
    )
    p.add_argument(
        "--log-latency",
        action="store_true",
        default=defaults["log_latency"],
        help="Collect tracking-loop latency (capture-to-pose ms) and print summary at exit (mean, p95, n).",
    )
    p.add_argument(
        "--latency-csv",
        type=str,
        default=defaults["latency_csv"],
        metavar="PATH",
        help=(
            "When --log-latency is set AND this path is non-empty, write per-frame latency "
            "(loop_ms, infer_ms) to that CSV. Default in pose.json is empty — no file is written."
        ),
    )
    p.add_argument(
        "--ocr-enable",
        action="store_true",
        default=defaults["ocr_enable"],
        help="Enable OCR and include ocrText in UDP payload for Unity.",
    )
    p.add_argument(
        "--ocr-roi",
        type=str,
        default=defaults["ocr_roi"],
        metavar="X,Y,W,H",
        help="Optional OCR ROI in normalized coords [0,1], e.g. 0.30,0.80,0.40,0.18",
    )
    p.add_argument(
        "--ocr-interval",
        type=int,
        default=defaults["ocr_interval"],
        metavar="N",
        help="Run OCR every N frames (1 = every frame).",
    )
    p.add_argument(
        "--ocr-whitelist",
        type=str,
        default=defaults["ocr_whitelist"],
        help="Allowed OCR chars, e.g. 0123456789:.",
    )
    p.add_argument(
        "--ocr-max-fps",
        type=float,
        default=defaults["ocr_max_fps"],
        metavar="FPS",
        help="Maximum OCR processing rate (default: 8). Lower = less lag in preview.",
    )
    p.add_argument(
        "--ocr-print-console",
        action="store_true",
        default=defaults["ocr_print_console"],
        help="Print recognized OCR text to console while running.",
    )
    p.add_argument(
        "--ocr-print-interval",
        type=float,
        default=defaults["ocr_print_interval"],
        metavar="SEC",
        help="Minimum seconds between OCR console prints (default: 0.25).",
    )
    p.add_argument(
        "--ocr-flip-x",
        action="store_true",
        default=defaults["ocr_flip_x"],
        help="Flip OCR input horizontally before recognition (use if digits appear mirrored).",
    )
    p.add_argument(
        "--ocr-debug",
        action="store_true",
        default=defaults["ocr_debug"],
        help="Verbose OCR diagnostics (ROI, skips, raw text, cleaned text, timings).",
    )
    p.add_argument(
        "--ocr-exhaustive",
        action="store_true",
        default=defaults["ocr_exhaustive"],
        help="Try many OCR variants (slower, more robust). Keep OFF for real-time use.",
    )
    p.add_argument(
        "--ocr-backend",
        type=str,
        default=defaults.get("ocr_backend", "tesseract"),
        choices=("tesseract", "rapid"),
        help="tesseract (CPU) or rapid (RapidOCR+ONNX Runtime, same stack as rtmlib; use onnxruntime-gpu for CUDA).",
    )
    p.add_argument(
        "--ocr-rapid-use-text-det",
        action="store_true",
        default=defaults.get("ocr_rapid_use_text_det", False),
        help="RapidOCR: enable DB detector (slower). Default off = whole ROI as one line (good for clocks).",
    )
    p.add_argument(
        "--clock-stream-enable",
        action="store_true",
        default=defaults.get("clock_stream_enable", False),
        help="Send live clock ROI image in UDP payload as roiImageBase64.",
    )
    p.add_argument(
        "--clock-roi",
        type=str,
        default=defaults.get("clock_roi", ""),
        metavar="X,Y,W,H",
        help="Clock ROI in normalized coords [0,1]. Empty = full frame.",
    )
    p.add_argument(
        "--clock-stream-max-fps",
        type=float,
        default=defaults.get("clock_stream_max_fps", 30.0),
        metavar="FPS",
        help="Maximum image-stream send rate for roiImageBase64 (default 30).",
    )
    p.add_argument(
        "--clock-stream-jpeg-quality",
        type=int,
        default=defaults.get("clock_stream_jpeg_quality", 55),
        metavar="Q",
        help="JPEG quality (1-100) for roiImageBase64 payload (default 55 — low-latency).",
    )
    p.add_argument(
        "--clock-downscale",
        type=int,
        default=defaults.get("clock_downscale", 192),
        metavar="MAX_DIM",
        help=(
            "Downscale the ROI crop so its longest side is <= MAX_DIM pixels before JPEG "
            "encoding. Drops packet size + decode cost dramatically and removes noise that "
            "kills JPEG compression. 0 = no downscale. Default 192."
        ),
    )
    p.add_argument(
        "--clock-stream-separate-udp",
        action="store_true",
        default=defaults.get("clock_stream_separate_udp", True),
        help=(
            "Send the clock ROI as its own UDP datagram immediately after capture instead of "
            "bundling it with the pose packet. Keeps ROI latency independent of pose inference. "
            "Default: on."
        ),
    )
    p.add_argument(
        "--no-clock-stream-separate-udp",
        dest="clock_stream_separate_udp",
        action="store_false",
        help="Disable the separate ROI datagram and bundle it in the pose packet (legacy behavior).",
    )
    return p.parse_args()


# -----------------------------------------------------------------------------
# rtmlib backend
# -----------------------------------------------------------------------------
def create_rtmlib_tracker(device: str, mode: str, det_frequency: int):
    from rtmlib import Body, PoseTracker
    return PoseTracker(
        Body,
        mode=mode,
        det_frequency=det_frequency,
        backend="onnxruntime",
        device=device,
        to_openpose=False,
        tracking=False,
    )


def _is_onnxruntime_runtime_usable() -> bool:
    try:
        import onnxruntime  # type: ignore[reportMissingImports]
    except Exception:
        return False
    return hasattr(onnxruntime, "get_available_providers")


def _onnx_cuda_provider_available() -> bool:
    try:
        import onnxruntime  # type: ignore[reportMissingImports]
    except Exception:
        return False
    if not hasattr(onnxruntime, "get_available_providers"):
        return False
    try:
        providers = onnxruntime.get_available_providers()
    except Exception:
        return False
    return "CUDAExecutionProvider" in providers


def _maybe_reexec_with_venv310_for_rtmlib(backend: str) -> None:
    if backend != "rtmlib":
        return
    if _is_onnxruntime_runtime_usable():
        return
    venv_python = os.path.join(_script_dir, ".venv310", "Scripts", "python.exe")
    current = os.path.abspath(sys.executable).lower()
    if current == os.path.abspath(venv_python).lower():
        return
    if os.path.isfile(venv_python):
        print("Detected broken onnxruntime in current Python; restarting with app/.venv310 for pose-only mode.")
        rc = subprocess.call([venv_python, os.path.abspath(__file__), *sys.argv[1:]])
        sys.exit(rc)


def _open_camera_with_mode(camera_index: int, camera_mode: str, camera_api: str = "auto"):
    """Open camera with backend order tuned for webcam vs FLIR on Windows."""
    attempts: list[tuple[str, int | None]] = [("default", None)]
    if sys.platform == "win32" and camera_api != "auto":
        forced_map = {
            "default": ("default", None),
            "msmf": ("msmf", cv2.CAP_MSMF),
            "dshow": ("dshow", cv2.CAP_DSHOW),
        }
        attempts = [forced_map.get(camera_api, ("default", None))]
    elif sys.platform == "win32":
        if camera_mode == "flir":
            attempts = [
                ("dshow", cv2.CAP_DSHOW),
                ("msmf", cv2.CAP_MSMF),
                ("default", None),
            ]
        else:
            attempts = [
                ("msmf", cv2.CAP_MSMF),
                ("dshow", cv2.CAP_DSHOW),
                ("default", None),
            ]
    for label, api in attempts:
        try:
            cap = cv2.VideoCapture(camera_index) if api is None else cv2.VideoCapture(camera_index, api)
        except Exception:
            continue
        if cap is not None and cap.isOpened():
            return cap, label
        if cap is not None:
            try:
                cap.release()
            except Exception:
                pass
    return None, ""


def _set_camera_prop(cap, prop_id: int, value: float) -> bool:
    try:
        return bool(cap.set(prop_id, value))
    except Exception:
        return False


def _get_camera_prop(cap, prop_id: int):
    try:
        return cap.get(prop_id)
    except Exception:
        return float("nan")


def _configure_webcam_capture(cap, args) -> None:
    """Apply low-latency webcam capture settings and print effective values."""
    # Keep newest frame only when the backend supports it.
    _set_camera_prop(cap, cv2.CAP_PROP_BUFFERSIZE, 1)

    if args.width > 0:
        _set_camera_prop(cap, cv2.CAP_PROP_FRAME_WIDTH, float(args.width))
    if args.height > 0:
        _set_camera_prop(cap, cv2.CAP_PROP_FRAME_HEIGHT, float(args.height))
    if float(args.camera_fps) > 0:
        _set_camera_prop(cap, cv2.CAP_PROP_FPS, float(args.camera_fps))

    ae_mode = getattr(args, "camera_auto_exposure", "keep")
    if ae_mode == "manual":
        # Different Windows backends expect different values; try common variants.
        for v in (0.25, 1.0, 0.0):
            if _set_camera_prop(cap, cv2.CAP_PROP_AUTO_EXPOSURE, v):
                break
    elif ae_mode == "auto":
        for v in (0.75, 3.0, 1.0):
            if _set_camera_prop(cap, cv2.CAP_PROP_AUTO_EXPOSURE, v):
                break

    if float(args.camera_exposure) != 0:
        _set_camera_prop(cap, cv2.CAP_PROP_EXPOSURE, float(args.camera_exposure))

    awb_mode = getattr(args, "camera_auto_wb", "keep")
    if awb_mode == "off":
        _set_camera_prop(cap, cv2.CAP_PROP_AUTO_WB, 0.0)
    elif awb_mode == "on":
        _set_camera_prop(cap, cv2.CAP_PROP_AUTO_WB, 1.0)

    # Force one read so negotiated format settles before startup logs.
    _ = cap.read()

    w = _get_camera_prop(cap, cv2.CAP_PROP_FRAME_WIDTH)
    h = _get_camera_prop(cap, cv2.CAP_PROP_FRAME_HEIGHT)
    fps = _get_camera_prop(cap, cv2.CAP_PROP_FPS)
    ae = _get_camera_prop(cap, cv2.CAP_PROP_AUTO_EXPOSURE)
    exp = _get_camera_prop(cap, cv2.CAP_PROP_EXPOSURE)
    awb = _get_camera_prop(cap, cv2.CAP_PROP_AUTO_WB)
    print(
        "Camera settings:"
        f" requested={{w:{args.width or 'keep'},h:{args.height or 'keep'},fps:{args.camera_fps or 'keep'},"
        f"ae:{args.camera_auto_exposure},exp:{args.camera_exposure or 'keep'},awb:{args.camera_auto_wb}}}"
    )
    print(
        "Camera settings:"
        f" actual={{w:{w:.0f},h:{h:.0f},fps:{fps:.2f},ae:{ae:.3f},exp:{exp:.3f},awb:{awb:.3f}}}"
    )


class _FlirCapture:
    """Minimal camera wrapper with OpenCV-like read/release for PySpin cameras."""

    def __init__(self, cam_index: int):
        try:
            import PySpin  # type: ignore[reportMissingImports]
        except Exception as e:
            raise RuntimeError(
                "PySpin is required for --camera-mode flir. "
                "Install spinnaker_python wheel in app/.venv310."
            ) from e
        self._ps = PySpin
        self._system = PySpin.System.GetInstance()
        self._cam_list = self._system.GetCameras()
        count = self._cam_list.GetSize()
        if cam_index < 0 or cam_index >= count:
            self._cam_list.Clear()
            self._system.ReleaseInstance()
            raise RuntimeError(f"FLIR camera index {cam_index} not found. Detected cameras: {count}.")
        self._cam = self._cam_list.GetByIndex(cam_index)
        self._cam.Init()
        self._configure()
        self._cam.BeginAcquisition()
        self._opened = True

    def _configure(self) -> None:
        ps = self._ps
        nodemap = self._cam.GetNodeMap()
        tl_stream_nodemap = self._cam.GetTLStreamNodeMap()

        # Keep only the newest frame to reduce latency under load.
        buf_node = ps.CEnumerationPtr(tl_stream_nodemap.GetNode("StreamBufferHandlingMode"))
        if ps.IsAvailable(buf_node) and ps.IsWritable(buf_node):
            newest = buf_node.GetEntryByName("NewestOnly")
            if ps.IsAvailable(newest) and ps.IsReadable(newest):
                buf_node.SetIntValue(newest.GetValue())

        acq_mode = ps.CEnumerationPtr(nodemap.GetNode("AcquisitionMode"))
        if ps.IsAvailable(acq_mode) and ps.IsWritable(acq_mode):
            cont = acq_mode.GetEntryByName("Continuous")
            if ps.IsAvailable(cont) and ps.IsReadable(cont):
                acq_mode.SetIntValue(cont.GetValue())

    def isOpened(self) -> bool:
        return self._opened

    def read(self):
        if not self._opened:
            return False, None
        try:
            img = self._cam.GetNextImage(1000)
        except Exception:
            return False, None
        try:
            if img.IsIncomplete():
                return False, None
            arr = img.GetNDArray()
            if arr.ndim == 2:
                frame = cv2.cvtColor(arr, cv2.COLOR_GRAY2BGR)
            elif arr.ndim == 3 and arr.shape[2] == 3:
                frame = arr
            else:
                return False, None
            return True, frame
        finally:
            img.Release()

    def set(self, prop_id: int, value: float) -> bool:
        try:
            nodemap = self._cam.GetNodeMap()
            if prop_id == cv2.CAP_PROP_FRAME_WIDTH:
                node = self._ps.CIntegerPtr(nodemap.GetNode("Width"))
            elif prop_id == cv2.CAP_PROP_FRAME_HEIGHT:
                node = self._ps.CIntegerPtr(nodemap.GetNode("Height"))
            else:
                return False
            if not self._ps.IsAvailable(node) or not self._ps.IsWritable(node):
                return False
            max_v = int(node.GetMax())
            min_v = int(node.GetMin())
            target = int(max(min_v, min(max_v, int(value))))
            node.SetValue(target)
            return True
        except Exception:
            return False

    def release(self) -> None:
        if not self._opened:
            return
        self._opened = False
        cam = self._cam
        cam_list = self._cam_list
        system = self._system
        self._cam = None
        self._cam_list = None
        self._system = None
        try:
            cam.EndAcquisition()
        except Exception:
            pass
        try:
            cam.DeInit()
        except Exception:
            pass
        try:
            del cam
        except Exception:
            pass
        try:
            cam_list.Clear()
        except Exception:
            pass
        try:
            del cam_list
        except Exception:
            pass
        try:
            system.ReleaseInstance()
        except Exception:
            pass


def _open_flir_camera(camera_index: int):
    try:
        cap = _FlirCapture(camera_index)
        return cap, "pyspin"
    except Exception:
        return None, ""


def run_rtmlib_frame(pose_tracker, frame, show_fps: bool, no_viz: bool = False):
    t0 = time.perf_counter()
    keypoints, scores = pose_tracker(frame)
    t_infer = (time.perf_counter() - t0) * 1000
    vis = frame.copy()
    if not no_viz:
        from rtmlib import draw_skeleton
        vis = draw_skeleton(
            vis,
            keypoints,
            scores,
            openpose_skeleton=False,
            kpt_thr=0.4,
        )
    n_persons = 1 if keypoints.ndim == 2 else keypoints.shape[0]
    h, w = frame.shape[:2]
    pose_data = None
    if n_persons > 0 and w > 0 and h > 0:
        # First person: keypoints (17, 3) or (N, 17, 3) — x, y, score
        kpt = keypoints[0] if keypoints.ndim == 3 else keypoints
        if kpt.size >= 17 * 2:
            keypoints_list = []
            for i in range(min(17, len(kpt))):
                x = float(kpt[i, 0]) / w
                y = float(kpt[i, 1]) / h
                sc_flat = scores.reshape(-1) if scores is not None and hasattr(scores, "reshape") else []
                s = float(kpt[i, 2]) if kpt.shape[-1] >= 3 else (float(sc_flat[i]) if i < len(sc_flat) else 0.0)
                keypoints_list.append({"x": x, "y": y, "s": s})
            if len(keypoints_list) >= 17:
                pose_data = {"keypoints": keypoints_list, "width": w, "height": h}
    return vis, t_infer, n_persons, pose_data


# -----------------------------------------------------------------------------
# MMPose backend (optional)
# -----------------------------------------------------------------------------
def create_mmpose_inferencer(device: str):
    try:
        from mmpose.apis import MMPoseInferencer  # type: ignore[reportMissingImports]
    except ImportError as e:
        raise RuntimeError(
            "MMPose not installed. Install with: pip install -r requirements-mmpose.txt"
        ) from e
    device_str = "cuda:0" if device == "cuda" else "cpu"
    return MMPoseInferencer(
        pose2d="human",
        device=device_str,
    )


def run_mmpose_frame(inferencer, frame, show_fps: bool, no_viz: bool = False):
    t0 = time.perf_counter()
    gen = inferencer(frame, return_vis=not no_viz)
    result = next(gen)
    t_infer = (time.perf_counter() - t0) * 1000
    if no_viz:
        vis = frame.copy()
    else:
        vis_list = result.get("visualization", [])
        if vis_list:
            vis = vis_list[0]
            if vis.ndim == 3 and vis.shape[2] == 3:
                vis = cv2.cvtColor(vis, cv2.COLOR_RGB2BGR)
        else:
            vis = frame.copy()
    h, w = frame.shape[:2]
    pose_data = None
    try:
        preds = result.get("predictions", [])
        if preds and len(preds) > 0:
            first = preds[0]
            # MMPose: keypoints shape (17, 3) or (N, 17, 3); x, y, score
            kpts = first.get("keypoints", first) if isinstance(first, dict) else first
            if hasattr(kpts, "__len__") and len(kpts) >= 17 and w > 0 and h > 0:
                keypoints_list = []
                for i in range(17):
                    pt = kpts[i]
                    x = float(pt[0]) / w
                    y = float(pt[1]) / h
                    s = float(pt[2]) if len(pt) > 2 else 1.0
                    keypoints_list.append({"x": x, "y": y, "s": s})
                pose_data = {"keypoints": keypoints_list, "width": w, "height": h}
        n_persons = len(preds[0]) if preds and hasattr(preds[0], "__len__") else (1 if preds else 0)
    except Exception:
        n_persons = 0
    return vis, t_infer, n_persons, pose_data


# -----------------------------------------------------------------------------
# UDP pose broadcast (for Unity Architect game)
# -----------------------------------------------------------------------------
def _blend_pose(pose_data: dict, prev: dict | None, alpha: float) -> dict:
    """Blend current pose with previous (one-tap EMA). alpha = weight of new; keep latency minimal."""
    if prev is None or alpha >= 1:
        return pose_data
    out = {"keypoints": [], "width": pose_data["width"], "height": pose_data["height"]}
    prev_kpts = prev.get("keypoints", [])
    for i, k in enumerate(pose_data["keypoints"]):
        if i < len(prev_kpts):
            pk = prev_kpts[i]
            out["keypoints"].append({
                "x": alpha * k["x"] + (1 - alpha) * pk["x"],
                "y": alpha * k["y"] + (1 - alpha) * pk["y"],
                "s": k["s"] if k.get("s") is not None else pk.get("s", 0),
            })
        else:
            out["keypoints"].append(dict(k))
    return out


def send_pose_udp(
    sock: socket.socket,
    host: str,
    port: int,
    pose_data: dict | None,
    smooth_alpha: float = 0,
    prev_pose: list | None = None,
    ocr_text: str | None = None,
    roi_image_base64: str | None = None,
) -> None:
    if sock is None or port <= 0:
        return
    payload = pose_data
    if payload is None:
        payload = {}
    if smooth_alpha > 0 and pose_data is not None and prev_pose is not None and len(prev_pose) > 0:
        payload = _blend_pose(pose_data, prev_pose[0], smooth_alpha)
        prev_pose[0] = payload
    if ocr_text is not None:
        payload["ocrText"] = ocr_text
    if roi_image_base64 is not None:
        payload["roiImageBase64"] = roi_image_base64
    if not payload:
        return
    try:
        msg = json.dumps(payload).encode("utf-8")
        sock.sendto(msg, (host, port))
    except (OSError, TypeError):
        pass  # avoid spamming console on disconnect


def send_clock_roi_udp(sock: socket.socket, host: str, port: int, roi_image_base64: str) -> None:
    """Send ONLY the clock ROI as a small, dedicated UDP datagram.

    This decouples ROI latency from the pose-inference path — the ROI is sent
    microseconds after capture, independent of how long rtmlib/mmpose take.
    Unity's PoseReceiver parses the same JSON schema (`roiImageBase64`) either
    way, so no receiver change is needed. Keypoint-bearing pose packets are
    still sent separately after inference.
    """
    if sock is None or port <= 0 or not roi_image_base64:
        return
    try:
        msg = json.dumps({"roiImageBase64": roi_image_base64}).encode("utf-8")
        sock.sendto(msg, (host, port))
    except (OSError, TypeError):
        pass


# -----------------------------------------------------------------------------
# Stats overlay (FPS, latency, resolution, backend, device, persons)
# -----------------------------------------------------------------------------
def draw_stats(
    vis,
    proc_fps: float,
    display_fps: float | None,
    infer_ms: float,
    width: int,
    height: int,
    backend: str,
    device: str,
    n_persons: int | None,
):
    font = cv2.FONT_HERSHEY_SIMPLEX
    scale = 0.6
    thick = 2
    color = (0, 255, 0)
    y, dy = 24, 22
    fps_line = f"Proc FPS: {proc_fps:.1f}"
    if display_fps is not None:
        fps_line += f"  |  Display FPS: {display_fps:.1f}"
    lines = [
        f"{fps_line}  |  Pose: {infer_ms:.1f} ms",
        f"{width}x{height}  |  {backend}  {device}",
    ]
    if n_persons is not None:
        lines.append(f"Persons: {n_persons}")
    (tw, th), _ = cv2.getTextSize(lines[0], font, scale, thick)
    cv2.rectangle(vis, (0, 0), (max(tw + 14, 280), len(lines) * dy + 12), (0, 0, 0), -1)
    for i, line in enumerate(lines):
        cv2.putText(vis, line, (10, y + i * dy), font, scale, color, thick)


def _parse_ocr_roi(roi_str: str) -> tuple[float, float, float, float] | None:
    if not roi_str:
        return None
    try:
        parts = [float(x.strip()) for x in roi_str.split(",")]
        if len(parts) != 4:
            return None
        x, y, w, h = parts
        if w <= 0 or h <= 0:
            return None
        return (
            max(0.0, min(1.0, x)),
            max(0.0, min(1.0, y)),
            max(0.0, min(1.0, w)),
            max(0.0, min(1.0, h)),
        )
    except Exception:
        return None


def _select_roi_interactive(window_name: str, display_frame, mirrored: bool = True):
    """Open a modal ROI selector on the currently shown display frame.

    The display frame is usually mirrored (flipped on X) compared to the raw
    capture. We convert the user-drawn rectangle back into normalized coords
    of the ORIGINAL (unflipped) frame so they can be reused as --ocr-roi /
    --clock-roi values or applied to the raw capture for cropping.

    Returns (x, y, w, h) in [0,1] normalized original-frame coords, or None
    if the user cancelled / drew an empty rectangle.
    """
    if display_frame is None:
        return None
    h, w = display_frame.shape[:2]
    if h <= 0 or w <= 0:
        return None
    try:
        x, y, bw, bh = cv2.selectROI(window_name, display_frame, showCrosshair=True, fromCenter=False)
    except Exception:
        return None
    if bw <= 0 or bh <= 0:
        return None
    x_norm = x / float(w)
    y_norm = y / float(h)
    w_norm = bw / float(w)
    h_norm = bh / float(h)
    if mirrored:
        x_norm = max(0.0, 1.0 - (x_norm + w_norm))
    x_norm = max(0.0, min(1.0, x_norm))
    y_norm = max(0.0, min(1.0, y_norm))
    w_norm = max(0.0, min(1.0, w_norm))
    h_norm = max(0.0, min(1.0, h_norm))
    return (x_norm, y_norm, w_norm, h_norm)


def _draw_roi_overlay(display_frame, roi_norm, label: str, color, mirrored: bool = True) -> None:
    """Draw a labelled rectangle for an ROI on the (possibly mirrored) display frame."""
    if display_frame is None or roi_norm is None:
        return
    h, w = display_frame.shape[:2]
    if h <= 0 or w <= 0:
        return
    x, y, bw, bh = roi_norm
    if mirrored:
        x_disp = 1.0 - (x + bw)
    else:
        x_disp = x
    x0 = int(max(0, min(w - 1, x_disp * w)))
    y0 = int(max(0, min(h - 1, y * h)))
    x1 = int(max(0, min(w, (x_disp + bw) * w)))
    y1 = int(max(0, min(h, (y + bh) * h)))
    if x1 <= x0 or y1 <= y0:
        return
    cv2.rectangle(display_frame, (x0, y0), (x1, y1), color, 2)
    cv2.putText(
        display_frame,
        label,
        (x0 + 4, max(15, y0 - 6)),
        cv2.FONT_HERSHEY_SIMPLEX,
        0.5,
        color,
        1,
        cv2.LINE_AA,
    )


def _encode_clock_roi_base64(frame, roi_norm, jpeg_quality: int, max_dim: int = 0) -> str:
    """Crop `frame` to `roi_norm`, optionally downscale so longest side <= max_dim,
    JPEG-encode, base64. Downscaling is the single biggest latency win for small
    clock ROIs: a 400x200 crop -> 192x96 drops pixel count from 80k to ~18k,
    shrinking both the JPEG bytes and Unity's LoadImage/upload cost by ~4x.
    """
    h, w = frame.shape[:2]
    if h <= 0 or w <= 0:
        return ""
    x0, y0, x1, y1 = 0, 0, w, h
    if roi_norm is not None:
        rx, ry, rw, rh = roi_norm
        x0 = max(0, min(w, int(rx * w)))
        y0 = max(0, min(h, int(ry * h)))
        x1 = max(0, min(w, int((rx + rw) * w)))
        y1 = max(0, min(h, int((ry + rh) * h)))
        if x1 <= x0 or y1 <= y0:
            return ""
    crop = frame[y0:y1, x0:x1]
    if crop.size == 0:
        return ""
    if max_dim and max_dim > 0:
        ch, cw = crop.shape[:2]
        longest = max(ch, cw)
        if longest > max_dim:
            scale = float(max_dim) / float(longest)
            new_w = max(1, int(round(cw * scale)))
            new_h = max(1, int(round(ch * scale)))
            crop = cv2.resize(crop, (new_w, new_h), interpolation=cv2.INTER_AREA)
    q = max(1, min(100, int(jpeg_quality)))
    ok, enc = cv2.imencode(".jpg", crop, [int(cv2.IMWRITE_JPEG_QUALITY), q])
    if not ok:
        return ""
    return base64.b64encode(enc.tobytes()).decode("ascii")


def _extract_ocr_text(
    frame,
    roi_norm,
    whitelist: str,
    flip_x: bool = False,
    exhaustive: bool = False,
) -> tuple[str, dict]:
    try:
        import pytesseract  # type: ignore[reportMissingImports]
    except Exception:
        return "", {"error": "pytesseract_missing"}
    h, w = frame.shape[:2]
    if h <= 0 or w <= 0:
        return "", {"error": "invalid_frame_shape"}
    x0, y0, x1, y1 = 0, 0, w, h
    if roi_norm is not None:
        rx, ry, rw, rh = roi_norm
        x0 = max(0, min(w, int(rx * w)))
        y0 = max(0, min(h, int(ry * h)))
        x1 = max(0, min(w, int((rx + rw) * w)))
        y1 = max(0, min(h, int((ry + rh) * h)))
        if x1 <= x0 or y1 <= y0:
            return "", {"error": "invalid_roi_pixels", "roi_px": (x0, y0, x1, y1)}
    crop = frame[y0:y1, x0:x1]
    if crop.size == 0:
        return "", {"error": "empty_crop", "roi_px": (x0, y0, x1, y1)}
    if flip_x:
        crop = cv2.flip(crop, 1)
    gray = cv2.cvtColor(crop, cv2.COLOR_BGR2GRAY)
    gray = cv2.GaussianBlur(gray, (3, 3), 0)
    _, bw = cv2.threshold(gray, 0, 255, cv2.THRESH_BINARY + cv2.THRESH_OTSU)

    if exhaustive:
        # High-accuracy debug mode (slow).
        variants = {
            "bw": bw,
            "inv": cv2.bitwise_not(bw),
            "gray": gray,
            "bw_x2": cv2.resize(bw, None, fx=2.0, fy=2.0, interpolation=cv2.INTER_CUBIC),
            "inv_x2": cv2.resize(cv2.bitwise_not(bw), None, fx=2.0, fy=2.0, interpolation=cv2.INTER_CUBIC),
        }
        psm_list = (7, 6, 8, 13)
    else:
        # Real-time default: few robust attempts only.
        variants = {
            "bw": bw,
            "inv_x2": cv2.resize(cv2.bitwise_not(bw), None, fx=2.0, fy=2.0, interpolation=cv2.INTER_CUBIC),
        }
        psm_list = (7, 6)

    best_text = ""
    best_raw = ""
    best_score = -1e9
    best_mode = ""
    attempt_count = 0
    t0 = time.perf_counter()
    for vname, img in variants.items():
        for psm in psm_list:
            attempt_count += 1
            cfg = f"--oem 3 --psm {psm} -c tessedit_char_whitelist={whitelist}"
            try:
                data = pytesseract.image_to_data(img, config=cfg, output_type=pytesseract.Output.DICT)
            except Exception:
                continue
            texts = data.get("text", []) if isinstance(data, dict) else []
            confs = data.get("conf", []) if isinstance(data, dict) else []
            raw_join = "".join((t or "") for t in texts)
            clean = re.sub(r"\s+", "", raw_join)
            clean = "".join(ch for ch in clean if ch in whitelist)
            if not clean:
                continue
            conf_vals = []
            for c in confs:
                try:
                    v = float(c)
                    if v >= 0:
                        conf_vals.append(v)
                except Exception:
                    pass
            avg_conf = (sum(conf_vals) / len(conf_vals)) if conf_vals else 0.0
            digit_count = sum(ch.isdigit() for ch in clean)
            colon_count = clean.count(":")
            # Prefer digit-rich outputs and penalize punctuation-only noise.
            score = avg_conf + (digit_count * 12.0) + (len(clean) * 3.0) + (colon_count * 2.0)
            if digit_count == 0:
                score -= 40.0
            if clean in (":", "::", ".", "..", ":", ":::"):
                score -= 80.0
            if score > best_score:
                best_score = score
                best_text = clean
                best_raw = raw_join.strip()
                best_mode = f"{vname}/psm{psm}"
    ocr_ms = (time.perf_counter() - t0) * 1000.0
    clean_txt = best_text
    dbg = {
        "roi_px": (x0, y0, x1, y1),
        "crop_shape": crop.shape[:2],
        "gray_mean": float(gray.mean()) if gray.size else 0.0,
        "bw_mean": float(bw.mean()) if bw.size else 0.0,
        "ocr_ms": ocr_ms,
        "raw": best_raw,
        "clean": clean_txt,
        "best_mode": best_mode,
        "attempts": attempt_count,
    }
    return clean_txt, dbg


def _extract_ocr_text_rapid(
    frame,
    roi_norm,
    whitelist: str,
    flip_x: bool,
    engine,
) -> tuple[str, dict]:
    from rapid_ocr_engine import run_rapid_ocr_on_crop

    if engine is None:
        return "", {"error": "rapid_engine_missing"}
    h, w = frame.shape[:2]
    if h <= 0 or w <= 0:
        return "", {"error": "invalid_frame_shape"}
    x0, y0, x1, y1 = 0, 0, w, h
    if roi_norm is not None:
        rx, ry, rw, rh = roi_norm
        x0 = max(0, min(w, int(rx * w)))
        y0 = max(0, min(h, int(ry * h)))
        x1 = max(0, min(w, int((rx + rw) * w)))
        y1 = max(0, min(h, int((ry + rh) * h)))
        if x1 <= x0 or y1 <= y0:
            return "", {"error": "invalid_roi_pixels", "roi_px": (x0, y0, x1, y1)}
    crop = frame[y0:y1, x0:x1]
    if crop.size == 0:
        return "", {"error": "empty_crop", "roi_px": (x0, y0, x1, y1)}
    if flip_x:
        crop = cv2.flip(crop, 1)
    clean_txt, dbg = run_rapid_ocr_on_crop(engine, crop, whitelist)
    dbg = dict(dbg)
    dbg["roi_px"] = (x0, y0, x1, y1)
    dbg.setdefault("gray_mean", float(crop.mean()) if crop.size else 0.0)
    dbg.setdefault("bw_mean", 0.0)
    dbg.setdefault("best_mode", "rapidocr/onnx")
    dbg.setdefault("attempts", 1)
    return clean_txt, dbg


def _ocr_backend_ready(args, rapid_engine) -> bool:
    if not args.ocr_enable:
        return False
    if getattr(args, "ocr_backend", "tesseract") == "rapid":
        return rapid_engine is not None
    try:
        import pytesseract  # type: ignore[reportMissingImports]
        return pytesseract is not None
    except Exception:
        return False


def main():
    args = parse_args()
    _maybe_reexec_with_venv310_for_rtmlib(args.backend)
    if getattr(args, "ocr_enable", False):
        print("OCR is disabled in pose.py for now (pose-only mode).")
    args.ocr_enable = False

    if args.device == "cuda":
        _ensure_cuda_in_path()

    camera_candidates = [args.camera]
    if args.camera_mode == "flir" and args.camera == 0:
        # FLIR/PySpin camera index is usually 0 (first FLIR camera),
        # but keep a fallback for systems exposing another index order.
        camera_candidates = [0, 1]

    cap = None
    camera_used = None
    camera_api = ""
    for idx in camera_candidates:
        if args.camera_mode == "flir":
            cap_try, api = _open_flir_camera(idx)
        else:
            cap_try, api = _open_camera_with_mode(idx, args.camera_mode, getattr(args, "camera_api", "auto"))
        if cap_try is not None:
            cap = cap_try
            camera_used = idx
            camera_api = api
            break
    if cap is None:
        tried = ", ".join(str(x) for x in camera_candidates)
        msg = (
            f"Cannot open camera for mode '{args.camera_mode}'. Tried index(es): {tried}. "
            "Use --camera N to pick a specific device."
        )
        if args.camera_mode == "flir":
            msg += (
                " Ensure spinnaker_python is installed in app/.venv310 and the camera is visible in SpinView."
            )
        print(msg)
        sys.exit(1)

    backend = args.backend
    device = args.device
    if backend == "rtmlib" and device == "cuda" and not _onnx_cuda_provider_available():
        print("CUDAExecutionProvider not available in current ONNX Runtime. Falling back to CPU.")
        device = "cpu"
    if backend == "rtmlib":
        try:
            pose_tracker = create_rtmlib_tracker(
                device, args.mode, args.det_frequency
            )
        except ImportError as e:
            print("rtmlib not found. Install with: pip install -r requirements.txt")
            sys.exit(1)
        except Exception as e:
            if device == "cuda":
                print("GPU failed, falling back to CPU:", e)
                device = "cpu"
                pose_tracker = create_rtmlib_tracker(
                    device, args.mode, args.det_frequency
                )
            else:
                raise
        run_frame = lambda f: run_rtmlib_frame(pose_tracker, f, args.show_fps, args.no_viz)
    else:
        try:
            inferencer = create_mmpose_inferencer(args.device)
        except RuntimeError as e:
            print(e)
            sys.exit(1)
        run_frame = lambda f: run_mmpose_frame(inferencer, f, args.show_fps, args.no_viz)

    if args.camera_mode == "webcam":
        _configure_webcam_capture(cap, args)
    elif args.width > 0 or args.height > 0:
        if args.width > 0:
            cap.set(cv2.CAP_PROP_FRAME_WIDTH, args.width)
        if args.height > 0:
            cap.set(cv2.CAP_PROP_FRAME_HEIGHT, args.height)
        _ = cap.read()

    # Warmup: run a few inferences so first real frame isn't cold (GPU/ONNX)
    warmup_frames = 5
    for _ in range(warmup_frames):
        ok, warm = cap.read()
        if not ok:
            break
        run_frame(warm)
    print(f"Warmup: {warmup_frames} frames.")

    rapid_ocr_engine = None
    if args.ocr_enable and getattr(args, "ocr_backend", "tesseract") == "rapid":
        try:
            from rapid_ocr_engine import (
                create_rapid_ocr_engine,
                onnx_cuda_available,
                run_rapid_ocr_on_crop,
            )

            use_ocr_cuda = device == "cuda" and onnx_cuda_available()
            if device == "cuda" and not onnx_cuda_available():
                print(
                    "Warning: CUDA pose requested but CUDAExecutionProvider missing for ONNX; "
                    "RapidOCR will use CPU. Use: pip uninstall onnxruntime -y && pip install onnxruntime-gpu"
                )
            rapid_ocr_engine = create_rapid_ocr_engine(
                use_cuda=use_ocr_cuda,
                use_text_det=getattr(args, "ocr_rapid_use_text_det", False),
            )
            import numpy as _np

            _warm_crop = _np.zeros((32, 160, 3), dtype=_np.uint8)
            run_rapid_ocr_on_crop(rapid_ocr_engine, _warm_crop, args.ocr_whitelist or "0123456789")
        except Exception as e:
            print(f"Warning: RapidOCR failed to load ({e}). Install: pip install rapidocr-onnxruntime")
            rapid_ocr_engine = None

    udp_sock = None
    udp_prev_pose = [None]
    if args.udp_port > 0:
        try:
            udp_sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
            print(f"Pose UDP: broadcasting to {args.udp_host}:{args.udp_port} (Unity Architect).")
        except OSError as e:
            print(f"Warning: could not create UDP socket: {e}")
    if getattr(args, "smooth_pose", 0) > 0:
        print(f"Pose smoothing: alpha={args.smooth_pose} (0=off for lowest latency).")

    if getattr(args, "log_latency", False):
        print("Latency logging: ON (capture-to-pose ms). Summary at exit.")
    print(
        f"Backend: {backend}  Camera: {camera_used} ({args.camera_mode}/{camera_api})  Device: {device}"
    )
    if args.threaded:
        print("Threaded mode: capture+inference in background, display on main thread.")
    if args.ocr_enable:
        _ob = getattr(args, "ocr_backend", "tesseract")
        if _ob == "rapid":
            if rapid_ocr_engine is None:
                print("Warning: OCR backend is rapid but RapidOCR did not load. Use tesseract or fix install.")
            else:
                from rapid_ocr_engine import onnx_cuda_available

                print(
                    f"OCR: ON (RapidOCR/ONNX) | ort_cuda={onnx_cuda_available()} | "
                    f"interval={max(1, int(args.ocr_interval))} | "
                    f"max_fps={max(0.1, float(args.ocr_max_fps)):.1f} | "
                    f"flip_x={'yes' if args.ocr_flip_x else 'no'} | "
                    f"roi={args.ocr_roi or 'full-frame'} | "
                    f"text_det={'yes' if args.ocr_rapid_use_text_det else 'no'}"
                )
        else:
            try:
                import pytesseract  # type: ignore[reportMissingImports]
            except Exception:
                pytesseract = None
        if _ob == "tesseract" and pytesseract is None:
            print("Warning: OCR enabled but pytesseract is missing. Install with: pip install pytesseract")
        elif _ob == "tesseract":
            print(
                f"OCR: ON (Tesseract) | interval={max(1, int(args.ocr_interval))} | "
                f"max_fps={max(0.1, float(args.ocr_max_fps)):.1f} | "
                f"flip_x={'yes' if args.ocr_flip_x else 'no'} | "
                f"roi={args.ocr_roi or 'full-frame'}"
            )
    if args.max_frames > 0:
        print(f"Auto-stop: enabled after {args.max_frames} processed frames.")
    if getattr(args, "clock_stream_enable", False):
        _clock_max_fps = max(0.1, float(getattr(args, "clock_stream_max_fps", 10.0)))
        _clock_q = int(getattr(args, "clock_stream_jpeg_quality", 70))
        print(
            f"Clock ROI stream: ON | max_fps={_clock_max_fps:.1f} | "
            f"jpeg_q={_clock_q} | roi={getattr(args, 'clock_roi', '') or 'full-frame'}"
        )
    if args.no_window:
        print("Window: disabled (--no-window).")
    else:
        print("Keys: [Q] quit | [C] select clock ROI | [O] select OCR ROI | [X] clear ROIs")
    print("(Console: 'load ... onnx' and 'Tracking is on' from rtmlib are normal. Camera warnings are usually harmless.)")

    window_name = f"Pose ({backend}) — Q to quit"
    if not args.no_window:
        cv2.namedWindow(window_name, cv2.WINDOW_NORMAL)

    fps_alpha = 0.2
    fps_smooth = 30.0
    infer_smooth_ms = 10.0
    latency_samples = []  # (loop_ms, infer_ms) when --log-latency
    ocr_roi = _parse_ocr_roi(args.ocr_roi)
    clock_roi = _parse_ocr_roi(getattr(args, "clock_roi", ""))
    clock_stream_enable = bool(getattr(args, "clock_stream_enable", False))
    clock_stream_max_fps = max(0.1, float(getattr(args, "clock_stream_max_fps", 10.0)))
    clock_stream_min_dt = 1.0 / clock_stream_max_fps
    clock_stream_jpeg_quality = int(getattr(args, "clock_stream_jpeg_quality", 55))
    clock_stream_downscale = int(getattr(args, "clock_downscale", 192) or 0)
    clock_stream_separate_udp = bool(getattr(args, "clock_stream_separate_udp", True))
    clock_stream_state = {"last_run_t": 0.0, "last_payload": ""}
    ocr_interval = max(1, int(args.ocr_interval))
    ocr_max_fps = max(0.1, float(args.ocr_max_fps))
    ocr_min_dt = 1.0 / ocr_max_fps
    ocr_state = {"idx": 0, "last": "", "last_run_t": 0.0, "last_print_t": 0.0, "dbg_print_t": 0.0}

    if args.threaded:
        # Single-slot "latest" result; worker overwrites, main thread displays (decouples imshow from inference)
        latest_lock = threading.Lock()
        latest_result = None
        stop_worker = threading.Event()
        worker_state = {"processed_frames": 0}

        def worker():
            while not stop_worker.is_set():
                ok, frame = cap.read()
                if not ok:
                    break
                t_capture = time.perf_counter()

                # LOW-LATENCY CLOCK ROI: encode + ship ROI BEFORE pose inference.
                # The clock stream is independent of the pose pipeline, so by
                # sending here we avoid ~15-30 ms of inference lag per frame.
                if clock_stream_enable and clock_stream_separate_udp:
                    if (t_capture - clock_stream_state["last_run_t"]) >= clock_stream_min_dt:
                        early_roi = _encode_clock_roi_base64(
                            frame, clock_roi, clock_stream_jpeg_quality, clock_stream_downscale
                        )
                        if early_roi:
                            send_clock_roi_udp(udp_sock, args.udp_host, args.udp_port, early_roi)
                            clock_stream_state["last_payload"] = early_roi
                            clock_stream_state["last_run_t"] = t_capture

                vis, infer_ms, n_persons, pose_data = run_frame(frame)
                if args.ocr_enable and _ocr_backend_ready(args, rapid_ocr_engine):
                    now = time.perf_counter()
                    by_interval = (ocr_state["idx"] % ocr_interval) == 0
                    by_rate = (now - ocr_state["last_run_t"]) >= ocr_min_dt
                    should_run = by_interval and by_rate
                    if should_run:
                        if getattr(args, "ocr_backend", "tesseract") == "rapid":
                            ocr_state["last"], dbg = _extract_ocr_text_rapid(
                                frame,
                                ocr_roi,
                                args.ocr_whitelist,
                                args.ocr_flip_x,
                                rapid_ocr_engine,
                            )
                        else:
                            ocr_state["last"], dbg = _extract_ocr_text(
                                frame, ocr_roi, args.ocr_whitelist, args.ocr_flip_x, args.ocr_exhaustive
                            )
                        ocr_state["last_run_t"] = now
                        if args.ocr_debug and (now - ocr_state["dbg_print_t"] >= 0.5):
                            print(
                                "OCR DEBUG run:"
                                f" frame={ocr_state['idx']}"
                                f" roi_px={dbg.get('roi_px')}"
                                f" crop={dbg.get('crop_shape')}"
                                f" gray_mean={dbg.get('gray_mean', 0):.1f}"
                                f" bw_mean={dbg.get('bw_mean', 0):.1f}"
                                f" ocr_ms={dbg.get('ocr_ms', 0):.1f}"
                                f" raw='{dbg.get('raw', '')}'"
                                f" clean='{dbg.get('clean', '')}'"
                                f" mode={dbg.get('best_mode', '-')}"
                                f" attempts={dbg.get('attempts', 0)}"
                                f" flip_x={'yes' if args.ocr_flip_x else 'no'}"
                            )
                            ocr_state["dbg_print_t"] = now
                    elif args.ocr_debug and (now - ocr_state["dbg_print_t"] >= 0.5):
                        print(
                            "OCR DEBUG skip:"
                            f" frame={ocr_state['idx']}"
                            f" by_interval={'yes' if by_interval else 'no'}"
                            f" by_rate={'yes' if by_rate else 'no'}"
                            f" dt={(now - ocr_state['last_run_t']):.3f}s"
                        )
                        ocr_state["dbg_print_t"] = now
                    if args.ocr_print_console and (now - ocr_state["last_print_t"] >= max(0.05, float(args.ocr_print_interval))):
                        print(f"OCR: {ocr_state['last'] or '--'}")
                        ocr_state["last_print_t"] = now
                    if pose_data is not None:
                        pose_data["ocrText"] = ocr_state["last"]
                    ocr_state["idx"] += 1
                t_after = time.perf_counter()
                roi_payload = None
                # Legacy bundled path: only used when --no-clock-stream-separate-udp.
                # With the default separate-UDP mode the ROI was already sent above.
                if clock_stream_enable and not clock_stream_separate_udp:
                    if (t_after - clock_stream_state["last_run_t"]) >= clock_stream_min_dt:
                        roi_payload = _encode_clock_roi_base64(
                            frame, clock_roi, clock_stream_jpeg_quality, clock_stream_downscale
                        )
                        clock_stream_state["last_payload"] = roi_payload
                        clock_stream_state["last_run_t"] = t_after
                    else:
                        roi_payload = clock_stream_state["last_payload"]
                if getattr(args, "log_latency", False):
                    loop_ms = (t_after - t_capture) * 1000
                    latency_samples.append((loop_ms, infer_ms))
                send_pose_udp(
                    udp_sock,
                    args.udp_host,
                    args.udp_port,
                    pose_data,
                    getattr(args, "smooth_pose", 0),
                    udp_prev_pose,
                    ocr_state["last"] if args.ocr_enable else None,
                    roi_payload if (clock_stream_enable and not clock_stream_separate_udp) else None,
                )
                with latest_lock:
                    nonlocal latest_result
                    latest_result = {
                        "vis": vis,
                        "infer_ms": infer_ms,
                        "n_persons": n_persons,
                        "w": vis.shape[1],
                        "h": vis.shape[0],
                        "t": time.perf_counter(),
                    }
                worker_state["processed_frames"] += 1
                if args.max_frames > 0 and worker_state["processed_frames"] >= args.max_frames:
                    stop_worker.set()

        worker_thread = threading.Thread(target=worker, daemon=True)
        worker_thread.start()

        prev_t = None
        prev_presented_frame_t = None
        prev_display_t = None
        display_fps_smooth = 60.0
        last_display_vis = None
        while True:
            with latest_lock:
                if latest_result is not None:
                    data = {
                        "vis": latest_result["vis"].copy(),
                        "infer_ms": latest_result["infer_ms"],
                        "n_persons": latest_result["n_persons"],
                        "w": latest_result["w"],
                        "h": latest_result["h"],
                        "t": latest_result["t"],
                    }
                else:
                    data = None
            if data is not None:
                # Only measure FPS when we have a new frame (timestamp changed). In threaded
                # mode the same result can be displayed many times, so elapsed would be ~0.
                if prev_t is not None:
                    elapsed = data["t"] - prev_t
                    if elapsed > 0:
                        fps_smooth = fps_alpha * (1.0 / elapsed) + (1 - fps_alpha) * fps_smooth
                        prev_t = data["t"]
                else:
                    prev_t = data["t"]
                infer_smooth_ms = fps_alpha * data["infer_ms"] + (1 - fps_alpha) * infer_smooth_ms
                has_new_presentable_frame = (prev_presented_frame_t is None) or (data["t"] != prev_presented_frame_t)
                if (not args.no_window) and has_new_presentable_frame:
                    now_display = time.perf_counter()
                    if prev_display_t is not None:
                        dt_display = now_display - prev_display_t
                        if dt_display > 0:
                            display_fps_smooth = (
                                fps_alpha * (1.0 / dt_display) + (1 - fps_alpha) * display_fps_smooth
                            )
                    prev_display_t = now_display
                    prev_presented_frame_t = data["t"]
                    display_vis = cv2.flip(data["vis"], 1)
                    draw_stats(
                        display_vis,
                        fps_smooth,
                        display_fps_smooth,
                        infer_smooth_ms,
                        data["w"],
                        data["h"],
                        backend,
                        device,
                        data["n_persons"],
                    )
                    _draw_roi_overlay(display_vis, clock_roi, "CLOCK ROI", (0, 255, 255))
                    _draw_roi_overlay(display_vis, ocr_roi, "OCR ROI", (0, 255, 0))
                    cv2.imshow(window_name, display_vis)
                    last_display_vis = display_vis
            if args.no_window:
                if stop_worker.is_set() and not worker_thread.is_alive():
                    break
                time.sleep(0.001)
            else:
                key = cv2.waitKey(1) & 0xFF
                if key == ord("q"):
                    break
                elif key == ord("c"):
                    sel = _select_roi_interactive(window_name, last_display_vis)
                    if sel is not None:
                        clock_roi = sel
                        print(
                            f"[CLOCK] New ROI (normalized, original frame): "
                            f"{sel[0]:.3f},{sel[1]:.3f},{sel[2]:.3f},{sel[3]:.3f}"
                        )
                elif key == ord("o"):
                    sel = _select_roi_interactive(window_name, last_display_vis)
                    if sel is not None:
                        ocr_roi = sel
                        print(
                            f"[OCR] New ROI (normalized, original frame): "
                            f"{sel[0]:.3f},{sel[1]:.3f},{sel[2]:.3f},{sel[3]:.3f}"
                        )
                elif key == ord("x"):
                    clock_roi = None
                    ocr_roi = None
                    print("[ROI] Cleared clock + OCR ROIs (using full frame).")
        stop_worker.set()
        worker_thread.join(timeout=1.5)
    else:
        processed_frames = 0
        prev_display_t = None
        display_fps_smooth = 60.0
        while True:
            ok, frame = cap.read()
            if not ok:
                break
            t_capture = time.perf_counter()

            # LOW-LATENCY CLOCK ROI (non-threaded path): send BEFORE pose inference.
            if clock_stream_enable and clock_stream_separate_udp:
                if (t_capture - clock_stream_state["last_run_t"]) >= clock_stream_min_dt:
                    early_roi = _encode_clock_roi_base64(
                        frame, clock_roi, clock_stream_jpeg_quality, clock_stream_downscale
                    )
                    if early_roi:
                        send_clock_roi_udp(udp_sock, args.udp_host, args.udp_port, early_roi)
                        clock_stream_state["last_payload"] = early_roi
                        clock_stream_state["last_run_t"] = t_capture

            vis, infer_ms, n_persons, pose_data = run_frame(frame)
            if args.ocr_enable and _ocr_backend_ready(args, rapid_ocr_engine):
                now = time.perf_counter()
                by_interval = (ocr_state["idx"] % ocr_interval) == 0
                by_rate = (now - ocr_state["last_run_t"]) >= ocr_min_dt
                should_run = by_interval and by_rate
                if should_run:
                    if getattr(args, "ocr_backend", "tesseract") == "rapid":
                        ocr_state["last"], dbg = _extract_ocr_text_rapid(
                            frame,
                            ocr_roi,
                            args.ocr_whitelist,
                            args.ocr_flip_x,
                            rapid_ocr_engine,
                        )
                    else:
                        ocr_state["last"], dbg = _extract_ocr_text(
                            frame, ocr_roi, args.ocr_whitelist, args.ocr_flip_x, args.ocr_exhaustive
                        )
                    ocr_state["last_run_t"] = now
                    if args.ocr_debug and (now - ocr_state["dbg_print_t"] >= 0.5):
                        print(
                            "OCR DEBUG run:"
                            f" frame={ocr_state['idx']}"
                            f" roi_px={dbg.get('roi_px')}"
                            f" crop={dbg.get('crop_shape')}"
                            f" gray_mean={dbg.get('gray_mean', 0):.1f}"
                            f" bw_mean={dbg.get('bw_mean', 0):.1f}"
                            f" ocr_ms={dbg.get('ocr_ms', 0):.1f}"
                            f" raw='{dbg.get('raw', '')}'"
                            f" clean='{dbg.get('clean', '')}'"
                            f" mode={dbg.get('best_mode', '-')}"
                            f" attempts={dbg.get('attempts', 0)}"
                            f" flip_x={'yes' if args.ocr_flip_x else 'no'}"
                        )
                        ocr_state["dbg_print_t"] = now
                elif args.ocr_debug and (now - ocr_state["dbg_print_t"] >= 0.5):
                    print(
                        "OCR DEBUG skip:"
                        f" frame={ocr_state['idx']}"
                        f" by_interval={'yes' if by_interval else 'no'}"
                        f" by_rate={'yes' if by_rate else 'no'}"
                        f" dt={(now - ocr_state['last_run_t']):.3f}s"
                    )
                    ocr_state["dbg_print_t"] = now
                if args.ocr_print_console and (now - ocr_state["last_print_t"] >= max(0.05, float(args.ocr_print_interval))):
                    print(f"OCR: {ocr_state['last'] or '--'}")
                    ocr_state["last_print_t"] = now
                if pose_data is not None:
                    pose_data["ocrText"] = ocr_state["last"]
                ocr_state["idx"] += 1
            t_after = time.perf_counter()
            roi_payload = None
            # Legacy bundled path: only when --no-clock-stream-separate-udp.
            if clock_stream_enable and not clock_stream_separate_udp:
                if (t_after - clock_stream_state["last_run_t"]) >= clock_stream_min_dt:
                    roi_payload = _encode_clock_roi_base64(
                        frame, clock_roi, clock_stream_jpeg_quality, clock_stream_downscale
                    )
                    clock_stream_state["last_payload"] = roi_payload
                    clock_stream_state["last_run_t"] = t_after
                else:
                    roi_payload = clock_stream_state["last_payload"]
            if getattr(args, "log_latency", False):
                loop_ms = (t_after - t_capture) * 1000
                latency_samples.append((loop_ms, infer_ms))
            send_pose_udp(
                udp_sock,
                args.udp_host,
                args.udp_port,
                pose_data,
                getattr(args, "smooth_pose", 0),
                udp_prev_pose,
                ocr_state["last"] if args.ocr_enable else None,
                roi_payload if (clock_stream_enable and not clock_stream_separate_udp) else None,
            )
            elapsed = t_after - t_capture
            h, w = vis.shape[:2]

            fps_smooth = fps_alpha * (1.0 / max(elapsed, 1e-6)) + (1 - fps_alpha) * fps_smooth
            infer_smooth_ms = fps_alpha * infer_ms + (1 - fps_alpha) * infer_smooth_ms
            if not args.no_window:
                now_display = time.perf_counter()
                if prev_display_t is not None:
                    dt_display = now_display - prev_display_t
                    if dt_display > 0:
                        display_fps_smooth = (
                            fps_alpha * (1.0 / dt_display) + (1 - fps_alpha) * display_fps_smooth
                        )
                prev_display_t = now_display
                display_vis = cv2.flip(vis, 1)
                draw_stats(
                    display_vis,
                    fps_smooth,
                    display_fps_smooth,
                    infer_smooth_ms,
                    w,
                    h,
                    backend,
                    device,
                    n_persons,
                )
                _draw_roi_overlay(display_vis, clock_roi, "CLOCK ROI", (0, 255, 255))
                _draw_roi_overlay(display_vis, ocr_roi, "OCR ROI", (0, 255, 0))
                cv2.imshow(window_name, display_vis)
            processed_frames += 1
            if args.max_frames > 0 and processed_frames >= args.max_frames:
                break
            if not args.no_window:
                key = cv2.waitKey(1) & 0xFF
                if key == ord("q"):
                    break
                elif key == ord("c"):
                    sel = _select_roi_interactive(window_name, display_vis if not args.no_window else None)
                    if sel is not None:
                        clock_roi = sel
                        print(
                            f"[CLOCK] New ROI (normalized, original frame): "
                            f"{sel[0]:.3f},{sel[1]:.3f},{sel[2]:.3f},{sel[3]:.3f}"
                        )
                elif key == ord("o"):
                    sel = _select_roi_interactive(window_name, display_vis if not args.no_window else None)
                    if sel is not None:
                        ocr_roi = sel
                        print(
                            f"[OCR] New ROI (normalized, original frame): "
                            f"{sel[0]:.3f},{sel[1]:.3f},{sel[2]:.3f},{sel[3]:.3f}"
                        )
                elif key == ord("x"):
                    clock_roi = None
                    ocr_roi = None
                    print("[ROI] Cleared clock + OCR ROIs (using full frame).")

    cap.release()
    if udp_sock:
        try:
            udp_sock.close()
        except Exception:
            pass
    if not args.no_window:
        cv2.destroyAllWindows()

    # Latency summary (thesis: report mean, p95, n; compare to 63 ms / 125 ms thresholds)
    if getattr(args, "log_latency", False) and latency_samples:
        import statistics
        loop_ms_list = [x[0] for x in latency_samples]
        infer_ms_list = [x[1] for x in latency_samples]
        n = len(loop_ms_list)
        mean_loop = statistics.mean(loop_ms_list)
        median_loop = statistics.median(loop_ms_list)
        sorted_loop = sorted(loop_ms_list)
        p95_idx = min(int(0.95 * n), n - 1) if n else 0
        p95_loop = sorted_loop[p95_idx] if sorted_loop else 0
        mean_infer = statistics.mean(infer_ms_list)
        print("--- Latency (capture-to-pose) ---")
        print(f"  n = {n}  |  mean = {mean_loop:.2f} ms  |  median = {median_loop:.2f} ms  |  p95 = {p95_loop:.2f} ms")
        print(f"  inference mean = {mean_infer:.2f} ms")
        csv_path = getattr(args, "latency_csv", "") or ""
        if csv_path:
            try:
                with open(csv_path, "w", encoding="utf-8") as f:
                    f.write("loop_ms,infer_ms\n")
                    for a, b in latency_samples:
                        f.write(f"{a:.3f},{b:.3f}\n")
                print(f"  Wrote {n} rows to {csv_path}")
            except OSError as e:
                print(f"  Warning: could not write CSV: {e}", file=sys.stderr)


if __name__ == "__main__":
    main()
