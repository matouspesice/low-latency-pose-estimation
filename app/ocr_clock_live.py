#!/usr/bin/env python3
"""
Low-latency standalone OCR for digital clock text (terminal output only).

Design goals:
- Same inference stack family as pose (`rapidocr-onnxruntime` + ONNX Runtime).
- Optional NVIDIA GPU via `onnxruntime-gpu`.
- Same camera selection idea as pose: webcam default, optional FLIR/PySpin.
- Threaded latest-frame pipeline for low latency.

Examples:
  python ocr_clock_live.py
  python ocr_clock_live.py --device cuda --camera-mode flir
  python ocr_clock_live.py --target-pattern "(\\d{2}:\\d{2}:\\d{2}\\.\\d{3})"
"""

from __future__ import annotations

import argparse
import json
import os
import re
import sys
import threading
import time

import cv2

_APP_DIR = os.path.dirname(os.path.abspath(__file__))
if _APP_DIR not in sys.path:
    sys.path.insert(0, _APP_DIR)

from rapid_ocr_engine import (
    create_rapid_ocr_engine,
    extract_target_text,
    onnx_cuda_available,
    run_rapid_ocr_on_crop,
)
from win_cuda_path import _ensure_cuda_in_path


def _log(msg: str) -> None:
    print(msg, flush=True)


def parse_args():
    p = argparse.ArgumentParser(
        description="Standalone low-latency OCR clock reader (terminal output)."
    )
    p.add_argument("--camera-mode", type=str, default="webcam", choices=("webcam", "flir"))
    p.add_argument("--camera", type=int, default=0, help="Camera index (webcam mode) or FLIR index.")
    p.add_argument(
        "--camera-api",
        type=str,
        default="dshow",
        choices=("auto", "msmf", "dshow", "default"),
        help="Webcam API on Windows. dshow is often lower-latency for manual controls.",
    )
    p.add_argument("--width", type=int, default=640)
    p.add_argument("--height", type=int, default=480)
    p.add_argument("--camera-fps", type=float, default=60.0, help="Requested camera FPS (0 = keep current).")
    p.add_argument(
        "--camera-auto-exposure",
        type=str,
        default="manual",
        choices=("keep", "auto", "manual"),
        help="Webcam auto-exposure mode override.",
    )
    p.add_argument(
        "--camera-exposure",
        type=float,
        default=-6.0,
        help="Requested exposure value (driver-specific units; 0 = keep current).",
    )
    p.add_argument(
        "--camera-auto-wb",
        type=str,
        default="off",
        choices=("keep", "on", "off"),
        help="Webcam auto white balance override.",
    )
    p.add_argument("--device", type=str, default="cuda", choices=("cpu", "cuda"))
    p.add_argument(
        "--ocr-roi",
        type=str,
        default="0,0,1,1",
        help="Normalized ROI x,y,w,h (default full frame).",
    )
    p.add_argument(
        "--ocr-whitelist",
        type=str,
        default="0123456789:,.",
        help="Keep only these chars after OCR (default: digits + : , .).",
    )
    p.add_argument("--ocr-max-fps", type=float, default=20.0, help="Max OCR runs per second.")
    p.add_argument("--print-interval", type=float, default=0.2, help="Seconds between terminal prints.")
    p.add_argument(
        "--target-pattern",
        type=str,
        default=r"(\d{1,2}:\d{2}[,\.]\d{1,3})",
        help="Regex to extract target clock text; group 1 is preferred when present.",
    )
    p.add_argument("--require-stable", type=int, default=1, help="Require N consecutive equal parses.")
    p.add_argument(
        "--rapid-use-text-det",
        action="store_true",
        default=False,
        help="Use full text detection (slower). Keep OFF for tight, single-line clock ROI.",
    )
    p.add_argument(
        "--no-rapid-use-text-det",
        dest="rapid_use_text_det",
        action="store_false",
        help="Disable detector and treat ROI as one line (faster for tight crop).",
    )
    p.add_argument(
        "--rapid-use-angle-cls",
        action="store_true",
        default=False,
        help="Enable angle classifier (slower). Keep OFF when the clock is upright.",
    )
    p.add_argument(
        "--no-rapid-use-angle-cls",
        dest="rapid_use_angle_cls",
        action="store_false",
        help="Disable angle classifier (faster).",
    )
    p.add_argument(
        "--ocr-scale",
        type=float,
        default=1.0,
        help="Scale OCR input (e.g. 0.75 for lower latency, 1.0 default).",
    )
    p.add_argument(
        "--roi-padding",
        type=float,
        default=0.08,
        help="Extra normalized padding added around interactively selected ROI (default: 0.08).",
    )
    p.add_argument("--debug", action="store_true", help="Print OCR debug details.")
    p.add_argument("--show-window", action="store_true", default=True, help="Show live preview window.")
    p.add_argument("--no-window", dest="show_window", action="store_false", help="Disable preview window.")
    p.add_argument("--flip-preview", action="store_true", default=True, help="Mirror preview.")
    p.add_argument("--no-flip-preview", dest="flip_preview", action="store_false")
    p.add_argument("--show-stats", action="store_true", default=True, help="Draw FPS and OCR stats overlay.")
    p.add_argument("--no-show-stats", dest="show_stats", action="store_false")
    p.add_argument(
        "--roi-file",
        type=str,
        default=os.path.join(_APP_DIR, "ocr_clock_live_roi.json"),
        help="Path to save/load interactive ROI.",
    )
    p.add_argument(
        "--load-roi",
        action="store_true",
        default=True,
        help="Load ROI from --roi-file when available.",
    )
    p.add_argument("--no-load-roi", dest="load_roi", action="store_false")
    p.add_argument(
        "--save-roi",
        action="store_true",
        default=True,
        help="Save ROI to --roi-file after interactive selection.",
    )
    p.add_argument("--no-save-roi", dest="save_roi", action="store_false")
    return p.parse_args()


def _parse_roi(roi_str: str):
    try:
        x, y, w, h = [float(v.strip()) for v in roi_str.split(",")]
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


def _roi_xyxy(frame, roi):
    h, w = frame.shape[:2]
    x, y, rw, rh = roi
    x0 = max(0, min(w, int(x * w)))
    y0 = max(0, min(h, int(y * h)))
    x1 = max(0, min(w, int((x + rw) * w)))
    y1 = max(0, min(h, int((y + rh) * h)))
    return x0, y0, x1, y1


def _flip_box_x(width: int, x0: int, y0: int, x1: int, y1: int):
    fx0 = max(0, min(width, width - x1))
    fx1 = max(0, min(width, width - x0))
    return fx0, y0, fx1, y1


def _load_roi_file(path: str):
    try:
        with open(path, "r", encoding="utf-8") as f:
            data = json.load(f)
        if not isinstance(data, dict):
            return None
        vals = [data.get(k) for k in ("x", "y", "w", "h")]
        if any(v is None for v in vals):
            return None
        return _parse_roi(",".join(str(float(v)) for v in vals))
    except Exception:
        return None


def _save_roi_file(path: str, roi):
    try:
        d = os.path.dirname(path)
        if d:
            os.makedirs(d, exist_ok=True)
        x, y, w, h = roi
        with open(path, "w", encoding="utf-8") as f:
            json.dump({"x": x, "y": y, "w": w, "h": h}, f, indent=2)
    except Exception as e:
        _log(f"Warning: failed to save ROI file '{path}': {e}")


def _open_webcam(camera_index: int, camera_api: str = "auto"):
    attempts: list[tuple[str, int | None]] = [("default", None)]
    if sys.platform == "win32" and camera_api != "auto":
        forced = {
            "default": ("default", None),
            "msmf": ("msmf", cv2.CAP_MSMF),
            "dshow": ("dshow", cv2.CAP_DSHOW),
        }
        attempts = [forced.get(camera_api, ("default", None))]
    elif sys.platform == "win32":
        attempts = [("msmf", cv2.CAP_MSMF), ("dshow", cv2.CAP_DSHOW), ("default", None)]
    for label, api in attempts:
        cap = cv2.VideoCapture(camera_index) if api is None else cv2.VideoCapture(camera_index, api)
        if cap is not None and cap.isOpened():
            return cap, label
        if cap is not None:
            cap.release()
    return None, ""


class _FlirCapture:
    """Minimal OpenCV-like wrapper around PySpin camera."""

    def __init__(self, cam_index: int):
        try:
            import PySpin  # type: ignore[reportMissingImports]
        except Exception as e:
            raise RuntimeError(
                "PySpin missing. Install local Spinnaker wheel in app/.venv310."
            ) from e
        self._ps = PySpin
        self._system = PySpin.System.GetInstance()
        self._cam_list = self._system.GetCameras()
        count = self._cam_list.GetSize()
        if cam_index < 0 or cam_index >= count:
            self._cam_list.Clear()
            self._system.ReleaseInstance()
            raise RuntimeError(f"FLIR index {cam_index} not found; detected cameras: {count}")
        self._cam = self._cam_list.GetByIndex(cam_index)
        self._cam.Init()
        self._configure()
        self._cam.BeginAcquisition()
        self._opened = True

    def _configure(self):
        ps = self._ps
        nm = self._cam.GetNodeMap()
        snm = self._cam.GetTLStreamNodeMap()

        # Low latency: deliver newest frame when processing lags.
        buf_node = ps.CEnumerationPtr(snm.GetNode("StreamBufferHandlingMode"))
        if ps.IsAvailable(buf_node) and ps.IsWritable(buf_node):
            newest = buf_node.GetEntryByName("NewestOnly")
            if ps.IsAvailable(newest) and ps.IsReadable(newest):
                buf_node.SetIntValue(newest.GetValue())

        acq_node = ps.CEnumerationPtr(nm.GetNode("AcquisitionMode"))
        if ps.IsAvailable(acq_node) and ps.IsWritable(acq_node):
            cont = acq_node.GetEntryByName("Continuous")
            if ps.IsAvailable(cont) and ps.IsReadable(cont):
                acq_node.SetIntValue(cont.GetValue())

    def isOpened(self):
        return self._opened

    def set(self, prop_id, value):
        try:
            nm = self._cam.GetNodeMap()
            if prop_id == cv2.CAP_PROP_FRAME_WIDTH:
                node = self._ps.CIntegerPtr(nm.GetNode("Width"))
            elif prop_id == cv2.CAP_PROP_FRAME_HEIGHT:
                node = self._ps.CIntegerPtr(nm.GetNode("Height"))
            else:
                return False
            if not self._ps.IsAvailable(node) or not self._ps.IsWritable(node):
                return False
            v = int(max(int(node.GetMin()), min(int(node.GetMax()), int(value))))
            node.SetValue(v)
            return True
        except Exception:
            return False

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

    def release(self):
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


def _open_flir(camera_index: int):
    try:
        cap = _FlirCapture(camera_index)
        return cap, "pyspin"
    except Exception:
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


def _configure_webcam_low_latency(cap, args):
    _set_camera_prop(cap, cv2.CAP_PROP_BUFFERSIZE, 1)
    if args.width > 0:
        _set_camera_prop(cap, cv2.CAP_PROP_FRAME_WIDTH, float(args.width))
    if args.height > 0:
        _set_camera_prop(cap, cv2.CAP_PROP_FRAME_HEIGHT, float(args.height))
    if float(args.camera_fps) > 0:
        _set_camera_prop(cap, cv2.CAP_PROP_FPS, float(args.camera_fps))

    if args.camera_auto_exposure == "manual":
        for v in (0.25, 1.0, 0.0):
            if _set_camera_prop(cap, cv2.CAP_PROP_AUTO_EXPOSURE, v):
                break
    elif args.camera_auto_exposure == "auto":
        for v in (0.75, 3.0, 1.0):
            if _set_camera_prop(cap, cv2.CAP_PROP_AUTO_EXPOSURE, v):
                break
    if float(args.camera_exposure) != 0:
        _set_camera_prop(cap, cv2.CAP_PROP_EXPOSURE, float(args.camera_exposure))

    if args.camera_auto_wb == "off":
        _set_camera_prop(cap, cv2.CAP_PROP_AUTO_WB, 0.0)
    elif args.camera_auto_wb == "on":
        _set_camera_prop(cap, cv2.CAP_PROP_AUTO_WB, 1.0)

    _ = cap.read()
    _log(
        "Camera actual:"
        f" w={_get_camera_prop(cap, cv2.CAP_PROP_FRAME_WIDTH):.0f}"
        f" h={_get_camera_prop(cap, cv2.CAP_PROP_FRAME_HEIGHT):.0f}"
        f" fps={_get_camera_prop(cap, cv2.CAP_PROP_FPS):.2f}"
        f" ae={_get_camera_prop(cap, cv2.CAP_PROP_AUTO_EXPOSURE):.3f}"
        f" exp={_get_camera_prop(cap, cv2.CAP_PROP_EXPOSURE):.3f}"
        f" awb={_get_camera_prop(cap, cv2.CAP_PROP_AUTO_WB):.3f}"
    )


def _open_capture(args):
    if args.camera_mode == "flir":
        candidates = [args.camera]
        if args.camera == 0:
            candidates = [0, 1]
        for idx in candidates:
            cap, api = _open_flir(idx)
            if cap is not None and cap.isOpened():
                return cap, idx, api
        return None, None, ""

    cap, api = _open_webcam(args.camera, args.camera_api)
    if cap is None:
        return None, None, ""
    return cap, args.camera, api


def _filter_to_numbers_clock(text: str) -> str:
    if not text:
        return ""
    return "".join(ch for ch in text if ch.isdigit() or ch in ":,.")


def _normalize_ios_stopwatch(text: str) -> str:
    """Normalize OCR output to iOS stopwatch style: mm:ss,ms."""
    if not text:
        return ""
    t = text.strip().replace(".", ",")
    m = re.search(r"(\d{1,2}:\d{2}),(\d{1,3})$", t)
    if m:
        return f"{m.group(1)},{m.group(2).ljust(3, '0')[:3]}"
    return t


def _expand_roi(roi, padding: float):
    x, y, w, h = roi
    p = max(0.0, float(padding))
    x2 = x - p * w
    y2 = y - p * h
    w2 = w * (1.0 + 2.0 * p)
    h2 = h * (1.0 + 2.0 * p)
    return _parse_roi(f"{x2},{y2},{w2},{h2}")


def _extract_ios_clock_from_raw(raw: str) -> str:
    """Recover mm:ss,ms from noisy OCR raw strings."""
    if not raw:
        return ""
    s = raw.upper()
    for a, b in {
        "O": "0",
        "D": "0",
        "Q": "0",
        "I": "1",
        "L": "1",
        "|": "1",
        "Z": "2",
        "S": "5",
        "B": "8",
        ".": ",",
        ";": ":",
    }.items():
        s = s.replace(a, b)
    s = "".join(ch for ch in s if ch.isdigit() or ch in ":,")
    s = re.sub(r":{2,}", ":", s)
    s = re.sub(r",{2,}", ",", s)

    m = re.search(r"(\d{1,2}):(\d{2}),(\d{1,3})", s)
    if m:
        mm = m.group(1).zfill(2)
        ss = m.group(2)
        ms = m.group(3).ljust(3, "0")[:3]
        return f"{mm}:{ss},{ms}"

    # Missing comma case: mm:ssmmm
    m = re.search(r"(\d{1,2}):(\d{2})(\d{1,3})", s)
    if m:
        mm = m.group(1).zfill(2)
        ss = m.group(2)
        ms = m.group(3).ljust(3, "0")[:3]
        return f"{mm}:{ss},{ms}"

    # Missing colon case: mmss,mmm or mmssmmm
    m = re.search(r"(\d{2})(\d{2}),(\d{1,3})", s)
    if m:
        return f"{m.group(1)}:{m.group(2)},{m.group(3).ljust(3, '0')[:3]}"
    m = re.search(r"(\d{2})(\d{2})(\d{1,3})", s)
    if m:
        return f"{m.group(1)}:{m.group(2)},{m.group(3).ljust(3, '0')[:3]}"
    return ""


def main():
    args = parse_args()
    if args.camera_mode == "flir":
        # FLIR view should reflect the true sensor orientation for measurement work.
        args.flip_preview = False
    roi = _parse_roi(args.ocr_roi)
    if roi is None:
        _log("Invalid --ocr-roi. Expected x,y,w,h in [0,1].")
        return
    if args.load_roi:
        roi_loaded = _load_roi_file(args.roi_file)
        if roi_loaded is not None:
            roi = roi_loaded
            _log(
                f"Loaded ROI from file: {args.roi_file} -> "
                f"{roi[0]:.4f},{roi[1]:.4f},{roi[2]:.4f},{roi[3]:.4f}"
            )

    if args.device == "cuda":
        _ensure_cuda_in_path()
    use_cuda = args.device == "cuda" and onnx_cuda_available()
    if args.device == "cuda" and not use_cuda:
        _log("Warning: CUDA requested but CUDAExecutionProvider unavailable; using CPU.")

    try:
        t0 = time.perf_counter()
        engine = create_rapid_ocr_engine(
            use_cuda=use_cuda,
            use_text_det=bool(args.rapid_use_text_det),
            use_angle_cls=bool(args.rapid_use_angle_cls),
        )
        _log(
            f"OCR engine ready in {(time.perf_counter() - t0):.2f}s "
            f"| mode={'CUDA' if use_cuda else 'CPU'}"
            f" | text_det={args.rapid_use_text_det}"
            f" | angle_cls={args.rapid_use_angle_cls}"
        )
    except Exception as e:
        _log(f"RapidOCR init failed: {e}")
        return

    cap, cam_idx, cam_api = _open_capture(args)
    if cap is None:
        if args.camera_mode == "flir":
            _log(
                "Could not open FLIR camera. Ensure camera is visible in SpinView "
                "and PySpin is installed in app/.venv310."
            )
        else:
            _log(f"Could not open webcam index {args.camera}.")
        return

    # Match pose defaults and apply low-latency camera controls.
    if args.camera_mode == "webcam":
        _configure_webcam_low_latency(cap, args)
    else:
        if args.width > 0:
            cap.set(cv2.CAP_PROP_FRAME_WIDTH, args.width)
        if args.height > 0:
            cap.set(cv2.CAP_PROP_FRAME_HEIGHT, args.height)
        _ = cap.read()

    _log(
        f"Capture: mode={args.camera_mode} index={cam_idx} api={cam_api} "
        f"| size={args.width}x{args.height} | ocr_max_fps={args.ocr_max_fps}"
    )
    if args.show_window:
        _log("Press Q in preview (or Ctrl+C) to stop.")
    else:
        _log("Press Ctrl+C to stop.")

    state_lock = threading.Lock()
    stop = threading.Event()
    shared = {
        "frame": None,
        "frame_shape": (0, 0),
        "capture_t": 0.0,
        "ocr_text": "",
        "ocr_ms": 0.0,
        "raw_text": "",
        "last_ocr_t": 0.0,
        "roi": roi,
    }

    min_dt = 1.0 / max(0.1, float(args.ocr_max_fps))
    stable_required = max(1, int(args.require_stable))

    def capture_worker():
        while not stop.is_set():
            ok, frame = cap.read()
            if not ok:
                time.sleep(0.002)
                continue
            with state_lock:
                shared["frame"] = frame
                shared["frame_shape"] = frame.shape[:2]
                shared["capture_t"] = time.perf_counter()

    def ocr_worker():
        candidate_text = ""
        candidate_hits = 0
        stable_text = ""
        while not stop.is_set():
            with state_lock:
                frame = shared["frame"]
                roi_now = shared["roi"]
            if frame is None:
                time.sleep(0.002)
                continue
            now = time.perf_counter()
            with state_lock:
                last_ocr_t = shared["last_ocr_t"]
            if now - last_ocr_t < min_dt:
                time.sleep(0.001)
                continue

            x0, y0, x1, y1 = _roi_xyxy(frame, roi_now)
            if x1 <= x0 or y1 <= y0:
                time.sleep(0.005)
                continue
            crop = frame[y0:y1, x0:x1]
            if crop.size == 0:
                time.sleep(0.005)
                continue

            scale = max(0.2, float(args.ocr_scale))
            if scale != 1.0:
                crop = cv2.resize(crop, None, fx=scale, fy=scale, interpolation=cv2.INTER_AREA)

            text, dbg = run_rapid_ocr_on_crop(engine, crop, args.ocr_whitelist)
            raw = (dbg.get("raw", "") or text or "").strip()
            parsed = _extract_ios_clock_from_raw(raw)
            if not parsed:
                parsed = extract_target_text(raw, args.target_pattern) if args.target_pattern else raw
            parsed = _filter_to_numbers_clock(parsed)
            if not parsed:
                parsed = _filter_to_numbers_clock((dbg.get("clean", "") or "").strip())

            if parsed:
                if parsed == candidate_text:
                    candidate_hits += 1
                else:
                    candidate_text = parsed
                    candidate_hits = 1
                if candidate_hits >= stable_required:
                    stable_text = _normalize_ios_stopwatch(parsed)

            with state_lock:
                shared["ocr_text"] = stable_text
                shared["raw_text"] = raw
                shared["ocr_ms"] = float(dbg.get("ocr_ms", 0.0))
                shared["last_ocr_t"] = time.perf_counter()

            if args.debug:
                _log(
                    f"DEBUG raw='{raw}' clean='{dbg.get('clean','')}' parsed='{parsed}' "
                    f"stable='{stable_text}' ocr_ms={shared['ocr_ms']:.1f}"
                )

    t_cap = threading.Thread(target=capture_worker, daemon=True)
    t_ocr = threading.Thread(target=ocr_worker, daemon=True)
    t_cap.start()
    t_ocr.start()

    last_print = 0.0
    fps_alpha = 0.2
    fps_smooth = 30.0
    ocr_ms_smooth = 8.0
    prev_cap_t = None
    window_name = "OCR Clock Live - Q to quit"
    roi_ui = {
        "dragging": False,
        "x0": 0,
        "y0": 0,
        "x1": 0,
        "y1": 0,
    }
    if args.show_window:
        cv2.namedWindow(window_name, cv2.WINDOW_NORMAL)
        _log("ROI selection: click+drag in preview to set ROI. Press C to clear ROI to full frame.")

        def on_mouse(event, x, y, flags, param):
            if event == cv2.EVENT_LBUTTONDOWN:
                roi_ui["dragging"] = True
                roi_ui["x0"] = x
                roi_ui["y0"] = y
                roi_ui["x1"] = x
                roi_ui["y1"] = y
            elif event == cv2.EVENT_MOUSEMOVE and roi_ui["dragging"]:
                roi_ui["x1"] = x
                roi_ui["y1"] = y
            elif event == cv2.EVENT_LBUTTONUP and roi_ui["dragging"]:
                roi_ui["dragging"] = False
                roi_ui["x1"] = x
                roi_ui["y1"] = y
                with state_lock:
                    h, w = shared["frame_shape"]
                if w <= 0 or h <= 0:
                    return
                dx0, dx1 = sorted((roi_ui["x0"], roi_ui["x1"]))
                dy0, dy1 = sorted((roi_ui["y0"], roi_ui["y1"]))
                if abs(dx1 - dx0) < 5 or abs(dy1 - dy0) < 5:
                    return
                if args.flip_preview:
                    fx0 = w - dx1
                    fx1 = w - dx0
                    fy0, fy1 = dy0, dy1
                else:
                    fx0, fx1 = dx0, dx1
                    fy0, fy1 = dy0, dy1
                fx0 = max(0, min(w, fx0))
                fx1 = max(0, min(w, fx1))
                fy0 = max(0, min(h, fy0))
                fy1 = max(0, min(h, fy1))
                rw = max(1, fx1 - fx0)
                rh = max(1, fy1 - fy0)
                roi_new = (fx0 / w, fy0 / h, rw / w, rh / h)
                roi_expanded = _expand_roi(roi_new, args.roi_padding)
                if roi_expanded is not None:
                    roi_new = roi_expanded
                with state_lock:
                    shared["roi"] = roi_new
                if args.save_roi:
                    _save_roi_file(args.roi_file, roi_new)
                _log(
                    f"ROI set to {roi_new[0]:.4f},{roi_new[1]:.4f},{roi_new[2]:.4f},{roi_new[3]:.4f}"
                )

        cv2.setMouseCallback(window_name, on_mouse)
    try:
        while True:
            now = time.perf_counter()
            with state_lock:
                frame = shared["frame"]
                txt = shared["ocr_text"] or "--"
                raw = shared["raw_text"] or ""
                ocr_ms = float(shared["ocr_ms"])
                cap_t = float(shared["capture_t"])
                roi_now = shared["roi"]
            frame_age_ms = (now - cap_t) * 1000.0 if cap_t > 0 else 0.0

            if now - last_print >= max(0.05, float(args.print_interval)):
                ts = time.strftime("%H:%M:%S")
                if raw and txt != "--":
                    _log(
                        f"[{ts}] OCR={txt} | raw='{raw}' | ocr_ms={ocr_ms:.1f} | frame_age_ms={frame_age_ms:.1f}"
                    )
                else:
                    _log(f"[{ts}] OCR={txt} | ocr_ms={ocr_ms:.1f} | frame_age_ms={frame_age_ms:.1f}")
                last_print = now

            if args.show_window and frame is not None:
                vis = frame.copy()
                h, w = vis.shape[:2]
                x0, y0, x1, y1 = _roi_xyxy(vis, roi_now)
                if args.flip_preview:
                    vis = cv2.flip(vis, 1)
                    x0, y0, x1, y1 = _flip_box_x(w, x0, y0, x1, y1)

                cv2.rectangle(vis, (x0, y0), (x1, y1), (0, 255, 255), 2)
                cv2.putText(
                    vis,
                    "OCR ROI",
                    (x0 + 6, max(20, y0 - 8)),
                    cv2.FONT_HERSHEY_SIMPLEX,
                    0.6,
                    (0, 255, 255),
                    2,
                )

                cv2.rectangle(vis, (0, h - 46), (w, h), (0, 0, 0), -1)
                cv2.putText(
                    vis,
                    f"OCR: {txt}",
                    (12, h - 14),
                    cv2.FONT_HERSHEY_SIMPLEX,
                    0.8,
                    (0, 255, 0),
                    2,
                )

                if args.show_stats:
                    if prev_cap_t is not None and cap_t > prev_cap_t:
                        dt = cap_t - prev_cap_t
                        if dt > 1e-6:
                            fps_smooth = fps_alpha * (1.0 / dt) + (1.0 - fps_alpha) * fps_smooth
                    if prev_cap_t is None or cap_t > prev_cap_t:
                        prev_cap_t = cap_t
                    ocr_ms_smooth = fps_alpha * ocr_ms + (1.0 - fps_alpha) * ocr_ms_smooth
                    cv2.rectangle(vis, (0, 0), (430, 56), (0, 0, 0), -1)
                    cv2.putText(
                        vis,
                        f"FPS: {fps_smooth:.1f} | OCR: {ocr_ms_smooth:.1f} ms",
                        (10, 22),
                        cv2.FONT_HERSHEY_SIMPLEX,
                        0.6,
                        (0, 255, 0),
                        2,
                    )
                    cv2.putText(
                        vis,
                        f"{w}x{h} | {'CUDA' if use_cuda else 'CPU'} | age {frame_age_ms:.1f} ms",
                        (10, 46),
                        cv2.FONT_HERSHEY_SIMPLEX,
                        0.55,
                        (0, 255, 0),
                        2,
                    )
                # Draw interactive ROI drag rectangle.
                if roi_ui["dragging"]:
                    dx0, dx1 = sorted((roi_ui["x0"], roi_ui["x1"]))
                    dy0, dy1 = sorted((roi_ui["y0"], roi_ui["y1"]))
                    cv2.rectangle(vis, (dx0, dy0), (dx1, dy1), (255, 200, 0), 1)
                cv2.imshow(window_name, vis)

                key = cv2.waitKey(1) & 0xFF
                if key == ord("q"):
                    break
                if key == ord("c"):
                    with state_lock:
                        shared["roi"] = (0.0, 0.0, 1.0, 1.0)
                    if args.save_roi:
                        _save_roi_file(args.roi_file, (0.0, 0.0, 1.0, 1.0))
                    _log("ROI reset to full frame.")
            time.sleep(0.01)
    except KeyboardInterrupt:
        pass
    finally:
        stop.set()
        time.sleep(0.05)
        cap.release()
        if args.show_window:
            try:
                cv2.destroyAllWindows()
            except Exception:
                pass


if __name__ == "__main__":
    main()
