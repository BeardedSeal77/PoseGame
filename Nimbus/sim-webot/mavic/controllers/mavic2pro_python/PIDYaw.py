"""
Yaw/Heading Controller for Drone Navigation
Keeps drone continuously pointed at target using PID control
"""

import math
from PID import PIDController


class YawController:
    """
    Controls drone yaw to keep it pointed at target.
    Runs continuously during navigation - no phase gating.
    """

    def __init__(self, kp, kd, max_yaw=1.3):
        """
        Args:
            kp: Proportional gain for heading error
            kd: Derivative gain (uses yaw velocity for damping)
            max_yaw: Maximum yaw disturbance (radians/sec or motor input)
        """
        self.pid = PIDController(kp, 0.0, kd, -max_yaw, max_yaw)
        self.max_yaw = max_yaw

    def update(self, delta_x, delta_y, yaw_velocity, dt):
        """
        Calculate yaw disturbance to point drone at target

        Args:
            delta_x: Forward distance to target (m, drone frame)
            delta_y: Lateral distance to target (m, drone frame, positive = right)
            yaw_velocity: Current yaw angular velocity (rad/s)
            dt: Time delta (seconds)

        Returns:
            yaw_disturbance: Motor input for yaw control
        """
        target_angle = math.atan2(delta_y, delta_x)

        yaw_disturbance = self.pid.update(target_angle, dt, velocity=yaw_velocity)

        return yaw_disturbance

    def reset(self):
        """Reset controller state"""
        self.pid.reset()
