"""Shared hub state and configuration."""

from __future__ import annotations

from collections import deque
from dataclasses import dataclass, field
from typing import Literal


@dataclass
class HubConfig:
    object_every_n_frames: int = 3
    depth_every_m_detections: int = 2
    frame_queue_size: int = 30


@dataclass
class HubState:
    frame_count: int = 0
    detection_count: int = 0
    successful_detections: int = 0
    last_frame_timestamp: float = 0.0
    current_source: Literal["webcam", "webots", "none"] = "none"  # Standardized name used by routes
    target_object: str = "person"  # Current target object for detection
    fps: float = 0.0  # Current frames per second
    processing_enabled: bool = True  # Whether detection processing is active
    recent_detections: deque = field(default_factory=lambda: deque(maxlen=10))  # Last 10 detection results (True/False)

