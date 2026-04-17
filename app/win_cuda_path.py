"""Windows: put CUDA 12 + cuDNN 9 on PATH before loading onnxruntime-gpu or CUDA PyTorch."""

import os
import sys


def _ensure_cuda_in_path():
    if sys.platform != "win32":
        return
    candidates = []
    cuda_path = os.environ.get("CUDA_PATH")
    if cuda_path and os.path.isdir(cuda_path):
        candidates.append(os.path.join(cuda_path, "bin"))
    for prog in (os.environ.get("ProgramFiles", "C:\\Program Files"), "C:\\Program Files (x86)"):
        cuda_base = os.path.join(prog, "NVIDIA GPU Computing Toolkit", "CUDA")
        if os.path.isdir(cuda_base):
            for name in sorted(os.listdir(cuda_base), reverse=True):
                if name.startswith("v12"):
                    candidates.append(os.path.join(cuda_base, name, "bin"))
            break
    for bin_dir in candidates:
        dll12 = os.path.join(bin_dir, "cublasLt64_12.dll")
        if not os.path.isdir(bin_dir) or not os.path.isfile(dll12):
            continue
        path = os.environ.get("PATH", "")
        if bin_dir not in path:
            os.environ["PATH"] = bin_dir + os.pathsep + path
        if hasattr(os, "add_dll_directory"):
            try:
                os.add_dll_directory(bin_dir)
            except OSError:
                pass
        print("Using CUDA from:", bin_dir)
        cudnn_candidates = []
        cudnn_path = os.environ.get("CUDNN_PATH")
        if cudnn_path and os.path.isdir(cudnn_path):
            cudnn_candidates.append(os.path.join(cudnn_path, "bin"))
            cudnn_candidates.append(cudnn_path)
        for prog in (os.environ.get("ProgramFiles", "C:\\Program Files"),):
            cudnn_base = os.path.join(prog, "NVIDIA", "CUDNN")
            if os.path.isdir(cudnn_base):
                for name in sorted(os.listdir(cudnn_base), reverse=True):
                    if name.startswith("v9"):
                        v9_dir = os.path.join(cudnn_base, name)
                        cudnn_candidates.append(os.path.join(v9_dir, "bin"))
                        bin_dir_cuda = os.path.join(v9_dir, "bin")
                        if os.path.isdir(bin_dir_cuda):
                            for sub in os.listdir(bin_dir_cuda):
                                x64 = os.path.join(bin_dir_cuda, sub, "x64")
                                if sub.startswith("12") and os.path.isdir(x64):
                                    cudnn_candidates.append(x64)
                        break
                break
        cudnn_found = False
        for cudnn_bin in cudnn_candidates:
            if not os.path.isdir(cudnn_bin) or not os.path.isfile(os.path.join(cudnn_bin, "cudnn64_9.dll")):
                continue
            path = os.environ.get("PATH", "")
            if cudnn_bin not in path:
                os.environ["PATH"] = cudnn_bin + os.pathsep + path
            if hasattr(os, "add_dll_directory"):
                try:
                    os.add_dll_directory(cudnn_bin)
                except OSError:
                    pass
            print("Using cuDNN 9 from:", cudnn_bin)
            cudnn_found = True
            break
        if not cudnn_found:
            print(
                "Note: cudnn64_9.dll not found. Install cuDNN 9 for CUDA 12 from https://developer.nvidia.com/cudnn "
                "and copy bin/*.dll into",
                bin_dir,
                "or add cuDNN bin to PATH.",
            )
        return
    for prog in (os.environ.get("ProgramFiles", "C:\\Program Files"),):
        cuda_base = os.path.join(prog, "NVIDIA GPU Computing Toolkit", "CUDA")
        if os.path.isdir(cuda_base):
            for name in os.listdir(cuda_base):
                if name.startswith("v13"):
                    b = os.path.join(cuda_base, name, "bin")
                    if os.path.isfile(os.path.join(b, "cublasLt64_13.dll")):
                        print(
                            "Note: You have CUDA 13. onnxruntime-gpu and some PyTorch builds expect CUDA 12 "
                            "(cublasLt64_12.dll). Install CUDA 12.x if GPU init fails."
                        )
                    break
            break
