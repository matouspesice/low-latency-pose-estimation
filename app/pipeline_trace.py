"""Append-only text log for end-to-end pipeline latency (Python + Unity)."""

from __future__ import annotations

import datetime
import os
import threading


class PipelineTrace:
    """Thread-safe timestamped trace file (one line per event)."""

    def __init__(self, path: str | None):
        self._path = (path or "").strip()
        self._lock = threading.Lock()
        self._file = None
        if not self._path:
            return
        parent = os.path.dirname(os.path.abspath(self._path))
        if parent:
            os.makedirs(parent, exist_ok=True)
        self._file = open(self._path, "a", encoding="utf-8", buffering=1)

    @property
    def enabled(self) -> bool:
        return self._file is not None

    def log_session_start(self, **metadata: object) -> None:
        """Log run boundary with command line and settings (call once per pose.py launch)."""
        self.log("session_start", seq=-1, source="python", **metadata)

    def log(self, stage: str, seq: int = -1, **extra: object) -> None:
        if self._file is None:
            return
        ts = datetime.datetime.now().strftime("%Y-%m-%d %H:%M:%S.%f")[:-3]
        parts = [ts, "python", f"stage={stage}"]
        if seq >= 0:
            parts.append(f"seq={seq}")
        for key, value in extra.items():
            if value is None:
                continue
            parts.append(f"{key}={value}")
        line = " | ".join(parts) + "\n"
        with self._lock:
            self._file.write(line)
            self._file.flush()

    def close(self) -> None:
        if self._file is None:
            return
        self.log("session_end", seq=-1, extra="source=python")
        with self._lock:
            try:
                self._file.close()
            except OSError:
                pass
        self._file = None
