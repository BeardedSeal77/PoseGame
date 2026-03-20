"""
Video writer using ffmpeg subprocess for proper H.264 encoding.
Falls back to OpenCV VideoWriter if ffmpeg is not available.
"""

import subprocess
import shutil
import logging
import numpy as np
from typing import Optional

logger = logging.getLogger(__name__)


class FFmpegWriter:
    """Writes raw BGR frames to an H.264 MP4 file via ffmpeg pipe."""

    def __init__(self, output_path: str, width: int, height: int, fps: float = 30.0):
        self.output_path = output_path
        self.width = width
        self.height = height
        self.fps = fps
        self.process: Optional[subprocess.Popen] = None
        self.frame_count = 0

    def start(self) -> bool:
        ffmpeg_path = shutil.which("ffmpeg")
        if not ffmpeg_path:
            logger.error("[VIDEO] ffmpeg not found in PATH")
            return False

        cmd = [
            ffmpeg_path,
            "-y",
            "-f", "rawvideo",
            "-vcodec", "rawvideo",
            "-pix_fmt", "bgr24",
            "-s", f"{self.width}x{self.height}",
            "-r", str(self.fps),
            "-i", "-",
            "-c:v", "libx264",
            "-preset", "medium",
            "-crf", "23",
            "-pix_fmt", "yuv420p",
            "-movflags", "+faststart",
            self.output_path,
        ]

        self.process = subprocess.Popen(
            cmd,
            stdin=subprocess.PIPE,
            stdout=subprocess.DEVNULL,
            stderr=subprocess.PIPE,
        )
        logger.info(f"[VIDEO] ffmpeg writer started -> {self.output_path}")
        return True

    def write_frame(self, frame: np.ndarray):
        if self.process and self.process.stdin:
            try:
                self.process.stdin.write(frame.tobytes())
                self.frame_count += 1
            except BrokenPipeError:
                logger.error("[VIDEO] ffmpeg pipe broken")

    def stop(self):
        if self.process:
            if self.process.stdin:
                self.process.stdin.close()
            self.process.wait(timeout=10)
            stderr = self.process.stderr.read().decode() if self.process.stderr else ""
            if self.process.returncode != 0:
                logger.error(f"[VIDEO] ffmpeg exited with code {self.process.returncode}: {stderr}")
            else:
                logger.info(f"[VIDEO] Saved {self.frame_count} frames to {self.output_path}")
            self.process = None
