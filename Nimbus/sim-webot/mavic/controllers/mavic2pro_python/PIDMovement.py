"""
Movement Controller for Drone Navigation
2-axis control (pitch forward/back, roll left/right) with physics-based deceleration
"""

import math
from PID import PIDController, clamp


class MovementController:
    """
    Controls drone movement toward target using pitch and roll.
    Includes physics-based deceleration for smooth arrival.
    Runs continuously during navigation - no phase gating.
    """

    def __init__(self, kp, ki, kd, max_pitch=15.0, max_roll=15.0, distance_threshold=1.0):
        """
        Args:
            kp: Proportional gain for position error
            ki: Integral gain for steady-state error
            kd: Derivative gain (uses velocity for damping)
            max_pitch: Maximum pitch disturbance (degrees) - balanced for racing
            max_roll: Maximum roll disturbance (degrees) - balanced for racing
            distance_threshold: Distance to target considered "arrived" (meters)
        """
        self.pitch_pid = PIDController(kp, ki, kd, -max_pitch, max_pitch, integral_max=5.0)
        self.roll_pid = PIDController(kp * 0.5, ki * 0.4, kd * 0.8, -max_roll, max_roll, integral_max=3.0)

        self.max_pitch = max_pitch
        self.max_roll = max_roll
        self.distance_threshold = distance_threshold

        self.max_observed_accel = 0.5
        self.accel_samples = []

    def update(self, delta_x, delta_y, velocity_x, velocity_y, accel_magnitude, dt):
        """
        Calculate pitch and roll disturbances to move toward target

        Args:
            delta_x: Forward distance to target (m, drone frame)
            delta_y: Lateral distance to target (m, drone frame, positive = right)
            velocity_x: Forward velocity (m/s, drone frame)
            velocity_y: Lateral velocity (m/s, drone frame)
            accel_magnitude: Current acceleration magnitude (m/s^2)
            dt: Time delta (seconds)

        Returns:
            tuple: (pitch_disturbance, roll_disturbance, at_target)
                pitch_disturbance: Motor input for pitch (negative = forward)
                roll_disturbance: Motor input for roll (positive = right)
                at_target: Boolean indicating arrival
        """
        distance = math.sqrt(delta_x**2 + delta_y**2)
        velocity_magnitude = math.sqrt(velocity_x**2 + velocity_y**2)

        if accel_magnitude > self.max_observed_accel:
            self.accel_samples.append(accel_magnitude)
            if len(self.accel_samples) > 10:
                self.accel_samples.pop(0)
                self.max_observed_accel = max(self.accel_samples)

        at_target = distance < self.distance_threshold

        if at_target:
            return 0.0, 0.0, True

        # Racing mode: minimal deceleration, aggressive approach
        stopping_distance = (velocity_magnitude ** 2) / (2 * self.max_observed_accel) if self.max_observed_accel > 0 else 0
        stopping_distance = max(stopping_distance, 0.3)  # Reduced from 0.5
        stopping_distance *= 1.0  # Reduced from 3.0 - much less conservative

        speed_limit_factor = 1.0
        # Only slow down when VERY close to target
        if distance < stopping_distance * 1.2:  # Reduced from 2.5
            speed_limit_factor = max(0.4, distance / (stopping_distance * 1.2))  # Minimum 40% speed instead of 15%

        pitch_output = self.pitch_pid.update(delta_x, dt, velocity_x)
        pitch_disturbance = -pitch_output * speed_limit_factor

        roll_output = self.roll_pid.update(delta_y, dt, velocity_y)
        roll_disturbance = roll_output * speed_limit_factor

        return pitch_disturbance, roll_disturbance, False

    def get_stopping_distance(self):
        """Get current calculated stopping distance for debugging"""
        return self.accel_samples[-1] if self.accel_samples else 0.0

    def reset(self):
        """Reset controller state"""
        self.pitch_pid.reset()
        self.roll_pid.reset()
        self.accel_samples = []
        self.max_observed_accel = 0.5
