"""Raw video frame endpoint - GET /video/raw"""

import cv2
from flask import Response, current_app


def video_raw():
    """Get a single raw frame"""
    hub = current_app.config['HUB']
    
    frame = hub.get_current_frame()
    if frame is None:
        return Response("No video source active", status=503)
    
    _, buffer = cv2.imencode('.jpg', frame)
    return Response(buffer.tobytes(), mimetype='image/jpeg')
