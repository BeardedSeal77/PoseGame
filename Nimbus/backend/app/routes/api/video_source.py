"""Video source control endpoint - POST/GET /api/video/source"""

from flask import jsonify, request, current_app


def video_source():
    """Control video source (POST) or get current source (GET)"""
    hub = current_app.config['HUB']
    
    if request.method == 'POST':
        data = request.json
        source = data.get('source', 'webcam')
        
        if source == 'stop':
            hub.stop_video_source()
        elif source == 'webcam':
            hub.start_webcam()
        elif source == 'webots':
            hub.start_webots()
        else:
            return jsonify({"error": "Invalid source"}), 400
        
        return jsonify({"status": "ok", "source": source})
    
    # GET request
    return jsonify({"source": hub.state.current_source})
