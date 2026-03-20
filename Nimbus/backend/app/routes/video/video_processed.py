"""Processed video frame with overlays - GET /video/processed"""

import cv2
from flask import Response, current_app


def video_processed():
    """Get a single processed frame with overlays"""
    hub = current_app.config['HUB']
    
    frame = hub.get_processed_frame()
    if frame is None:
        return Response("No video source active", status=503)
    
    _, buffer = cv2.imencode('.jpg', frame)
    return Response(buffer.tobytes(), mimetype='image/jpeg')
