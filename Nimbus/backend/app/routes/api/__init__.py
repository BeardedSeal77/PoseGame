"""API routes module - exports Blueprint with all API endpoints"""

from flask import Blueprint
from .stats import stats
from .health import health
from .video_source import video_source

# Create API blueprint
api_bp = Blueprint('api', __name__, url_prefix='/api')

# Register endpoints
api_bp.add_url_rule('/stats', 'stats', stats, methods=['GET'])
api_bp.add_url_rule('/health', 'health', health, methods=['GET'])
api_bp.add_url_rule('/video/source', 'video_source', video_source, methods=['POST', 'GET'])

