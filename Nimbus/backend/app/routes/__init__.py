"""
Routes package
Flask blueprints for API, video streaming, and drone communication
"""

from .video import video_bp
from .api import api_bp
from .drone import drone_bp

__all__ = ['video_bp', 'api_bp', 'drone_bp']

