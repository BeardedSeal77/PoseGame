"""Video capture sources for webcam, simulator, and other inputs."""

from .webcam_threaded import WebcamSource
from .webots import WebotsSource

__all__ = [
    'WebcamSource',
    'WebotsSource',
]
