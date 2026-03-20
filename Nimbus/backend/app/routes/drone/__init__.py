"""Drone routes module - endpoints for drone communication"""

from flask import Blueprint
from .drone_video import drone_video

# Create drone blueprint
drone_bp = Blueprint('drone', __name__, url_prefix='/drone')

# Register endpoints
drone_bp.add_url_rule('/video', 'video', drone_video, methods=['POST'])
