"""Stats endpoint - GET /api/stats"""

from flask import jsonify, current_app


def stats():
    """Get detection and processing statistics"""
    hub = current_app.config['HUB']
    state = hub.state
    
    # Calculate rolling detection rate over last 10 detections
    detection_rate = 0.0
    if len(state.recent_detections) > 0:
        detection_rate = sum(state.recent_detections) / len(state.recent_detections)
    
    return jsonify({
        "video_source": state.current_source,
        "target_object": state.target_object,
        "detection_rate": detection_rate,
        "fps": state.fps,
        "processing": state.processing_enabled
    })
