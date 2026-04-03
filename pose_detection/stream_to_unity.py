"""
Live pose bridge from a webcam-style camera to Unity.

This script owns the camera device, runs YOLO pose detection, and sends the
normalized 17-joint pose to Unity over UDP while showing a preview window.

Usage examples:
    python stream_to_unity.py
    python stream_to_unity.py --camera 1 --host 127.0.0.1 --port 5053
    python stream_to_unity.py --camera 0 --no-preview
"""

import argparse
import json
import logging
import socket
import time

import cv2

from pose_detector import KEYPOINT_NAMES, PoseDetector
from webcam import WebcamSource

logging.basicConfig(
    level=logging.INFO,
    format="%(asctime)s - %(levelname)s - %(message)s",
)
logger = logging.getLogger(__name__)
PREVIEW_WINDOW_NAME = "PoseGame Webcam Preview"


def resize_to_cover(frame, target_width, target_height):
    if target_width <= 0 or target_height <= 0:
        return frame

    source_height, source_width = frame.shape[:2]
    if source_width <= 0 or source_height <= 0:
        return frame

    scale = max(target_width / source_width, target_height / source_height)
    resized_width = max(1, int(round(source_width * scale)))
    resized_height = max(1, int(round(source_height * scale)))
    resized = cv2.resize(frame, (resized_width, resized_height), interpolation=cv2.INTER_LINEAR)

    crop_x = max(0, (resized_width - target_width) // 2)
    crop_y = max(0, (resized_height - target_height) // 2)
    return resized[crop_y:crop_y + target_height, crop_x:crop_x + target_width]


def build_packet(result, width, height):
    joints = []

    if result["num_people"] > 0:
        keypoints = result["keypoints"][0]
        for index, joint_name in enumerate(KEYPOINT_NAMES):
            x, y, confidence = keypoints[joint_name]
            joints.append(
                {
                    "id": index,
                    "x": round(x / width, 6),
                    "y": round(y / height, 6),
                    "z": 0.0,
                    "confidence": round(confidence, 6),
                }
            )
    else:
        for index in range(len(KEYPOINT_NAMES)):
            joints.append(
                {
                    "id": index,
                    "x": 0.0,
                    "y": 0.0,
                    "z": 0.0,
                    "confidence": 0.0,
                }
            )

    return {
        "timestamp": time.time(),
        "frameWidth": width,
        "frameHeight": height,
        "joints": joints,
    }


def run(args):
    camera = WebcamSource(camera_index=args.camera, width=args.width, height=args.height)
    if not camera.start():
        logger.error("Failed to open camera %s", args.camera)
        return

    time.sleep(0.5)

    detector = PoseDetector(model_name=args.model, confidence=args.confidence)
    socket_client = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)

    logger.info(
        "Streaming pose packets to %s:%s from camera %s",
        args.host,
        args.port,
        args.camera,
    )

    if not args.no_preview:
        cv2.namedWindow(PREVIEW_WINDOW_NAME, cv2.WINDOW_NORMAL)
        cv2.resizeWindow(PREVIEW_WINDOW_NAME, args.width, args.height)

    frame_count = 0
    start_time = time.time()

    try:
        while True:
            frame = camera.read_frame()
            if frame is None:
                time.sleep(0.01)
                continue

            result = detector.detect(frame)
            annotated = result["annotated_frame"]
            if annotated.shape[1] != args.width or annotated.shape[0] != args.height:
                annotated = cv2.resize(annotated, (args.width, args.height))

            packet = build_packet(result, args.width, args.height)
            payload = json.dumps(packet).encode("utf-8")
            socket_client.sendto(payload, (args.host, args.port))

            frame_count += 1

            if not args.no_preview:
                elapsed = time.time() - start_time
                fps = frame_count / elapsed if elapsed > 0 else 0.0
                cv2.putText(
                    annotated,
                    f"FPS: {fps:.1f} | People: {result['num_people']} | UDP: {args.host}:{args.port}",
                    (10, 30),
                    cv2.FONT_HERSHEY_SIMPLEX,
                    0.65,
                    (0, 255, 0),
                    2,
                )
                cv2.putText(
                    annotated,
                    "[Q] Quit - drag this window to your second monitor for preview",
                    (10, annotated.shape[0] - 15),
                    cv2.FONT_HERSHEY_SIMPLEX,
                    0.5,
                    (220, 220, 220),
                    1,
                )

                try:
                    _, _, preview_width, preview_height = cv2.getWindowImageRect(PREVIEW_WINDOW_NAME)
                except cv2.error:
                    preview_width, preview_height = args.width, args.height

                preview_width = preview_width if preview_width > 0 else args.width
                preview_height = preview_height if preview_height > 0 else args.height
                preview_frame = resize_to_cover(annotated, preview_width, preview_height)

                cv2.imshow(PREVIEW_WINDOW_NAME, preview_frame)
                key = cv2.waitKey(1) & 0xFF
                if key == ord("q"):
                    break
            elif frame_count % 100 == 0:
                elapsed = time.time() - start_time
                fps = frame_count / elapsed if elapsed > 0 else 0.0
                logger.info("Processed %s frames at %.1f FPS", frame_count, fps)
    except KeyboardInterrupt:
        logger.info("Interrupted")
    finally:
        socket_client.close()
        camera.stop()
        cv2.destroyAllWindows()


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description="Stream webcam pose data to Unity")
    parser.add_argument("--camera", type=int, default=0, help="Camera index to open")
    parser.add_argument("--width", type=int, default=640, help="Capture width")
    parser.add_argument("--height", type=int, default=480, help="Capture height")
    parser.add_argument("--model", type=str, default="yolo11n-pose.pt", help="YOLO pose model")
    parser.add_argument("--confidence", type=float, default=0.5, help="Pose confidence threshold")
    parser.add_argument("--host", type=str, default="127.0.0.1", help="Unity host")
    parser.add_argument("--port", type=int, default=5053, help="Unity UDP port")
    parser.add_argument("--no-preview", action="store_true", help="Disable the preview window")
    run(parser.parse_args())