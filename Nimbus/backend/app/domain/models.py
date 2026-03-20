"""Core domain models used by services and pipelines."""

from __future__ import annotations

from typing import List, Optional
from pydantic import BaseModel, Field


class Detection(BaseModel):
    label: str
    confidence: float = Field(ge=0, le=1)
    x: float
    y: float
    width: float
    height: float
    is_target: bool = False  # Whether this detection matches the target object


class DetectionResult(BaseModel):
    frame_id: int
    timestamp: float
    detections: List[Detection] = Field(default_factory=list)

    @property
    def has_detections(self) -> bool:
        return len(self.detections) > 0


class DepthResult(BaseModel):
    frame_id: int
    timestamp: float
    distance_meters: Optional[float] = None
    method: str = "UNKNOWN"


class TranscriptResult(BaseModel):
    text: str
    confidence: float = Field(ge=0, le=1)


class IntentResult(BaseModel):
    intent: str
    confidence: float = Field(ge=0, le=1)
