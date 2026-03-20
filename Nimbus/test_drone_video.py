#!/usr/bin/env python3
"""
Test script to verify drone video endpoint is working
Sends a test frame to the backend
"""

import requests
import base64
from PIL import Image
import io
import time

# Create a test image (black with white text)
def create_test_frame(width=1280, height=720, text="TEST FRAME"):
    """Create a test JPEG frame"""
    img = Image.new('RGB', (width, height), color='black')
    
    # Convert to JPEG
    buffer = io.BytesIO()
    img.save(buffer, format='JPEG', quality=85)
    jpeg_bytes = buffer.getvalue()
    
    return jpeg_bytes

def send_test_frame():
    """Send test frame to backend"""
    jpeg_bytes = create_test_frame()
    jpeg_base64 = base64.b64encode(jpeg_bytes).decode('ascii')
    
    payload = {
        'data': jpeg_base64,
        'timestamp': time.time(),
        'width': 1280,
        'height': 720
    }
    
    try:
        response = requests.post(
            'http://localhost:8000/drone/video',
            json=payload,
            timeout=1
        )
        print(f"✅ Response: {response.status_code} - {response.json()}")
        return True
    except Exception as e:
        print(f"❌ Error: {e}")
        return False

if __name__ == "__main__":
    print("=" * 60)
    print("Testing Drone Video Endpoint")
    print("=" * 60)
    print("\n1. Make sure Flask backend is running:")
    print("   python -m backend.app.hub_threaded")
    print("\n2. Sending test frame...")
    
    if send_test_frame():
        print("\n✅ SUCCESS! Video endpoint is working.")
        print("\n3. Now open: http://localhost:3000/drone")
        print("   You should see the video stream.")
    else:
        print("\n❌ FAILED! Check if Flask backend is running on port 8000.")
