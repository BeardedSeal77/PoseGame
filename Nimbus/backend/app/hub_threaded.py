"""
Nimbus Hub - Game Server (Simplified)
Core orchestration for multiplayer drone racing game.

Responsibilities:
- Video streaming from drone cameras (for spectator view)
- Game state management (drone positions, targets, scores)
- World object registry (static positions from Webots)
- Player target assignment and race logic
- WebSocket/HTTP API for frontend communication

This is the main entrypoint. To run the entire system:
  python -m backend.app.hub_threaded
"""

import threading
import time
from typing import Optional, List
import logging

from .domain.events import (
    VIDEO_RAW_TOPIC,
    VIDEO_PROCESSED_TOPIC,
)
from .infrastructure.broker import PubSubBroker
from .infrastructure.state import HubConfig, HubState
from .services.capture.webcam_threaded import WebcamSource
from .services.capture.webots import WebotsSource

logger = logging.getLogger(__name__)

# Global configuration: Set default video source
DEFAULT_VIDEO_SOURCE = "none"  # Options: "webcam", "webots", "none" (drones send video directly)


class Hub:
    """Main hub for drone racing game - manages video streams and game state"""
    
    def __init__(self, config: HubConfig) -> None:
        self.config = config
        self.state = HubState()
        self.broker = PubSubBroker()
        
        # Video sources (optional - drones can send video directly)
        self.webcam_source: Optional[WebcamSource] = None
        self.webots_source: Optional[WebotsSource] = None
        
        # Frame storage (for video streaming)
        self._latest_raw_frame: Optional[bytes] = None
        self._latest_processed_frame: Optional[bytes] = None
        self._frame_lock = threading.Lock()
        
        # Threading
        self._shutdown = threading.Event()
        self._pipeline_thread: Optional[threading.Thread] = None
        
        logger.info("[HUB] Game server initialized")
    
    def start(self):
        """Start hub in background thread"""
        logger.info("[HUB] Starting hub...")
        
        # Start video pipeline thread
        self._pipeline_thread = threading.Thread(target=self._video_pipeline_loop, daemon=True)
        self._pipeline_thread.start()
        
        # Auto-start default video source
        threading.Thread(target=self._start_default_source, daemon=True).start()
        
        logger.info("[HUB] Hub started")
    
    def stop(self):
        """Stop hub and cleanup"""
        logger.info("[HUB] Stopping hub...")
        self._shutdown.set()
        
        # Stop all video sources
        if self.webcam_source:
            self.webcam_source.stop()
            self.webcam_source = None
        if self.webots_source:
            self.webots_source.stop()
            self.webots_source = None
        
        # Wait for pipeline thread
        if self._pipeline_thread and self._pipeline_thread.is_alive():
            self._pipeline_thread.join(timeout=2)
        
        logger.info("[HUB] Hub stopped")
    
    def _start_default_source(self):
        """Auto-start default video source after brief delay"""
        time.sleep(0.5)  # Let server start first
        logger.info(f"[HUB] Auto-starting default video source: {DEFAULT_VIDEO_SOURCE}")
        self.switch_video_source(DEFAULT_VIDEO_SOURCE)
    
    def switch_video_source(self, source: str) -> bool:
        """Switch between webcam/webots/none"""
        logger.info(f"[HUB] Switching video source to: {source}")
        
        # Stop current sources
        if self.webcam_source:
            self.webcam_source.stop()
            self.webcam_source = None
        if self.webots_source:
            self.webots_source.stop()
            self.webots_source = None
        
        self.state.current_source = "none"
        
        # Start new source
        if source == "webcam":
            self.webcam_source = WebcamSource()
            if self.webcam_source.start():
                self.state.current_source = "webcam"
                logger.info("[HUB] Webcam source started")
                return True
            else:
                logger.error("[HUB] Failed to start webcam")
                return False
        
        elif source == "webots":
            self.webots_source = WebotsSource()
            if self.webots_source.start():
                self.state.current_source = "webots"
                logger.info("[HUB] Webots source started")
                return True
            else:
                logger.error("[HUB] Failed to start webots")
                return False
        
        elif source == "none":
            logger.info("[HUB] Video source set to none")
            return True
        
        else:
            logger.error(f"[HUB] Unknown video source: {source}")
            return False
    
    def _video_pipeline_loop(self):
        """Main video processing loop (runs in thread) - simplified for game"""
        logger.info("[HUB] Video pipeline loop started")
        
        fps_frame_count = 0
        fps_last_time = time.time()
        
        while not self._shutdown.is_set():
            # Read frame from active source
            jpeg_bytes = self._read_from_active_source()
            if jpeg_bytes is None:
                time.sleep(0.01)
                continue
            
            ts = time.time()
            self.state.frame_count += 1
            frame_id = self.state.frame_count
            
            # Calculate FPS every 30 frames
            fps_frame_count += 1
            if fps_frame_count >= 30:
                elapsed = ts - fps_last_time
                if elapsed > 0:
                    self.state.fps = fps_frame_count / elapsed
                fps_frame_count = 0
                fps_last_time = ts
            
            # Store raw frame (pass-through, no processing)
            with self._frame_lock:
                self._latest_raw_frame = jpeg_bytes
                self._latest_processed_frame = jpeg_bytes  # No overlay processing
            self.state.last_frame_timestamp = ts
            
            # Publish raw frame event
            self.broker.publish(VIDEO_RAW_TOPIC, {"frame_id": frame_id, "timestamp": ts})
            
            # Publish processed frame event (same as raw for game)
            self.broker.publish(VIDEO_PROCESSED_TOPIC, {"frame_id": frame_id, "timestamp": ts})
            self.broker.publish(VIDEO_PROCESSED_TOPIC, {"frame_id": frame_id, "timestamp": ts})
            
            # Log periodically (commented out to reduce console spam)
            # if frame_id % 30 == 0:
            #     logger.info(f"[HUB] Generated processed frame {frame_id}, size: {len(processed_frame)} bytes")
            
            # Small sleep to avoid CPU spinning
            time.sleep(0.001)
        
        logger.info("[HUB] Video pipeline loop stopped")
    
    def _read_from_active_source(self) -> Optional[bytes]:
        """Read frame from active video source"""
        if self.state.current_source == "webcam" and self.webcam_source:
            return self.webcam_source.read_frame()
        elif self.state.current_source == "webots" and self.webots_source:
            return self.webots_source.read_frame()
        return None
    
    def latest_processed_frame(self) -> Optional[bytes]:
        """Get latest processed frame (for video streaming)"""
        with self._frame_lock:
            return self._latest_processed_frame
    
    def latest_raw_frame(self) -> Optional[bytes]:
        """Get latest raw frame"""
        with self._frame_lock:
            return self._latest_raw_frame


# ============================================================================
# MAIN: Run hub with Flask server
# ============================================================================

if __name__ == "__main__":
    import logging
    
    # Configure logging
    logging.basicConfig(
        level=logging.INFO,
        format='%(asctime)s - %(name)s - %(levelname)s - %(message)s'
    )
    
    # Disable Flask/Werkzeug HTTP request logging (too noisy)
    logging.getLogger('werkzeug').setLevel(logging.ERROR)
    
    # Import Flask server
    from .server import create_app
    
    # Create Flask app with hub
    app = create_app()
    hub = app.config['HUB']
    
    try:
        logger.info("=" * 60)
        logger.info("Starting Nimbus Hub with Flask server")
        logger.info("=" * 60)
        
        # Run Flask server (blocking)
        app.run(host='0.0.0.0', port=8000, threaded=True, debug=False)
    
    except KeyboardInterrupt:
        logger.info("\nShutdown requested...")
    finally:
        hub.stop()
        logger.info("Nimbus Hub shut down")
