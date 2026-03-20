# Copyright 1996-2024 Cyberbotics Ltd.
#
# Licensed under the Apache License, Version 2.0 (the "License");
# you may not use this file except in compliance with the License.
# You may obtain a copy of the License at
#
#     https://www.apache.org/licenses/LICENSE-2.0
#
# Unless required by applicable law or agreed to in writing, software
# distributed under the License is distributed on an "AS IS" BASIS,
# WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
# See the License for the specific language governing permissions and
# limitations under the License.

"""Python controller for Mavic 2 Pro with ROS2 integration via rosbridge.
   Connects to ROS2 container running at localhost:9090
   Publishes MJPEG video stream to Nimbus Hub via HTTP POST"""

from controller import Robot, Keyboard
import json
import time
import threading
import base64
import io
import queue
import requests
from PID import AltitudeController, clamp
from PIDYaw import YawController
from PIDMovement import MovementController
try:
    import websocket
except ImportError:
    print("Warning: 'websocket-client' module not found. Install with: pip install websocket-client")
    websocket = None
try:
    from PIL import Image
except ImportError:
    print("Warning: 'Pillow' module not found. Install with: pip install Pillow")
    Image = None


# clamp() function moved to PID.py

# ============================================================================
# HTTP SESSION WITH CONNECTION POOLING
# ============================================================================

# Create persistent session to reuse TCP connections and avoid socket exhaustion
# This session is shared across all HTTP requests to reduce connection overhead
_http_session = requests.Session()
_http_adapter = requests.adapters.HTTPAdapter(
    pool_connections=10,  # Number of connection pools to cache
    pool_maxsize=20,      # Maximum connections to save in the pool
    max_retries=0         # No retries for speed (timeouts are already low)
)
_http_session.mount('http://', _http_adapter)
_http_session.mount('https://', _http_adapter)

print("[HTTP] Connection pooling initialized (pool_size=20, connections=10)")


# ============================================================================
# GLOBAL CONFIGURATION
# ============================================================================

CONFIG = {
    # Flight Control Parameters (balanced for racing game)
    'K_VERTICAL_THRUST': 68.5,  # Base thrust (stable value)
    'K_VERTICAL_OFFSET': 0.6,
    'K_VERTICAL_P': 3.8,  # Slightly reduced for stability
    'K_VERTICAL_D': 0.6,  # Increased damping to prevent flip
    'K_ROLL_P': 60.0,     # Reduced from 70 for stability
    'K_PITCH_P': 45.0,    # Reduced from 50 for stability

    # Motor limiting (prevent overcorrection oscillations)
    'MAX_MOTOR_DIFFERENTIAL': 18.0,  # Slightly reduced for stability

    # Control Inputs (keyboard disturbances) - racing game values
    'PITCH_DISTURBANCE_FORWARD': -12.0,   # Reduced from -15 for stability
    'PITCH_DISTURBANCE_BACKWARD': 12.0,   # Reduced from 15
    'YAW_DISTURBANCE_RIGHT': -1.8,        # Slightly reduced
    'YAW_DISTURBANCE_LEFT': 1.8,
    'ROLL_DISTURBANCE_RIGHT': -12.0,      # Reduced from -15 for stability
    'ROLL_DISTURBANCE_LEFT': 12.0,
    'ALTITUDE_INCREMENT': 0.08,           # Slightly reduced

    # Balanced arcade racing: fast but stable response
    'DISTURBANCE_RAMP_TIME': 0.08,  # Sweet spot: fast response without flipping

    # Home Position
    'HOME_POSITION': {'x': 0.0, 'y': 0.0, 'z': 0.0},
    'TAKEOFF_ALTITUDE': 2.0,  # Home + 2m on z-axis

    # Video Stream
    'VIDEO_JPEG_QUALITY': 85,

    # ROS2 Connection
    'ROS2_HOST': 'localhost',
    'ROS2_PORT': 9090,
    'ROS2_PUBLISH_INTERVAL': 0.033,  # 30Hz

    # Nimbus Hub (Central Communication)
    'HUB_URL': 'http://localhost:8000',
    'HUB_PUBLISH_INTERVAL': 0.01,  # 100Hz (10ms)

    # Simulation and Autonomous Control
    'SIMULATION_MODE': True,
    'NAV_HEADING_KP': 0.6,        # Increased for faster heading changes
    'NAV_HEADING_KD': 1.0,         # Increased damping
    'NAV_DISTANCE_KP': 3.0,        # Increased for faster approach to targets
    'NAV_DISTANCE_KD': 0.8,        # Increased damping
    'NAV_HEADING_THRESHOLD_DEG': 5.0,  # Slightly looser for racing
    'NAV_DISTANCE_THRESHOLD_M': 1.5,   # Slightly larger arrival zone
    'NAV_POLL_INTERVAL': 0.1,

    # Camera
    'CAMERA_ROLL_FACTOR': -0.115,
    'CAMERA_PITCH_FACTOR': -0.1,
    'CAMERA_MANUAL_INCREMENT': 0.05,  # Radians per keypress
    'CAMERA_PITCH_STABILIZATION_GAIN': -0.8,  # Inverted pitch compensation (negative = counter drone pitch)
    'CAMERA_STABILIZATION_SMOOTHING': 0.3,  # Seconds to smooth camera movements
}

# World Objects (from mavic_2_pro.wbt)
WORLD_OBJECTS = {
    'car': {
        'type': 'TeslaModel3Simple',
        'position': {'x': -41.5139, 'y': 4.34169, 'z': 0.31},
        'rotation': {'x': 0, 'y': 0, 'z': 1, 'angle': -0.2618053071795865}
    },
    'chair': {
        'type': 'OfficeChair',
        'position': {'x': -25.44, 'y': -2.95, 'z': 0},
        'rotation': {'x': 0, 'y': 0, 'z': 1, 'angle': 0}
    },
    'human': {
        'type': 'Pedestrian',
        'position': {'x': -8.89, 'y': -6.67, 'z': 1.27},
        'rotation': {'x': 0, 'y': 0, 'z': 1, 'angle': 1.5708}
    },
    'cabinet': {
        'type': 'Cabinet',
        'position': {'x': -31.795, 'y': 13.8306, 'z': 0},
        'rotation': {'x': 0, 'y': 0, 'z': 1, 'angle': -2.094395307179586}
    },
    'cardboard_box': {
        'type': 'CardboardBox',
        'position': {'x': -0.730157, 'y': -1.22891, 'z': 0.3},
        'rotation': {'x': 0, 'y': 0, 'z': 1, 'angle': -1.8325953071795862}
    },
    'manhole': {
        'type': 'SquareManhole',
        'position': {'x': 0, 'y': 0, 'z': -0.03},
        'rotation': {'x': 0, 'y': 0, 'z': 1, 'angle': 0}
    },
    'forklift': {
        'type': 'Forklift',
        'position': {'x': -17.03, 'y': -8.12, 'z': 0.81},
        'rotation': {'x': 0, 'y': 0, 'z': 1, 'angle': 0}
    }
}


class DroneState:
    """Tracks comprehensive drone state"""

    def __init__(self):
        # Home position (starting position)
        self.home_position = CONFIG['HOME_POSITION'].copy()

        # Current position (x, y, z in meters)
        self.position = {'x': 0.0, 'y': 0.0, 'z': 0.0}

        # Orientation (roll, pitch, yaw in radians)
        self.orientation = {'roll': 0.0, 'pitch': 0.0, 'yaw': 0.0}

        # Angular velocity (roll, pitch, yaw rates in rad/s)
        self.angular_velocity = {'roll': 0.0, 'pitch': 0.0, 'yaw': 0.0}

        # Linear velocity (x, y, z in m/s) - computed from position changes
        self.velocity = {'x': 0.0, 'y': 0.0, 'z': 0.0}

        # Linear acceleration (x, y, z in m/s²) - computed from velocity changes
        self.acceleration = {'x': 0.0, 'y': 0.0, 'z': 0.0}

        # Previous values for derivative calculations
        self._prev_position = {'x': 0.0, 'y': 0.0, 'z': 0.0}
        self._prev_velocity = {'x': 0.0, 'y': 0.0, 'z': 0.0}
        self._prev_time = 0.0

        # Flight mode
        self.mode = 'MANUAL'  # MANUAL, AUTO, RETURNING_HOME, LANDING, TAKEOFF
        self.target_altitude = CONFIG['TAKEOFF_ALTITUDE']

        

    def update(self, x, y, z, roll, pitch, yaw, roll_vel, pitch_vel, yaw_vel, current_time):
        """Update drone state with current sensor readings"""
        # Update position
        self.position['x'] = x
        self.position['y'] = y
        self.position['z'] = z

        # Update orientation
        self.orientation['roll'] = roll
        self.orientation['pitch'] = pitch
        self.orientation['yaw'] = yaw

        # Update angular velocity
        self.angular_velocity['roll'] = roll_vel
        self.angular_velocity['pitch'] = pitch_vel
        self.angular_velocity['yaw'] = yaw_vel

        # Calculate linear velocity (if enough time has passed)
        dt = current_time - self._prev_time
        if dt > 0.001:  # Avoid division by zero
            self.velocity['x'] = (x - self._prev_position['x']) / dt
            self.velocity['y'] = (y - self._prev_position['y']) / dt
            self.velocity['z'] = (z - self._prev_position['z']) / dt

            # Calculate acceleration
            self.acceleration['x'] = (self.velocity['x'] - self._prev_velocity['x']) / dt
            self.acceleration['y'] = (self.velocity['y'] - self._prev_velocity['y']) / dt
            self.acceleration['z'] = (self.velocity['z'] - self._prev_velocity['z']) / dt

            # Update previous values
            self._prev_position = self.position.copy()
            self._prev_velocity = self.velocity.copy()
            self._prev_time = current_time

    def distance_to_home(self):
        """Calculate distance from current position to home"""
        dx = self.position['x'] - self.home_position['x']
        dy = self.position['y'] - self.home_position['y']
        dz = self.position['z'] - self.home_position['z']
        return (dx**2 + dy**2 + dz**2)**0.5

    def get_state_dict(self):
        """Get complete state as dictionary for logging/publishing"""
        return {
            'position': self.position.copy(),
            'orientation': self.orientation.copy(),
            'angular_velocity': self.angular_velocity.copy(),
            'velocity': self.velocity.copy(),
            'acceleration': self.acceleration.copy(),
            'mode': self.mode,
            'target_altitude': self.target_altitude,
            'distance_to_home': self.distance_to_home()
        }


class VideoPublisher:
    """Encodes video frames and publishes to hub via HTTP POST"""

    def __init__(self, hub_url):
        self.hub_url = hub_url
        self.encoder_thread = None
        self.running = False
        self.frame_queue = queue.Queue(maxsize=2)
        self.last_publish_time = 0
        self.publish_interval = 0.033  # 30 FPS max

    def start(self):
        """Start the encoding and publishing thread"""
        self.running = True

        # Start encoding thread
        self.encoder_thread = threading.Thread(target=self._encoding_loop, daemon=True)
        self.encoder_thread.start()

        print(f"Video publisher started - sending to {self.hub_url}/drone/video")

    def queue_frame(self, image_data, width, height):
        """Queue a frame for encoding (non-blocking, called from main loop)"""
        if not self.running:
            return

        try:
            # Non-blocking put - drops frame if queue full
            self.frame_queue.put_nowait((image_data, width, height))
        except queue.Full:
            pass  # Drop frame if encoder is behind

    def _encoding_loop(self):
        """Background thread that encodes and publishes frames"""
        while self.running:
            try:
                # Get frame from queue with timeout
                image_data, width, height = self.frame_queue.get(timeout=0.1)

                if Image is None:
                    continue

                # Rate limit publishing
                current_time = time.time()
                if current_time - self.last_publish_time < self.publish_interval:
                    continue

                # Convert BGRA to RGB
                img = Image.frombytes('RGBA', (width, height), image_data)
                img = img.convert('RGB')

                # Encode as JPEG
                buffer = io.BytesIO()
                img.save(buffer, format='JPEG', quality=85)
                jpeg_bytes = buffer.getvalue()

                # Publish to hub using persistent session
                try:
                    _http_session.post(
                        f"{self.hub_url}/drone/video",
                        json={
                            'data': base64.b64encode(jpeg_bytes).decode('ascii'),
                            'timestamp': current_time,
                            'width': width,
                            'height': height
                        },
                        timeout=0.01  # 10ms timeout
                    )
                    self.last_publish_time = current_time
                except requests.exceptions.RequestException:
                    pass  # Silently fail if hub not available

            except queue.Empty:
                continue
            except Exception as e:
                print(f"Error encoding/publishing frame: {e}")

    def stop(self):
        """Stop the encoding thread"""
        self.running = False

        if self.encoder_thread and self.encoder_thread.is_alive():
            self.encoder_thread.join(timeout=1)

        print("Video publisher stopped")


class ROS2Bridge:
    """Handle WebSocket connection to ROS2 rosbridge"""

    def __init__(self, host='localhost', port=9090):
        self.host = host
        self.port = port
        self.ws = None
        self.connected = False
        self.receive_thread = None
        self.running = False

    def connect(self):
        """Connect to rosbridge WebSocket server"""
        if websocket is None:
            print("Cannot connect to ROS2: websocket-client not installed")
            return False

        try:
            url = f"ws://{self.host}:{self.port}"
            print(f"Connecting to ROS2 rosbridge at {url}...")
            self.ws = websocket.WebSocketApp(
                url,
                on_open=self._on_open,
                on_message=self._on_message,
                on_error=self._on_error,
                on_close=self._on_close
            )

            self.running = True
            self.receive_thread = threading.Thread(target=self.ws.run_forever, daemon=True)
            self.receive_thread.start()

            # Wait for connection
            timeout = 5
            start_time = time.time()
            while not self.connected and (time.time() - start_time) < timeout:
                time.sleep(0.1)

            return self.connected

        except Exception as e:
            print(f"Failed to connect to ROS2: {e}")
            return False

    def _on_open(self, ws):
        """Callback when WebSocket connection opens"""
        self.connected = True
        print("Connected to ROS2 rosbridge")

    def _on_message(self, ws, message):
        """Callback when message received from ROS2"""
        try:
            data = json.loads(message)
            # Handle incoming ROS2 messages here
            if data.get('op') == 'publish':
                topic = data.get('topic')
                msg = data.get('msg')
                print(f"Received from {topic}: {msg}")
        except Exception as e:
            print(f"Error processing message: {e}")

    def _on_error(self, ws, error):
        """Callback when WebSocket error occurs"""
        print(f"ROS2 connection error: {error}")

    def _on_close(self, ws, close_status_code, close_msg):
        """Callback when WebSocket connection closes"""
        self.connected = False
        print("Disconnected from ROS2 rosbridge")

    def advertise(self, topic, msg_type):
        """Advertise a ROS2 topic for publishing"""
        if not self.connected or not self.ws:
            return False

        msg = {
            "op": "advertise",
            "topic": topic,
            "type": msg_type
        }
        try:
            self.ws.send(json.dumps(msg))
            return True
        except Exception as e:
            print(f"Failed to advertise topic: {e}")
            return False

    def publish(self, topic, msg):
        """Publish a message to a ROS2 topic"""
        if not self.connected or not self.ws:
            return False

        message = {
            "op": "publish",
            "topic": topic,
            "msg": msg
        }
        try:
            self.ws.send(json.dumps(message))
            return True
        except Exception as e:
            print(f"Failed to publish: {e}")
            return False

    def subscribe(self, topic, msg_type):
        """Subscribe to a ROS2 topic"""
        if not self.connected or not self.ws:
            return False

        msg = {
            "op": "subscribe",
            "topic": topic,
            "type": msg_type
        }
        try:
            self.ws.send(json.dumps(msg))
            return True
        except Exception as e:
            print(f"Failed to subscribe: {e}")
            return False

    def disconnect(self):
        """Disconnect from rosbridge"""
        self.running = False
        if self.ws:
            self.ws.close()


class Mavic2ProROS2Controller(Robot):
    def __init__(self):
        Robot.__init__(self)

        self.time_step = int(self.getBasicTimeStep())

        # Initialize drone state tracker
        self.state = DroneState()

        # Initialize devices
        self.camera = self.getDevice("camera")
        self.camera.enable(self.time_step)

        self.front_left_led = self.getDevice("front left led")
        self.front_right_led = self.getDevice("front right led")

        self.imu = self.getDevice("inertial unit")
        self.imu.enable(self.time_step)

        self.gps = self.getDevice("gps")
        self.gps.enable(self.time_step)

        self.compass = self.getDevice("compass")
        self.compass.enable(self.time_step)

        self.gyro = self.getDevice("gyro")
        self.gyro.enable(self.time_step)

        self.keyboard = self.getKeyboard()
        self.keyboard.enable(self.time_step)

        self.camera_roll_motor = self.getDevice("camera roll")
        self.camera_pitch_motor = self.getDevice("camera pitch")

        # Camera manual control state
        self.camera_manual_roll = 0.0
        self.camera_manual_pitch = 0.0
        self.camera_stabilization_enabled = True

        # Camera smoothed pitch stabilization
        self.camera_target_pitch = 0.0
        self.camera_current_pitch = 0.0

        # Smooth disturbance control (current and target values)
        self.current_roll_disturbance = 0.0
        self.current_pitch_disturbance = 0.0
        self.current_yaw_disturbance = 0.0
        self.target_roll_disturbance = 0.0
        self.target_pitch_disturbance = 0.0
        self.target_yaw_disturbance = 0.0

        # PID derivative tracking
        self.prev_altitude = 0.0

        # PID Controllers
        self.altitude_controller = AltitudeController(
            k_p=CONFIG['K_VERTICAL_P'],
            k_d=CONFIG['K_VERTICAL_D'],
            offset=CONFIG['K_VERTICAL_OFFSET']
        )

        self.yaw_controller = YawController(
            kp=CONFIG['NAV_HEADING_KP'],
            kd=CONFIG['NAV_HEADING_KD'],
            max_yaw=abs(CONFIG['YAW_DISTURBANCE_LEFT'])
        )

        self.movement_controller = MovementController(
            kp=CONFIG['NAV_DISTANCE_KP'],
            ki=0.05,
            kd=CONFIG['NAV_DISTANCE_KD'],
            max_pitch=abs(CONFIG['PITCH_DISTURBANCE_FORWARD']),
            max_roll=abs(CONFIG['ROLL_DISTURBANCE_RIGHT']),
            distance_threshold=CONFIG['NAV_DISTANCE_THRESHOLD_M']
        )

        # Navigation state
        self.autonomous_mode = False
        self.navigation_target = None  # Legacy delta-based (deprecated)
        self.object_absolute_position = None  # World position of target object
        self.navigation_state = 'IDLE'  # States: IDLE, SEARCHING, FACING, NAVIGATING
        self.target_locked = False  # True once coordinates received

        self.joystick_yaw = 0
        self.joystick_pitch = 0
        self.joystick_roll = 0
        self.use_joystick = False

        self.global_intent = None
        self.global_object = None
        self.last_hud_object = None

        self.front_left_motor = self.getDevice("front left propeller")
        self.front_right_motor = self.getDevice("front right propeller")
        self.rear_left_motor = self.getDevice("rear left propeller")
        self.rear_right_motor = self.getDevice("rear right propeller")

        motors = [self.front_left_motor, self.front_right_motor,
                  self.rear_left_motor, self.rear_right_motor]
        for motor in motors:
            motor.setPosition(float('inf'))
            motor.setVelocity(1.0)

        # Initialize ROS2 bridge
        self.ros2 = ROS2Bridge(host=CONFIG['ROS2_HOST'], port=CONFIG['ROS2_PORT'])
        self.ros2_connected = False

        # Initialize video publisher
        self.video_publisher = VideoPublisher(hub_url=CONFIG['HUB_URL'])

    def go_home(self):
        """Command drone to return to home position and land"""
        print("Returning home...")
        self.state.mode = 'RETURNING_HOME'
        # Target position will be home
        # Will transition to LANDING when close to home

    def land(self):
        """Command drone to land at current position"""
        print("Landing...")
        self.state.mode = 'LANDING'
        self.state.target_altitude = 0.0

    def takeoff(self, altitude=None):
        """Command drone to take off to specified altitude"""
        if altitude is None:
            altitude = CONFIG['TAKEOFF_ALTITUDE']
        print(f"Taking off to {altitude}m...")
        self.state.mode = 'TAKEOFF'
        self.state.target_altitude = altitude

    def setup_ros2_topics(self):
        """Setup ROS2 topics for publishing drone state"""
        if not self.ros2_connected:
            return

        # Advertise topics
        self.ros2.advertise("/drone/pose", "geometry_msgs/PoseStamped")
        self.ros2.advertise("/drone/velocity", "geometry_msgs/TwistStamped")
        self.ros2.advertise("/drone/acceleration", "geometry_msgs/AccelStamped")
        self.ros2.advertise("/drone/altitude", "std_msgs/Float64")
        self.ros2.advertise("/drone/status", "std_msgs/String")
        self.ros2.advertise("/camera/image_raw", "sensor_msgs/Image")

        print("ROS2 topics advertised")

    def publish_camera_image(self):
        """Publish camera image to ROS2"""
        if not self.ros2_connected:
            return

        # Get camera image
        image = self.camera.getImage()
        if image is None:
            return

        width = self.camera.getWidth()
        height = self.camera.getHeight()

        # Convert BGRA to RGB and encode as base64
        image_bytes = bytes(image)
        image_data = base64.b64encode(image_bytes).decode('utf-8')

        # Create ROS2 Image message
        image_msg = {
            "header": {
                "stamp": {"sec": int(self.getTime()), "nanosec": 0},
                "frame_id": "camera"
            },
            "height": height,
            "width": width,
            "encoding": "bgra8",
            "is_bigendian": 0,
            "step": width * 4,
            "data": image_data
        }
        self.ros2.publish("/camera/image_raw", image_msg)

    def publish_drone_state(self, x_pos, y_pos, altitude, roll, pitch, yaw,
                           roll_velocity, pitch_velocity, yaw_velocity):
        """Publish current drone state to ROS2"""
        if not self.ros2_connected:
            return

        # Publish pose
        pose_msg = {
            "header": {
                "stamp": {"sec": int(self.getTime()), "nanosec": 0},
                "frame_id": "world"
            },
            "pose": {
                "position": {"x": x_pos, "y": y_pos, "z": altitude},
                "orientation": {"x": roll, "y": pitch, "z": yaw, "w": 1.0}
            }
        }
        self.ros2.publish("/drone/pose", pose_msg)

        # Publish velocity (with computed linear velocity from state)
        velocity_msg = {
            "header": {
                "stamp": {"sec": int(self.getTime()), "nanosec": 0},
                "frame_id": "world"
            },
            "twist": {
                "linear": {
                    "x": self.state.velocity['x'],
                    "y": self.state.velocity['y'],
                    "z": self.state.velocity['z']
                },
                "angular": {"x": roll_velocity, "y": pitch_velocity, "z": yaw_velocity}
            }
        }
        self.ros2.publish("/drone/velocity", velocity_msg)

        # Publish acceleration (computed from velocity changes)
        acceleration_msg = {
            "header": {
                "stamp": {"sec": int(self.getTime()), "nanosec": 0},
                "frame_id": "world"
            },
            "accel": {
                "linear": {
                    "x": self.state.acceleration['x'],
                    "y": self.state.acceleration['y'],
                    "z": self.state.acceleration['z']
                },
                "angular": {"x": 0.0, "y": 0.0, "z": 0.0}
            }
        }
        self.ros2.publish("/drone/acceleration", acceleration_msg)

        # Publish altitude
        altitude_msg = {"data": altitude}
        self.ros2.publish("/drone/altitude", altitude_msg)

        # Compute facing vector from yaw (forward direction in XY plane)
        import math
        facing_x = math.cos(yaw)
        facing_y = math.sin(yaw)
        facing_z = 0.0  # Assuming level flight for forward direction

        # Publish status (flight mode, distance to home, facing vector, etc.)
        status_msg = {
            "data": f"Mode: {self.state.mode} | "
                   f"Alt: {altitude:.2f}m | "
                   f"Home Dist: {self.state.distance_to_home():.2f}m | "
                   f"Target Alt: {self.state.target_altitude:.2f}m | "
                   f"Facing: ({facing_x:.2f}, {facing_y:.2f}, {facing_z:.2f})"
        }
        self.ros2.publish("/drone/status", status_msg)

    def publish_to_hub(self):
        """Publish complete drone state to Nimbus Hub (fast HTTP)"""
        try:
            # Compute facing vector from yaw
            import math
            yaw = self.state.orientation['yaw']
            facing_x = math.cos(yaw)
            facing_y = math.sin(yaw)
            facing_z = 0.0

            # Build complete state dictionary
            state_dict = {
                'position': self.state.position.copy(),
                'orientation': self.state.orientation.copy(),
                'velocity': self.state.velocity.copy(),
                'acceleration': self.state.acceleration.copy(),
                'angular_velocity': self.state.angular_velocity.copy(),
                'facing_vector': {'x': facing_x, 'y': facing_y, 'z': facing_z},
                'mode': self.state.mode,
                'target_altitude': self.state.target_altitude,
                'distance_to_home': self.state.distance_to_home(),
                'connected': True,
                'timestamp': self.getTime(),
                'world_objects': WORLD_OBJECTS,  # Ground truth object positions for TRIG depth method
                'simulation': CONFIG['SIMULATION_MODE'],
                'autonomous_mode': self.autonomous_mode,
                'navigation_target': self.navigation_target,
                'camera_config': {
                    'fov': 0.7854,
                    'width': self.camera.getWidth(),
                    'height': self.camera.getHeight()
                }
            }

            # Non-blocking POST to hub using persistent session
            _http_session.post(
                f"{CONFIG['HUB_URL']}/drone/state",
                json=state_dict,
                timeout=0.005  # 5ms timeout
            )

        except Exception as e:
            # Silently fail - don't block control loop
            pass

    def update_hud_message(self, message):
        try:
            _http_session.post(
                "http://127.0.0.1:8000/hud/message",
                json={"message": message},
                timeout=1
            )
            print("[HUD] Sent "+message+" message.")
        except Exception as e:
            print(f"[HUD] Failed to send HUD message: {e}")

    def poll_autonomous_mode_trigger(self):
        """Check if autonomous mode should be enabled via voice command"""
        try:
            response = _http_session.get(
                f"{CONFIG['HUB_URL']}/api/autonomous_mode_trigger",
                timeout=0.01
            )
            if response.status_code == 200:
                data = response.json()
                if data.get('trigger', False):
                    self.autonomous_mode = True
                    self.navigation_state = 'SEARCHING'
                    self.target_locked = False
                    print("[AUTONOMOUS MODE: ENABLED via voice command - SEARCHING]")
                    # Clear the trigger
                    _http_session.post(f"{CONFIG['HUB_URL']}/api/clear_autonomous_trigger", timeout=0.01)
        except:
            pass

    def poll_navigation_target(self):
        """Poll hub for object absolute position from AI"""
        try:
            response = _http_session.get(
                f"{CONFIG['HUB_URL']}/navigation/object_position",
                timeout=0.01
            )
            if response.status_code == 200:
                data = response.json()
                if data.get('has_position') and data.get('object_position'):
                    new_object = data['object_name']

                    # If object changed, clear old position and unlock target
                    if self.global_object and new_object != self.global_object:
                        print(f"[AUTO] Object changed from '{self.global_object}' to '{new_object}' - resetting")
                        self.object_absolute_position = None
                        self.target_locked = False
                        self.navigation_state = 'SEARCHING'

                    # Track when coordinates are first received
                    if not self.target_locked:
                        self.target_locked = True
                        self.navigation_state = 'FACING'
                        print(f"[AUTO] Target locked at position: {data['object_position']}")

                    self.object_absolute_position = data['object_position']
                    self.global_intent = data['intent']
                    self.global_object = new_object
                else:
                    # Only clear position if we haven't locked target yet
                    if not self.target_locked:
                        self.object_absolute_position = None
                    # If target_locked = True, keep the coordinates even if detection fails
        except:
            pass

    def poll_joystick_yaw(self):
        """Poll hub for joystick yaw"""
        try:
            response = _http_session.get(
                f"{CONFIG['HUB_URL']}/api/mr/rotation",
                timeout=0.01
            )
            if response.status_code == 200:
                data = response.json()
                self.joystick_yaw = data.get('yaw')
            else:
                self.joystick_yaw = 0
        except:
            pass

    def poll_joystick_pitch_roll(self):
        """Poll hub for joystick pitch and roll"""
        try:
            response = _http_session.get(
                f"{CONFIG['HUB_URL']}/api/mr/joystick",
                timeout=0.01
            )
            if response.status_code == 200:
                data = response.json()
                self.joystick_pitch = data.get('pitch')
                self.joystick_roll = data.get('roll')
            else:
                self.joystick_pitch = 0
                self.joystick_roll = 0
                print(response.status_code)
        except:
            pass

    def get_navigation_disturbances(self):
        """
        State machine for autonomous navigation:
        - SEARCHING: Rotate slowly until object detected and coordinates received
        - FACING: Yaw to face object within 3 degrees
        - NAVIGATING: Normal PID navigation to target
        """
        if not self.autonomous_mode:
            return 0.0, 0.0, 0.0

        import math

        # STATE 1: SEARCHING - No coordinates yet, slowly rotate to find object
        if self.navigation_state == 'SEARCHING':
            if not self.object_absolute_position:
                # Slowly yaw left until object is detected and coordinates calculated
                print("[AUTO] SEARCHING for object...")
                return -0.3, 0.0, 0.0
            else:
                # Coordinates received, transition to FACING
                self.navigation_state = 'FACING'
                self.target_locked = True
                print("[AUTO] Target found! Transitioning to FACING mode")

        # From here on, we ALWAYS have object_absolute_position (locked target)
        if not self.object_absolute_position:
            return 0.0, 0.0, 0.0

        # Calculate relative position to object
        obj_x = self.object_absolute_position['x']
        obj_y = self.object_absolute_position['y']

        drone_x = self.state.position['x']
        drone_y = self.state.position['y']
        drone_yaw = self.state.orientation['yaw']

        world_dx = obj_x - drone_x
        world_dy = obj_y - drone_y

        # Transform to drone frame
        delta_x = world_dx * math.cos(drone_yaw) + world_dy * math.sin(drone_yaw)
        delta_y = -world_dx * math.sin(drone_yaw) + world_dy * math.cos(drone_yaw)

        # Calculate heading error
        heading_error_rad = math.atan2(delta_y, delta_x)
        heading_error_deg = math.degrees(heading_error_rad)

        # Get controller inputs
        world_vx = self.state.velocity['x']
        world_vy = self.state.velocity['y']
        velocity_x = world_vx * math.cos(drone_yaw) + world_vy * math.sin(drone_yaw)
        velocity_y = -world_vx * math.sin(drone_yaw) + world_vy * math.cos(drone_yaw)

        accel_x = self.state.acceleration['x']
        accel_y = self.state.acceleration['y']
        accel_z = self.state.acceleration['z']
        accel_magnitude = math.sqrt(accel_x**2 + accel_y**2 + accel_z**2)

        yaw_velocity = self.state.angular_velocity['yaw']
        dt = self.time_step / 1000.0

        # STATE 2: FACING - Object in view but not facing, yaw only (no movement)
        if self.navigation_state == 'FACING':
            if abs(heading_error_deg) > 3.0:
                # Still turning to face object
                yaw_disturbance = self.yaw_controller.update(delta_x, delta_y, yaw_velocity, dt)
                print(f"[AUTO] FACING object... heading error: {heading_error_deg:+.1f} deg")
                return yaw_disturbance, 0.0, 0.0
            else:
                # Now facing object, transition to NAVIGATING
                self.navigation_state = 'NAVIGATING'
                print("[AUTO] Object facing! Transitioning to NAVIGATING mode")

        # STATE 3: NAVIGATING - Normal PID navigation (existing behavior)
        if self.navigation_state == 'NAVIGATING':
            distance = math.sqrt(delta_x**2 + delta_y**2)
            velocity_magnitude = math.sqrt(velocity_x**2 + velocity_y**2)

            yaw_disturbance = self.yaw_controller.update(delta_x, delta_y, yaw_velocity, dt)
            pitch_disturbance, roll_disturbance, at_target = self.movement_controller.update(
                delta_x, delta_y, velocity_x, velocity_y, accel_magnitude, dt
            )

            if at_target:
                print(f"[AUTO] Arrived at target!")
                self.autonomous_mode = False
                self.navigation_state = 'IDLE'
                self.target_locked = False
                self.update_hud_message("Arrived at target!")
            else:
                print(f"[AUTO] NAVIGATING - Dist: {distance:.2f}m, Heading: {heading_error_deg:+.1f}deg, Vel: {velocity_magnitude:.2f}m/s")

            return yaw_disturbance, pitch_disturbance, roll_disturbance

        # Default: no disturbances
        return 0.0, 0.0, 0.0

    def run(self):
        print("=" * 60)
        print("MAVIC 2 PRO - ROS2 CONTROLLER")
        print("=" * 60)
        print(f"Home Position: ({CONFIG['HOME_POSITION']['x']}, "
              f"{CONFIG['HOME_POSITION']['y']}, {CONFIG['HOME_POSITION']['z']})")
        print(f"Takeoff Altitude: {CONFIG['TAKEOFF_ALTITUDE']}m")
        print(f"World Objects Loaded: {len(WORLD_OBJECTS)}")
        for obj_name, obj_data in WORLD_OBJECTS.items():
            pos = obj_data['position']
            print(f"  - {obj_name}: ({pos['x']:.2f}, {pos['y']:.2f}, {pos['z']:.2f})")
        print("=" * 60)

        # Start video publisher
        self.video_publisher.start()

        # Try to connect to ROS2
        print("Attempting to connect to ROS2 container...")
        self.ros2_connected = self.ros2.connect()

        if self.ros2_connected:
            self.setup_ros2_topics()
        else:
            print("Warning: Running without ROS2 connection")

        while self.step(self.time_step) != -1:
            if self.getTime() > 1.0:
                break

        print("You can control the drone with your computer keyboard:")
        print("- Arrow Up: move forward")
        print("- Arrow Down: move backward")
        print("- Arrow Right: strafe right")
        print("- Arrow Left: strafe left")
        print("- Numpad 1: decrease altitude")
        print("- Numpad 3: increase altitude")
        print("- Numpad 7: yaw left")
        print("- Numpad 9: yaw right")
        print("- Numpad 2: camera tilt down")
        print("- Numpad 8: camera tilt up")
        print("- Numpad 4: camera pan left")
        print("- Numpad 6: camera pan right")
        print("- H: return home and land")
        print("- L: land at current position")
        print("- A: toggle autonomous object tracking")
        print("- J: enable joystick controls")
        print("- K: disables joystick controls")

        last_publish_time = 0
        last_hub_publish_time = 0
        last_nav_poll_time = 0
        publish_interval = CONFIG['ROS2_PUBLISH_INTERVAL']
        hub_publish_interval = CONFIG['HUB_PUBLISH_INTERVAL']
        nav_poll_interval = CONFIG['NAV_POLL_INTERVAL']

        while self.step(self.time_step) != -1:
            time_now = self.getTime()

            # Read sensor data
            roll, pitch, yaw = self.imu.getRollPitchYaw()
            x_pos, y_pos, altitude = self.gps.getValues()
            roll_velocity, pitch_velocity, yaw_velocity = self.gyro.getValues()

            # Update drone state
            self.state.update(x_pos, y_pos, altitude, roll, pitch, yaw,
                            roll_velocity, pitch_velocity, yaw_velocity, time_now)

            # LED blinking
            led_state = int(time_now) % 2
            self.front_left_led.set(1 if led_state else 0)
            self.front_right_led.set(0 if led_state else 1)

            # Camera control with inverted pitch stabilization
            dt = self.time_step / 1000.0

            if self.camera_stabilization_enabled:
                # Calculate target pitch: invert drone pitch + velocity damping + manual offset
                self.camera_target_pitch = (
                    CONFIG['CAMERA_PITCH_STABILIZATION_GAIN'] * pitch +  # Invert drone pitch angle
                    CONFIG['CAMERA_PITCH_FACTOR'] * pitch_velocity +      # Velocity damping
                    self.camera_manual_pitch                              # Manual adjustment
                )

                # Smooth interpolation toward target pitch
                smoothing_rate = 1.0 / CONFIG['CAMERA_STABILIZATION_SMOOTHING']
                max_pitch_change = smoothing_rate * dt

                pitch_diff = self.camera_target_pitch - self.camera_current_pitch
                if abs(pitch_diff) <= max_pitch_change:
                    self.camera_current_pitch = self.camera_target_pitch
                else:
                    self.camera_current_pitch += max_pitch_change if pitch_diff > 0 else -max_pitch_change

                # Apply smoothed pitch and roll stabilization
                self.camera_pitch_motor.setPosition(self.camera_current_pitch)
                self.camera_roll_motor.setPosition(
                    CONFIG['CAMERA_ROLL_FACTOR'] * roll_velocity + self.camera_manual_roll
                )
            else:
                # Pure manual control
                self.camera_roll_motor.setPosition(self.camera_manual_roll)
                self.camera_pitch_motor.setPosition(self.camera_manual_pitch)

            # Get navigation disturbances if autonomous
            nav_yaw, nav_pitch, nav_roll = self.get_navigation_disturbances()

            # Handle keyboard input - set target disturbances
            self.target_roll_disturbance = 0.0
            self.target_pitch_disturbance = 0.0
            self.target_yaw_disturbance = 0.0

            key = self.keyboard.getKey()
            while key >= 0:
                # Arrow keys - Strafe controls
                if key == Keyboard.UP:
                    self.target_pitch_disturbance = CONFIG['PITCH_DISTURBANCE_FORWARD']
                elif key == Keyboard.DOWN:
                    self.target_pitch_disturbance = CONFIG['PITCH_DISTURBANCE_BACKWARD']
                elif key == Keyboard.RIGHT:
                    self.target_roll_disturbance = CONFIG['ROLL_DISTURBANCE_RIGHT']
                elif key == Keyboard.LEFT:
                    self.target_roll_disturbance = CONFIG['ROLL_DISTURBANCE_LEFT']

                # Numpad altitude control (multiple key codes for NumLock on/off)
                elif key == 321 or key == ord('1'):  # Numpad 1
                    self.state.target_altitude -= CONFIG['ALTITUDE_INCREMENT']
                    print(f"Target altitude: {self.state.target_altitude:.2f}m")
                elif key == 323 or key == ord('3'):  # Numpad 3
                    self.state.target_altitude += CONFIG['ALTITUDE_INCREMENT']
                    print(f"Target altitude: {self.state.target_altitude:.2f}m")

                # Numpad yaw control
                elif key == 327 or key == ord('7'):  # Numpad 7
                    self.target_yaw_disturbance = CONFIG['YAW_DISTURBANCE_LEFT']
                elif key == 329 or key == ord('9'):  # Numpad 9
                    self.target_yaw_disturbance = CONFIG['YAW_DISTURBANCE_RIGHT']

                # Numpad camera pitch control
                elif key == 322 or key == ord('2'):  # Numpad 2
                    self.camera_manual_pitch -= CONFIG['CAMERA_MANUAL_INCREMENT']
                    print(f"Camera pitch: {self.camera_manual_pitch:.3f} rad")
                elif key == 328 or key == ord('8'):  # Numpad 8
                    self.camera_manual_pitch += CONFIG['CAMERA_MANUAL_INCREMENT']
                    print(f"Camera pitch: {self.camera_manual_pitch:.3f} rad")

                # Numpad camera roll control
                elif key == 324 or key == ord('4'):  # Numpad 4
                    self.camera_manual_roll += CONFIG['CAMERA_MANUAL_INCREMENT']
                    print(f"Camera roll: {self.camera_manual_roll:.3f} rad")
                elif key == 326 or key == ord('6'):  # Numpad 6
                    self.camera_manual_roll -= CONFIG['CAMERA_MANUAL_INCREMENT']
                    print(f"Camera roll: {self.camera_manual_roll:.3f} rad")

                elif key == ord('j') or key == ord('J'):
                    self.use_joystick = True
                    print("use joystick true")
                elif key == ord('k') or key == ord('K'):
                    self.use_joystick = False
                    print("use joystick false")

                # Debug: print unknown keys
                elif key not in [Keyboard.UP, Keyboard.DOWN, Keyboard.LEFT, Keyboard.RIGHT, ord('j'), ord('k')]:
                    if key > 0 and key < 500:  # Reasonable key range
                        print(f"DEBUG: Unknown key pressed: {key}")

                # Other commands
                if key == ord('H'):
                    self.go_home()
                elif key == ord('L'):
                    self.land()
                elif key == ord('A'):
                    self.autonomous_mode = True
                    self.navigation_state = 'SEARCHING'
                    self.target_locked = False
                    print(f"[AUTONOMOUS MODE: ENABLED - SEARCHING]")
                elif key == ord('X'):
                    self.autonomous_mode = False
                    self.navigation_state = 'IDLE'
                    self.target_locked = False
                    self.object_absolute_position = None
                    print(f"[AUTONOMOUS MODE: DISABLED]")

                key = self.keyboard.getKey()

            # Yaw from joystick 
            if self.use_joystick:
                self.poll_joystick_yaw()
                self.poll_joystick_pitch_roll()

                if self.joystick_yaw == 0:
                    if self.target_yaw_disturbance != 0:
                        self.target_yaw_disturbance = 0
                elif self.joystick_yaw < 0:
                    self.target_yaw_disturbance = CONFIG['YAW_DISTURBANCE_LEFT'] * abs(self.joystick_yaw)
                elif self.joystick_yaw > 0:
                    self.target_yaw_disturbance = CONFIG['YAW_DISTURBANCE_RIGHT'] * self.joystick_yaw

                if self.joystick_pitch == 0:
                    if self.target_pitch_disturbance != 0:
                        self.target_pitch_disturbance = 0
                elif self.joystick_pitch < 0:
                    self.target_pitch_disturbance = CONFIG['PITCH_DISTURBANCE_BACKWARD'] * abs(self.joystick_pitch)
                elif self.joystick_pitch > 0:
                    self.target_pitch_disturbance = CONFIG['PITCH_DISTURBANCE_FORWARD'] * self.joystick_pitch

                if self.joystick_roll == 0:
                    if self.target_roll_disturbance != 0:
                        self.target_roll_disturbance = 0
                elif self.joystick_roll < 0:
                    self.target_roll_disturbance = CONFIG['ROLL_DISTURBANCE_LEFT'] * abs(self.joystick_roll)
                elif self.joystick_roll > 0:
                    self.target_roll_disturbance = CONFIG['ROLL_DISTURBANCE_RIGHT'] * self.joystick_roll
                

            # Override with autonomous navigation if active
            if self.autonomous_mode and self.global_intent.lower() == 'go':
                self.target_yaw_disturbance = nav_yaw
                self.target_pitch_disturbance = nav_pitch
                self.target_roll_disturbance = nav_roll
                # Update HUD to show we are moving toward object
                if self.last_hud_object != self.global_object:
                    self.update_hud_message("Moving to "+self.global_object)
                    self.last_hud_object = self.global_object

            # Smooth ramping of disturbances (interpolate current toward target)
            dt = self.time_step / 1000.0  # Convert ms to seconds
            ramp_rate = 1.0 / CONFIG['DISTURBANCE_RAMP_TIME']  # Units per second

            # Calculate maximum change allowed this frame
            max_change = ramp_rate * dt

            # Ramp roll disturbance
            roll_diff = self.target_roll_disturbance - self.current_roll_disturbance
            if abs(roll_diff) <= max_change:
                self.current_roll_disturbance = self.target_roll_disturbance
            else:
                self.current_roll_disturbance += max_change if roll_diff > 0 else -max_change

            # Ramp pitch disturbance
            pitch_diff = self.target_pitch_disturbance - self.current_pitch_disturbance
            if abs(pitch_diff) <= max_change:
                self.current_pitch_disturbance = self.target_pitch_disturbance
            else:
                self.current_pitch_disturbance += max_change if pitch_diff > 0 else -max_change

            # Ramp yaw disturbance
            yaw_diff = self.target_yaw_disturbance - self.current_yaw_disturbance
            if abs(yaw_diff) <= max_change:
                self.current_yaw_disturbance = self.target_yaw_disturbance
            else:
                self.current_yaw_disturbance += max_change if yaw_diff > 0 else -max_change

            # Use smoothed values for control
            roll_disturbance = self.current_roll_disturbance
            pitch_disturbance = self.current_pitch_disturbance
            yaw_disturbance = self.current_yaw_disturbance

            # PID control with damping
            # Roll/Pitch: stronger P gain + velocity damping + disturbance input
            roll_input = CONFIG['K_ROLL_P'] * clamp(roll, -1.0, 1.0) + roll_velocity + roll_disturbance
            pitch_input = CONFIG['K_PITCH_P'] * clamp(pitch, -1.0, 1.0) + pitch_velocity + pitch_disturbance
            yaw_input = yaw_disturbance

            # Vertical: PD controller (P + D term for damping)
            vertical_input = self.altitude_controller.update(
                current_altitude=altitude,
                target_altitude=self.state.target_altitude,
                dt=self.time_step / 1000.0
            )

            # Motor control with differential limiting
            base_thrust = CONFIG['K_VERTICAL_THRUST'] + vertical_input

            # Calculate raw motor commands
            front_left_motor_input = base_thrust - roll_input + pitch_input - yaw_input
            front_right_motor_input = base_thrust + roll_input + pitch_input + yaw_input
            rear_left_motor_input = base_thrust - roll_input - pitch_input + yaw_input
            rear_right_motor_input = base_thrust + roll_input - pitch_input - yaw_input

            # Limit differential from base thrust to prevent overcorrection
            max_diff = CONFIG['MAX_MOTOR_DIFFERENTIAL']
            front_left_motor_input = clamp(front_left_motor_input, base_thrust - max_diff, base_thrust + max_diff)
            front_right_motor_input = clamp(front_right_motor_input, base_thrust - max_diff, base_thrust + max_diff)
            rear_left_motor_input = clamp(rear_left_motor_input, base_thrust - max_diff, base_thrust + max_diff)
            rear_right_motor_input = clamp(rear_right_motor_input, base_thrust - max_diff, base_thrust + max_diff)

            # Ensure motors stay within physical limits (positive values)
            front_left_motor_input = max(0.1, front_left_motor_input)
            front_right_motor_input = max(0.1, front_right_motor_input)
            rear_left_motor_input = max(0.1, rear_left_motor_input)
            rear_right_motor_input = max(0.1, rear_right_motor_input)

            self.front_left_motor.setVelocity(front_left_motor_input)
            self.front_right_motor.setVelocity(-front_right_motor_input)
            self.rear_left_motor.setVelocity(-rear_left_motor_input)
            self.rear_right_motor.setVelocity(rear_right_motor_input)

            # Queue camera frame for async encoding and publishing (non-blocking)
            image = self.camera.getImage()
            if image is not None:
                self.video_publisher.queue_frame(
                    bytes(image),
                    self.camera.getWidth(),
                    self.camera.getHeight()
                )

            # Publish to Nimbus Hub at high frequency (100Hz)
            if (time_now - last_hub_publish_time) >= hub_publish_interval:
                self.publish_to_hub()
                last_hub_publish_time = time_now

            # Poll navigation target from hub (10Hz)
            if (time_now - last_nav_poll_time) >= nav_poll_interval:
                self.poll_autonomous_mode_trigger()
                self.poll_navigation_target()
                last_nav_poll_time = time_now

            # Publish to ROS2 at lower frequency (30Hz) - for legacy systems
            if self.ros2_connected and (time_now - last_publish_time) >= publish_interval:
                self.publish_drone_state(x_pos, y_pos, altitude, roll, pitch, yaw,
                                        roll_velocity, pitch_velocity, yaw_velocity)
                self.publish_camera_image()
                last_publish_time = time_now

        # Cleanup
        self.video_publisher.stop()
        if self.ros2_connected:
            self.ros2.disconnect()


controller = Mavic2ProROS2Controller()
controller.run()