"""
Flask Server - Main Application
App factory pattern for clean dependency injection
"""

from flask import Flask, render_template
from flask_cors import CORS
import logging

from .hub_threaded import Hub, HubConfig
from .routes.video import video_bp  # Video streaming endpoints (stream, raw, processed)
from .routes.api import api_bp      # API endpoints (stats, health, source)
from .routes.drone import drone_bp  # Drone communication endpoints (video, state, targets)
from .routes.webrtc import webrtc_bp

logger = logging.getLogger(__name__)


def create_app():
    """Flask app factory"""
    # Create Flask app
    app = Flask(__name__, 
                static_folder='../../webapp/static',
                template_folder='../../webapp/templates')
    
    # Enable CORS
    CORS(app)
    
    # Initialize hub
    config = HubConfig()
    hub = Hub(config)
    app.config['HUB'] = hub
    
    # Start hub immediately
    logger.info("[SERVER] Starting hub...")
    hub.start()
    
    # Register blueprints (url_prefix defined in blueprint __init__.py)
    app.register_blueprint(video_bp)
    app.register_blueprint(api_bp)
    app.register_blueprint(drone_bp)
    app.register_blueprint(webrtc_bp, url_prefix='/webrtc')
    
    # Index route
    @app.route('/')
    def index():
        return render_template('control.html')
    
    # WebRTC page route
    @app.route('/webrtc')
    def webrtc_page():
        return render_template('webrtc.html')
    
    logger.info("[SERVER] Flask app created")
    return app


if __name__ == '__main__':
    logging.basicConfig(
        level=logging.INFO,
        format='%(asctime)s - %(name)s - %(levelname)s - %(message)s'
    )
    
    app = create_app()
    app.run(host='0.0.0.0', port=8000, threaded=True, debug=False)
