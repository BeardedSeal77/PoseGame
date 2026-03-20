"""
Webcam capture - stripped from Nimbus project.
Threaded capture delivering raw OpenCV frames (not JPEG).
"""

import cv2
import threading
import time
from typing import Optional
import logging
import numpy as np

logger = logging.getLogger(__name__)


class WebcamSource:
    """Threaded webcam capture that delivers raw BGR frames."""

    def __init__(self, camera_index: int = 0, width: int = 640, height: int = 480):
        self.camera_index = camera_index
        self.width = width
        self.height = height
        self.cap: Optional[cv2.VideoCapture] = None
        self.is_running = False
        self._lock = threading.Lock()
        self._latest_frame: Optional[np.ndarray] = None
        self._capture_thread: Optional[threading.Thread] = None

    def start(self) -> bool:
        if self.is_running:
            return True

        logger.info(f"[WEBCAM] Connecting to camera {self.camera_index}...")
        self.cap = cv2.VideoCapture(self.camera_index, cv2.CAP_DSHOW)

        if not self.cap.isOpened():
            logger.error("[WEBCAM] Failed to open camera")
            return False

        self.cap.set(cv2.CAP_PROP_FRAME_WIDTH, self.width)
        self.cap.set(cv2.CAP_PROP_FRAME_HEIGHT, self.height)
        self.cap.set(cv2.CAP_PROP_FPS, 30)
        self.cap.set(cv2.CAP_PROP_BUFFERSIZE, 1)

        ret, frame = self.cap.read()
        if not ret:
            logger.error("[WEBCAM] Failed to read test frame")
            self.cap.release()
            return False

        logger.info(f"[WEBCAM] Connected: {frame.shape}")

        self.is_running = True
        self._capture_thread = threading.Thread(target=self._capture_loop, daemon=True)
        self._capture_thread.start()
        return True

    def stop(self):
        if not self.is_running:
            return
        self.is_running = False
        if self._capture_thread and self._capture_thread.is_alive():
            self._capture_thread.join(timeout=2)
        if self.cap:
            self.cap.release()
            self.cap = None
        logger.info("[WEBCAM] Stopped")

    def _capture_loop(self):
        while self.is_running and self.cap:
            ret, frame = self.cap.read()
            if ret:
                with self._lock:
                    self._latest_frame = frame
            else:
                time.sleep(0.01)
            time.sleep(0.001)

    def read_frame(self) -> Optional[np.ndarray]:
        """Get latest raw BGR frame (non-blocking)."""
        with self._lock:
            return self._latest_frame.copy() if self._latest_frame is not None else None

    @property
    def fps(self) -> float:
        if self.cap:
            return self.cap.get(cv2.CAP_PROP_FPS)
        return 30.0
