"""Generate MJPEG stream helper"""

import time
import cv2
import numpy as np


def create_loading_frame():
    """Create a loading screen frame"""
    frame = np.zeros((480, 640, 3), dtype=np.uint8)
    cv2.putText(frame, "Nimbus Hub", (220, 200),
                cv2.FONT_HERSHEY_SIMPLEX, 1.2, (255, 255, 255), 2)
    cv2.putText(frame, "Loading video...", (200, 250),
                cv2.FONT_HERSHEY_SIMPLEX, 0.8, (255, 255, 255), 2)
    cv2.putText(frame, "Please wait", (230, 300),
                cv2.FONT_HERSHEY_SIMPLEX, 0.7, (200, 200, 200), 1)
    
    _, jpeg = cv2.imencode('.jpg', frame, [cv2.IMWRITE_JPEG_QUALITY, 90])
    return jpeg.tobytes()


def generate_mjpeg(hub):
    """Generate MJPEG stream from hub"""
    loading_bytes = create_loading_frame()
    frame_count = 0
    
    while True:
        frame_bytes = hub.latest_processed_frame()
        
        if not frame_bytes:
            frame_bytes = loading_bytes
        else:
            frame_count += 1
        
        # Yield MJPEG frame
        yield (b'--frame\r\n'
               b'Content-Type: image/jpeg\r\n\r\n' + frame_bytes + b'\r\n')
        
        time.sleep(0.033)  # ~30 FPS
