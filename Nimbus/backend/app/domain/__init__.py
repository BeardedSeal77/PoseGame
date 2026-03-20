"""Domain models and events."""

from .models import (
    Detection,
    DetectionResult,
    DepthResult,
    TranscriptResult,
    IntentResult,
)
from .events import (
    VIDEO_RAW_TOPIC,
    VIDEO_PROCESSED_TOPIC,
    OBJECT_DETECTIONS_TOPIC,
    DEPTH_RESULT_TOPIC,
    TRANSCRIPT_TOPIC,
    INTENT_TOPIC,
)

__all__ = [
    # Models
    "Detection",
    "DetectionResult",
    "DepthResult",
    "TranscriptResult",
    "IntentResult",
    # Events
    "VIDEO_RAW_TOPIC",
    "VIDEO_PROCESSED_TOPIC",
    "OBJECT_DETECTIONS_TOPIC",
    "DEPTH_RESULT_TOPIC",
    "TRANSCRIPT_TOPIC",
    "INTENT_TOPIC",
]
