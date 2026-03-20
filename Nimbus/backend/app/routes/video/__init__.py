"""Video routes module - exports Blueprint with all video endpoints"""

from flask import Blueprint
from .video_stream import video_stream
from .video_raw import video_raw
from .video_processed import video_processed

# Create video blueprint
video_bp = Blueprint('video', __name__, url_prefix='/video')

# Register endpoints
video_bp.add_url_rule('/stream', 'stream', video_stream, methods=['GET'])
video_bp.add_url_rule('/raw', 'raw', video_raw, methods=['GET'])
video_bp.add_url_rule('/processed', 'processed', video_processed, methods=['GET'])


