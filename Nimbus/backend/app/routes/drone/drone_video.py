"""
POST /drone/video - Receive video frames from Webots drone controller
"""

from flask import request, jsonify, current_app
import base64
import logging

logger = logging.getLogger(__name__)


def drone_video():
    """
    Receive MJPEG video frame from drone controller.
    Drone posts: {"data": "base64_jpeg", "timestamp": float, "width": int, "height": int}
    """
    try:
        data = request.get_json()
        
        if not data or 'data' not in data:
            return jsonify({"error": "Missing video data"}), 400
        
        # Decode base64 JPEG
        jpeg_base64 = data['data']
        jpeg_bytes = base64.b64decode(jpeg_base64)
        
        # Get hub instance
        hub = current_app.config.get('HUB')
        if not hub:
            return jsonify({"error": "Hub not initialized"}), 500
        
        # Store latest frame in hub
        with hub._frame_lock:
            hub._latest_raw_frame = jpeg_bytes
            hub._latest_processed_frame = jpeg_bytes  # No processing for game mode
        
        # Update video source to webots when receiving drone video
        if hub.state.current_source != "webots":
            hub.state.current_source = "webots"
            logger.info("[DRONE VIDEO] Automatically switched video source to 'webots'")
        
        # Update frame timestamp
        hub.state.last_frame_timestamp = data.get('timestamp', 0)
        hub.state.frame_count += 1
        
        # Log periodically (every 30 frames)
        if hub.state.frame_count % 30 == 0:
            width = data.get('width', 'unknown')
            height = data.get('height', 'unknown')
            logger.info(f"[DRONE VIDEO] Frame {hub.state.frame_count}: {width}x{height}, {len(jpeg_bytes)} bytes")
        
        return jsonify({"status": "ok"}), 200
    
    except Exception as e:
        logger.error(f"[DRONE VIDEO] Error processing video: {e}")
        return jsonify({"error": str(e)}), 500
