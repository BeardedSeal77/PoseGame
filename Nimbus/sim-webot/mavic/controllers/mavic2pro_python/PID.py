"""
Generic PID Controllers for Drone Navigation
Can be copied to nimbus-robotics for real drone use
"""

import math


def clamp(value, min_val, max_val):
    """Clamp value between min and max"""
    return max(min_val, min(max_val, value))


class PIDController:
    """Generic single-axis PID controller with velocity feedback"""
    def __init__(self, kp, ki, kd, output_min=-float('inf'), output_max=float('inf'), integral_max=10.0):
        self.kp = kp
        self.ki = ki
        self.kd = kd
        self.output_min = output_min
        self.output_max = output_max
        self.integral_max = integral_max

        self.prev_error = 0.0
        self.integral = 0.0

    def update(self, error, dt, velocity=None):
        """
        Update PID with current error and time delta

        Args:
            error: Position error (setpoint - current)
            dt: Time delta in seconds
            velocity: Optional current velocity for D-term (more accurate than derivative of error)

        Returns:
            Control output (clamped)
        """
        p_term = self.kp * error

        self.integral += error * dt
        self.integral = clamp(self.integral, -self.integral_max, self.integral_max)
        i_term = self.ki * self.integral

        if velocity is not None:
            d_term = -self.kd * velocity
        else:
            d_term = 0.0
            if dt > 0:
                d_term = self.kd * (error - self.prev_error) / dt

        self.prev_error = error

        output = p_term + i_term + d_term
        return clamp(output, self.output_min, self.output_max)

    def reset(self):
        """Reset controller state"""
        self.prev_error = 0.0
        self.integral = 0.0


class AltitudeController:
    """PD controller for altitude with cubic error scaling"""
    def __init__(self, k_p, k_d, offset=0.0):
        self.k_p = k_p
        self.k_d = k_d
        self.offset = offset
        self.prev_altitude = 0.0

    def update(self, current_altitude, target_altitude, dt):
        """
        Calculate vertical thrust adjustment
        Returns: vertical_input to add to base thrust
        """
        altitude_error = target_altitude - current_altitude + self.offset

        altitude_rate = 0.0
        if dt > 0:
            altitude_rate = (current_altitude - self.prev_altitude) / dt
        self.prev_altitude = current_altitude

        clamped_error = clamp(altitude_error, -1.0, 1.0)

        vertical_input = (self.k_p * pow(clamped_error, 3.0) - self.k_d * altitude_rate)

        return vertical_input
