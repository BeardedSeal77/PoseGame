"""
Webcam Video Source - Threaded Version
Captures video from USB webcam using OpenCV (synchronous/threaded)
"""

import cv2
import threading
import time
from typing import Optional
import logging

logger = logging.getLogger(__name__)


class WebcamSource:
    """Webcam video source with threaded capture"""
    
    def __init__(self, camera_index: int = 0, width: int = 640, height: int = 480):
        self.camera_index = camera_index
        self.width = width
        self.height = height
        self.cap: Optional[cv2.VideoCapture] = None
        self.is_running = False
        self._lock = threading.Lock()
        self._latest_frame: Optional[bytes] = None
        self._capture_thread: Optional[threading.Thread] = None
    
    def start(self) -> bool:
        """Start webcam capture"""
        if self.is_running:
            logger.warning("[WEBCAM] Already running")
            return True
        
        logger.info(f"[WEBCAM] Connecting to camera {self.camera_index}...")
        
        # Try to open webcam with DirectShow backend (Windows)
        self.cap = cv2.VideoCapture(self.camera_index, cv2.CAP_DSHOW)
        
        if not self.cap.isOpened():
            logger.error("[WEBCAM] Failed to open camera")
            return False
        
        # Configure camera
        self.cap.set(cv2.CAP_PROP_FRAME_WIDTH, self.width)
        self.cap.set(cv2.CAP_PROP_FRAME_HEIGHT, self.height)
        self.cap.set(cv2.CAP_PROP_FPS, 30)
        self.cap.set(cv2.CAP_PROP_BUFFERSIZE, 1)  # Minimize latency
        
        # Test read
        ret, frame = self.cap.read()
        if not ret:
            logger.error("[WEBCAM] Failed to read test frame")
            self.cap.release()
            return False
        
        logger.info(f"[WEBCAM] Test frame captured: {frame.shape}")
        
        # Start capture thread
        self.is_running = True
        self._capture_thread = threading.Thread(target=self._capture_loop, daemon=True)
        self._capture_thread.start()
        
        logger.info("[WEBCAM] Webcam started successfully")
        return True
    
    def stop(self):
        """Stop webcam capture"""
        if not self.is_running:
            return
        
        logger.info("[WEBCAM] Stopping webcam...")
        self.is_running = False
        
        # Wait for capture thread
        if self._capture_thread and self._capture_thread.is_alive():
            self._capture_thread.join(timeout=2)
        
        # Release camera
        if self.cap:
            self.cap.release()
            self.cap = None
        
        logger.info("[WEBCAM] Webcam stopped")
    
    def _capture_loop(self):
        """Background thread that continuously captures frames"""
        logger.info("[WEBCAM] Capture loop started")
        frame_count = 0
        
        while self.is_running and self.cap:
            ret, frame = self.cap.read()
            if ret:
                # Encode to JPEG
                _, jpeg = cv2.imencode('.jpg', frame, [cv2.IMWRITE_JPEG_QUALITY, 85])
                jpeg_bytes = jpeg.tobytes()
                
                # Store latest frame
                with self._lock:
                    self._latest_frame = jpeg_bytes
                
                frame_count += 1
                if frame_count % 100 == 0:
                    logger.debug(f"[WEBCAM] Captured {frame_count} frames")
            else:
                logger.warning("[WEBCAM] Failed to read frame")
                time.sleep(0.1)  # Wait before retry
            
            time.sleep(0.01)  # ~100 FPS capture rate
        
        logger.info("[WEBCAM] Capture loop stopped")
    
    def read_frame(self) -> Optional[bytes]:
        """Read latest frame (non-blocking)"""
        with self._lock:
            return self._latest_frame
