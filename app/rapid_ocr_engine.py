"""
RapidOCR with ONNX Runtime — same inference stack family as rtmlib (Body/PoseTracker).

Install:
  pip install rapidocr-onnxruntime
For GPU with pose: use onnxruntime-gpu (not onnxruntime) so CUDA EP is available.
Windows: win_cuda_path._ensure_cuda_in_path() before import onnxruntime if needed.
"""

from __future__ import annotations

import re
import time

_PATCHED = False


def _patch_rapidocr_param_keys() -> None:
    """rapidocr_onnxruntime only strips det_* for Det; fix cls_/rec_ so cls_use_cuda / rec_use_cuda work."""
    global _PATCHED
    if _PATCHED:
        return
    import rapidocr_onnxruntime.utils as ru

    def update_cls_params(self, config, cls_dict):
        if not cls_dict:
            return config
        new_cls_dict = {}
        for k, v in cls_dict.items():
            if k == "cls_label_list":
                new_cls_dict["label_list"] = v
            elif k == "cls_model_path":
                new_cls_dict["model_path"] = v
            elif k.startswith("cls_"):
                new_cls_dict[k[len("cls_") :]] = v
            else:
                new_cls_dict[k] = v
        if not new_cls_dict.get("model_path"):
            new_cls_dict["model_path"] = config["model_path"]
        config.update(new_cls_dict)
        return config

    def update_rec_params(self, config, rec_dict):
        if not rec_dict:
            return config
        new_rec_dict = {}
        for k, v in rec_dict.items():
            if k == "rec_model_path":
                new_rec_dict["model_path"] = v
            elif k.startswith("rec_"):
                new_rec_dict[k[len("rec_") :]] = v
            else:
                new_rec_dict[k] = v
        if not new_rec_dict.get("model_path"):
            new_rec_dict["model_path"] = config["model_path"]
        config.update(new_rec_dict)
        return config

    ru.UpdateParameters.update_cls_params = update_cls_params  # type: ignore[method-assign]
    ru.UpdateParameters.update_rec_params = update_rec_params  # type: ignore[method-assign]
    _PATCHED = True


def onnx_cuda_available() -> bool:
    try:
        import onnxruntime as ort

        return "CUDAExecutionProvider" in ort.get_available_providers()
    except Exception:
        return False


def create_rapid_ocr_engine(
    use_cuda: bool = False,
    use_text_det: bool = False,
    use_angle_cls: bool = True,
):
    """
    use_text_det=False: treat ROI as a single text line (good for tight clock crops).
    use_cuda=True: requires onnxruntime-gpu and CUDA EP available.
    """
    _patch_rapidocr_param_keys()
    from rapidocr_onnxruntime import RapidOCR

    return RapidOCR(
        use_text_det=use_text_det,
        use_angle_cls=use_angle_cls,
        det_use_cuda=use_cuda,
        cls_use_cuda=use_cuda,
        rec_use_cuda=use_cuda,
    )


def run_rapid_ocr_on_crop(engine, crop_bgr, whitelist: str) -> tuple[str, dict]:
    """Run OCR on BGR crop; return (cleaned_text, debug_dict)."""
    t0 = time.perf_counter()
    if crop_bgr is None or crop_bgr.size == 0:
        return "", {"ocr_ms": 0.0, "raw": "", "clean": "", "error": "empty_crop"}
    h, w = crop_bgr.shape[:2]
    try:
        result, elapse = engine(crop_bgr)
    except Exception as e:
        return "", {
            "ocr_ms": (time.perf_counter() - t0) * 1000.0,
            "raw": "",
            "clean": "",
            "error": str(e),
            "crop_shape": (h, w),
        }
    ocr_ms = (time.perf_counter() - t0) * 1000.0
    dbg_base = {
        "ocr_ms": ocr_ms,
        "crop_shape": (h, w),
        "elapse": elapse,
        "best_mode": "rapidocr/onnx",
        "attempts": 1,
    }
    if result is None:
        return "", {**dbg_base, "raw": "", "clean": ""}

    texts = []
    for row in result:
        if row and len(row) >= 2:
            texts.append(str(row[1]))
    raw = "".join(texts)
    raw_spaced = " ".join(texts)
    if whitelist:
        clean = "".join(ch for ch in raw.replace(" ", "") if ch in whitelist)
        if not clean:
            clean = "".join(ch for ch in raw_spaced if ch in whitelist)
    else:
        clean = re.sub(r"\s+", "", raw_spaced)
    dbg = {**dbg_base, "raw": raw_spaced.strip(), "clean": clean, "lines": len(texts)}
    return clean, dbg


def extract_target_text(text: str, pattern: str) -> str:
    if not text:
        return ""
    try:
        m = re.search(pattern, text)
        if not m:
            return ""
        out = m.group(1) if m.groups() else m.group(0)
        return out.strip()
    except re.error:
        return ""
