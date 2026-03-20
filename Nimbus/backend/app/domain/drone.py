"""
Drone Controller - Abstract Base Class
Modular architecture for managing multiple drones in racing game
"""

from abc import ABC, abstractmethod
from typing import Optional, Dict
from dataclasses import dataclass, field
import time


@dataclass
class DroneState:
    """Complete drone state snapshot"""
    # Identity
    id: int
    name: str
    
    # Position & Movement
    position: Dict[str, float] = field(default_factory=lambda: {"x": 0.0, "y": 0.0, "z": 0.0})
    velocity: Dict[str, float] = field(default_factory=lambda: {"x": 0.0, "y": 0.0, "z": 0.0})
    acceleration: Dict[str, float] = field(default_factory=lambda: {"x": 0.0, "y": 0.0, "z": 0.0})
    
    # Orientation
    orientation: Dict[str, float] = field(default_factory=lambda: {"roll": 0.0, "pitch": 0.0, "yaw": 0.0})
    angular_velocity: Dict[str, float] = field(default_factory=lambda: {"roll": 0.0, "pitch": 0.0, "yaw": 0.0})
    
    # Navigation
    current_target: Optional[str] = None
    distance_to_target: Optional[float] = None
    
    # Game State
    score: int = 0
    checkpoints_completed: int = 0
    
    # Status
    connected: bool = False
    last_update: float = field(default_factory=time.time)
    mode: str = "MANUAL"  # MANUAL, AUTO, LANDING, TAKEOFF


class DroneController(ABC):
    """
    Abstract base class for drone controllers.
    Implementations can be Webots-based, physical drones, or simulated.
    """
    
    def __init__(self, drone_id: int, name: str):
        self.state = DroneState(id=drone_id, name=name)
        self._target_queue: Optional[str] = None  # Pending target assignment
    
    @abstractmethod
    def update_state(self, telemetry: dict) -> None:
        """
        Update drone state from incoming telemetry data.
        Called when drone posts its state via HTTP.
        
        Args:
            telemetry: Raw telemetry dict from drone controller
        """
        pass
    
    @abstractmethod
    def set_target(self, target_name: str, target_position: dict) -> None:
        """
        Command drone to navigate to a target.
        
        Args:
            target_name: Name of target object (e.g., "car", "human")
            target_position: World coordinates {"x": ..., "y": ..., "z": ...}
        """
        pass
    
    def get_pending_target(self) -> Optional[dict]:
        """
        Get queued target for drone to poll.
        Drone controllers call GET /drone/{id}/target to retrieve this.
        
        Returns:
            Target dict with name and position, or None
        """
        if self._target_queue:
            return {
                "target_name": self._target_queue,
                "has_target": True
            }
        return {"has_target": False}
    
    def clear_target(self) -> None:
        """Clear current target (called when reached or cancelled)"""
        self.state.current_target = None
        self._target_queue = None
    
    def get_state_dict(self) -> dict:
        """
        Get complete drone state as dictionary for API/frontend.
        
        Returns:
            Serializable state dict
        """
        return {
            "id": self.state.id,
            "name": self.state.name,
            "position": self.state.position,
            "velocity": self.state.velocity,
            "acceleration": self.state.acceleration,
            "orientation": self.state.orientation,
            "angular_velocity": self.state.angular_velocity,
            "current_target": self.state.current_target,
            "distance_to_target": self.state.distance_to_target,
            "score": self.state.score,
            "checkpoints_completed": self.state.checkpoints_completed,
            "connected": self.state.connected,
            "mode": self.state.mode,
            "last_update": self.state.last_update
        }
    
    def is_connected(self) -> bool:
        """Check if drone is still sending telemetry (within last 2 seconds)"""
        return (time.time() - self.state.last_update) < 2.0
    
    def increment_score(self, points: int = 1) -> None:
        """Award points to drone"""
        self.state.score += points
    
    def reset(self) -> None:
        """Reset drone state (for new race)"""
        self.state.score = 0
        self.state.checkpoints_completed = 0
        self.state.current_target = None
        self._target_queue = None


# =============================================================================
# EXAMPLE IMPLEMENTATION: Webots Drone
# =============================================================================

class WebotsDroneController(DroneController):
    """
    Concrete implementation for Webots-simulated drones.
    Communicates via HTTP POST (telemetry) and HTTP GET (target polling).
    """
    
    def update_state(self, telemetry: dict) -> None:
        """Parse telemetry from Webots Python controller"""
        # Update position
        if "position" in telemetry:
            self.state.position = telemetry["position"]
        
        # Update velocity
        if "velocity" in telemetry:
            self.state.velocity = telemetry["velocity"]
        
        # Update acceleration
        if "acceleration" in telemetry:
            self.state.acceleration = telemetry["acceleration"]
        
        # Update orientation
        if "orientation" in telemetry:
            self.state.orientation = telemetry["orientation"]
        
        # Update angular velocity
        if "angular_velocity" in telemetry:
            self.state.angular_velocity = telemetry["angular_velocity"]
        
        # Update mode
        if "mode" in telemetry:
            self.state.mode = telemetry["mode"]
        
        # Update distance to target (if navigating)
        if "distance_to_home" in telemetry:
            self.state.distance_to_target = telemetry.get("distance_to_home")
        
        # Mark connected and update timestamp
        self.state.connected = True
        self.state.last_update = time.time()
    
    def set_target(self, target_name: str, target_position: dict) -> None:
        """Queue target for Webots drone to poll"""
        self.state.current_target = target_name
        self._target_queue = target_name
        
        # Store target position for distance calculations (if needed)
        # In practice, Webots controller calculates distance internally
        print(f"[DRONE {self.state.id}] Target set: {target_name} at {target_position}")


# =============================================================================
# USAGE EXAMPLE
# =============================================================================

if __name__ == "__main__":
    # Create two drone instances
    drone1 = WebotsDroneController(drone_id=1, name="Red Drone")
    drone2 = WebotsDroneController(drone_id=2, name="Blue Drone")
    
    # Simulate telemetry update from Webots controller
    telemetry = {
        "position": {"x": 1.5, "y": 2.3, "z": 0.8},
        "velocity": {"x": 0.2, "y": 0.1, "z": 0.0},
        "orientation": {"roll": 0.0, "pitch": 0.0, "yaw": 1.57},
        "mode": "MANUAL"
    }
    drone1.update_state(telemetry)
    
    # Command drone to target
    target_position = {"x": 10.0, "y": 5.0, "z": 1.0}
    drone1.set_target("car", target_position)
    
    # Get state for API response
    state = drone1.get_state_dict()
    print(f"Drone 1 State: {state}")
    
    # Drone polls for pending target
    pending = drone1.get_pending_target()
    print(f"Pending Target: {pending}")
