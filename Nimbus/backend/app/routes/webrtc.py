"""
WebRTC Video Routes - Real Video Streaming
Flask routes for WebRTC-based video streaming with aiortc
"""

from flask import Blueprint, request, jsonify, current_app
import asyncio
import json
import logging
from aiortc import RTCPeerConnection, RTCSessionDescription, VideoStreamTrack
from aiortc.contrib.media import MediaRelay
import cv2
import numpy as np
from av import VideoFrame

logger = logging.getLogger(__name__)

webrtc_bp = Blueprint('webrtc', __name__)

# Store active peer connections
pcs = set()
relay = MediaRelay()


class HubVideoTrack(VideoStreamTrack):
    """
    Custom video track that reads from the hub's video pipeline
    """
    kind = "video"
    
    def __init__(self, hub):
        super().__init__()
        self.hub = hub
        self.frame_count = 0
    
    async def recv(self):
        """Receive the next video frame"""
        pts, time_base = await self.next_timestamp()
        
        # Get latest frame from hub
        frame_bytes = self.hub.latest_processed_frame()
        
        if frame_bytes is None:
            # Create black frame if no video available
            img = np.zeros((480, 640, 3), dtype=np.uint8)
            cv2.putText(img, "Waiting for video...", (200, 240),
                       cv2.FONT_HERSHEY_SIMPLEX, 0.8, (255, 255, 255), 2)
        else:
            # Decode JPEG to numpy array
            np_arr = np.frombuffer(frame_bytes, np.uint8)
            img = cv2.imdecode(np_arr, cv2.IMREAD_COLOR)
        
        # Convert BGR to RGB
        img = cv2.cvtColor(img, cv2.COLOR_BGR2RGB)
        
        # Create VideoFrame
        frame = VideoFrame.from_ndarray(img, format="rgb24")
        frame.pts = pts
        frame.time_base = time_base
        
        self.frame_count += 1
        if self.frame_count % 30 == 0:
            logger.info(f"[WEBRTC] Sent frame {self.frame_count}")
        
        return frame


@webrtc_bp.route('/offer', methods=['POST'])
def offer():
    """Handle WebRTC offer from client"""
    params = request.get_json()
    offer = RTCSessionDescription(sdp=params["sdp"], type=params["type"])
    
    pc = RTCPeerConnection()
    pcs.add(pc)
    
    hub = current_app.config['HUB']
    
    @pc.on("connectionstatechange")
    async def on_connectionstatechange():
        logger.info(f"[WEBRTC] Connection state is {pc.connectionState}")
        if pc.connectionState == "failed":
            await pc.close()
            pcs.discard(pc)
    
    # Create video track
    video_track = HubVideoTrack(hub)
    pc.addTrack(relay.subscribe(video_track))
    
    # Handle the offer
    loop = asyncio.new_event_loop()
    asyncio.set_event_loop(loop)
    
    async def handle_offer():
        await pc.setRemoteDescription(offer)
        answer = await pc.createAnswer()
        await pc.setLocalDescription(answer)
        return {
            "sdp": pc.localDescription.sdp,
            "type": pc.localDescription.type
        }
    
    try:
        answer_data = loop.run_until_complete(handle_offer())
        logger.info("[WEBRTC] WebRTC connection established")
        return jsonify(answer_data)
    except Exception as e:
        logger.error(f"[WEBRTC] Error handling offer: {e}")
        return jsonify({"error": str(e)}), 500
    finally:
        loop.close()


@webrtc_bp.route('/close', methods=['POST'])
def close():
    """Close all peer connections"""
    loop = asyncio.new_event_loop()
    asyncio.set_event_loop(loop)
    
    async def close_all():
        coros = [pc.close() for pc in pcs]
        await asyncio.gather(*coros)
        pcs.clear()
    
    try:
        loop.run_until_complete(close_all())
        logger.info("[WEBRTC] All connections closed")
        return jsonify({"status": "closed"})
    except Exception as e:
        logger.error(f"[WEBRTC] Error closing connections: {e}")
        return jsonify({"error": str(e)}), 500
    finally:
        loop.close()
