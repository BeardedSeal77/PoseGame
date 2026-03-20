"""MJPEG video stream endpoint - GET /video/stream"""

from flask import Response, current_app
from .generate_mjpeg import generate_mjpeg


def video_stream():
    """MJPEG video stream endpoint"""
    hub = current_app.config['HUB']
    return Response(
        generate_mjpeg(hub),
        mimetype='multipart/x-mixed-replace; boundary=frame'
    )
