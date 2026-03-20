"""Health check endpoint - GET /api/health"""

from flask import jsonify


def health():
    """Health check endpoint"""
    return jsonify({"status": "healthy"})
