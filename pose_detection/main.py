"""
PoseGame - Pose Detection Pipeline

Captures webcam -> runs YOLO pose estimation -> outputs video with pose overlay.

Usage:
    python main.py                    # Live preview only (press Q to quit)
    python main.py --record out.mp4   # Live preview + save to file
    python main.py --headless out.mp4 # No preview, just save to file

Controls:
    Q - Quit
    S - Snapshot current pose (saves JSON + screenshot to poses/)
"""

import argparse
import json
import os
import time
import logging
import cv2

from webcam import WebcamSource
from pose_detector import PoseDetector
from video_writer import FFmpegWriter

logging.basicConfig(
    level=logging.INFO,
    format="%(asctime)s - %(levelname)s - %(message)s",
)
logger = logging.getLogger(__name__)


def save_pose_snapshot(poses_dir, name, result, annotated_frame, width, height):
    """Save pose keypoints as JSON + annotated screenshot."""
    # Use the first detected person
    keypoints = result["keypoints"][0]

    # Normalize keypoints to 0-1 range relative to frame size
    normalized = {}
    for joint_name, (x, y, conf) in keypoints.items():
        normalized[joint_name] = {
            "x": round(x / width, 4),
            "y": round(y / height, 4),
            "confidence": round(conf, 4),
        }

    pose_data = {
        "name": name,
        "frame_width": width,
        "frame_height": height,
        "keypoints": normalized,
    }

    # Save JSON
    json_path = os.path.join(poses_dir, f"{name}.json")
    with open(json_path, "w") as f:
        json.dump(pose_data, f, indent=2)
    logger.info(f"  -> {json_path}")

    # Save screenshot (use a clean copy so HUD text isn't baked in)
    img_path = os.path.join(poses_dir, f"{name}.png")
    success = cv2.imwrite(img_path, annotated_frame)
    if success:
        logger.info(f"  -> {img_path}")
    else:
        logger.error(f"  Failed to save image: {img_path}")


def run(args):
    # Init webcam
    cam = WebcamSource(camera_index=args.camera, width=args.width, height=args.height)
    if not cam.start():
        logger.error("Failed to start webcam")
        return

    # Wait for first frame
    time.sleep(0.5)

    # Init pose detector
    detector = PoseDetector(model_name=args.model, confidence=args.confidence)

    # Init video writer (if recording)
    writer = None
    if args.record or args.headless:
        output_path = args.record or args.headless
        writer = FFmpegWriter(output_path, args.width, args.height, fps=cam.fps)
        if not writer.start():
            logger.error("Failed to start ffmpeg writer. Is ffmpeg in PATH?")
            cam.stop()
            return

    show_preview = not args.headless
    frame_count = 0
    fps_start = time.time()

    # Poses output directory
    poses_dir = os.path.join(os.path.dirname(__file__), "poses")
    os.makedirs(poses_dir, exist_ok=True)
    pose_count = len([f for f in os.listdir(poses_dir) if f.endswith(".json")])

    logger.info("Pipeline running. Press Q to quit, S to snapshot a pose.")

    try:
        while True:
            frame = cam.read_frame()
            if frame is None:
                time.sleep(0.01)
                continue

            # Run pose detection
            result = detector.detect(frame)
            annotated = result["annotated_frame"]

            # Resize annotated frame to match expected output dimensions
            if annotated.shape[1] != args.width or annotated.shape[0] != args.height:
                annotated = cv2.resize(annotated, (args.width, args.height))

            # Keep a clean copy before HUD text is drawn (for screenshots)
            clean_annotated = annotated.copy()

            # Write to video file
            if writer:
                writer.write_frame(annotated)

            # Show preview
            if show_preview:
                # Draw FPS counter
                frame_count += 1
                elapsed = time.time() - fps_start
                if elapsed > 0:
                    fps = frame_count / elapsed
                    cv2.putText(
                        annotated, f"FPS: {fps:.1f} | People: {result['num_people']}",
                        (10, 30), cv2.FONT_HERSHEY_SIMPLEX, 0.7, (0, 255, 0), 2,
                    )

                cv2.putText(
                    annotated, "[S] Snapshot  [Q] Quit",
                    (10, args.height - 15), cv2.FONT_HERSHEY_SIMPLEX, 0.5, (200, 200, 200), 1,
                )

                cv2.imshow("PoseGame - Pose Detection", annotated)
                key = cv2.waitKey(1) & 0xFF

                if key == ord("q"):
                    break

                if key == ord("s"):
                    if result["num_people"] == 0:
                        logger.warning("No pose detected - snapshot skipped")
                    else:
                        pose_count += 1
                        name = f"pose_{pose_count:03d}"
                        save_pose_snapshot(
                            poses_dir, name, result, clean_annotated,
                            args.width, args.height,
                        )
                        logger.info(f"Saved pose snapshot: {name}")
            else:
                frame_count += 1
                if frame_count % 100 == 0:
                    elapsed = time.time() - fps_start
                    fps = frame_count / elapsed if elapsed > 0 else 0
                    logger.info(f"Processed {frame_count} frames ({fps:.1f} FPS)")

    except KeyboardInterrupt:
        logger.info("Interrupted")
    finally:
        if show_preview:
            cv2.destroyAllWindows()
        if writer:
            writer.stop()
        cam.stop()
        logger.info(f"Done. Processed {frame_count} frames.")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description="PoseGame Pose Detection")
    parser.add_argument("--camera", type=int, default=0, help="Camera index (default: 0)")
    parser.add_argument("--width", type=int, default=640, help="Frame width (default: 640)")
    parser.add_argument("--height", type=int, default=480, help="Frame height (default: 480)")
    parser.add_argument("--model", type=str, default="yolo11n-pose.pt",
                        help="YOLO pose model (default: yolo11n-pose.pt)")
    parser.add_argument("--confidence", type=float, default=0.5,
                        help="Detection confidence threshold (default: 0.5)")
    parser.add_argument("--record", type=str, default=None,
                        help="Record to MP4 file (with live preview)")
    parser.add_argument("--headless", type=str, default=None,
                        help="Record to MP4 file (no preview window)")
    run(parser.parse_args())
