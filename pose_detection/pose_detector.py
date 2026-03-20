"""
YOLO v26 Pose Detection.
Runs pose estimation on raw frames and returns annotated frames + keypoint data.
Reference: https://docs.ultralytics.com/models/yolo26/
"""

import numpy as np
import logging
from ultralytics import YOLO

logger = logging.getLogger(__name__)

# COCO pose keypoint names (17 keypoints)
KEYPOINT_NAMES = [
    "nose", "left_eye", "right_eye", "left_ear", "right_ear",
    "left_shoulder", "right_shoulder", "left_elbow", "right_elbow",
    "left_wrist", "right_wrist", "left_hip", "right_hip",
    "left_knee", "right_knee", "left_ankle", "right_ankle",
]

# Skeleton connections for drawing
SKELETON = [
    (5, 6),    # shoulders
    (5, 7),    # left shoulder -> left elbow
    (7, 9),    # left elbow -> left wrist
    (6, 8),    # right shoulder -> right elbow
    (8, 10),   # right elbow -> right wrist
    (5, 11),   # left shoulder -> left hip
    (6, 12),   # right shoulder -> right hip
    (11, 12),  # hips
    (11, 13),  # left hip -> left knee
    (13, 15),  # left knee -> left ankle
    (12, 14),  # right hip -> right knee
    (14, 16),  # right knee -> right ankle
]


class PoseDetector:
    """YOLO v26 pose detector. Loads model once, runs inference per frame."""

    def __init__(self, model_name: str = "yolo11n-pose.pt", confidence: float = 0.5):
        """
        Args:
            model_name: YOLO pose model to load. Downloads automatically on first run.
                        Options: yolo11n-pose.pt, yolo11s-pose.pt, yolo11m-pose.pt, etc.
            confidence: Minimum detection confidence threshold.
        """
        logger.info(f"[POSE] Loading model: {model_name}")
        self.model = YOLO(model_name)
        self.confidence = confidence
        logger.info("[POSE] Model loaded")

    def detect(self, frame: np.ndarray) -> dict:
        """
        Run pose estimation on a single BGR frame.

        Returns:
            dict with keys:
                - annotated_frame: BGR frame with pose skeleton drawn
                - keypoints: list of dicts per person, each with 17 named keypoints
                              [{name: (x, y, confidence)}, ...]
                - num_people: number of people detected
        """
        results = self.model(frame, conf=self.confidence, verbose=False)
        result = results[0]

        annotated_frame = result.plot()

        people = []
        if result.keypoints is not None and result.keypoints.data.shape[0] > 0:
            for person_kps in result.keypoints.data:
                person = {}
                for i, name in enumerate(KEYPOINT_NAMES):
                    x, y, conf = person_kps[i].tolist()
                    person[name] = (float(x), float(y), float(conf))
                people.append(person)

        return {
            "annotated_frame": annotated_frame,
            "keypoints": people,
            "num_people": len(people),
        }
