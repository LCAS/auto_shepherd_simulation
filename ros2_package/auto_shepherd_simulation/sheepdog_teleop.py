#!/usr/bin/env python3

import sys
import termios
import tty
import select
import rclpy
from rclpy.node import Node
from geometry_msgs.msg import PoseStamped
from rclpy.qos import QoSProfile, ReliabilityPolicy, HistoryPolicy
from rclpy.callback_groups import ReentrantCallbackGroup
import math
import numpy as np

class SheepdogTeleop(Node):
    def __init__(self):
        super().__init__('sheepdog_teleop')
        
        # Create QoS profile
        qos_profile = QoSProfile(
            reliability=ReliabilityPolicy.RELIABLE,
            history=HistoryPolicy.KEEP_LAST,
            depth=10
        )
        
        # Create publisher for dog commands
        self.command_pub = self.create_publisher(PoseStamped, '/dog/command', qos_profile)
        
        # Initialize dog state
        self.position = [400.0, 300.0]  # Center of the simulation area
        self.yaw = 0.0  # Current yaw angle in radians
        self.velocity = 0.0  # Current forward velocity
        self.angular_velocity = 0.0  # Current angular velocity
        
        # Control parameters
        self.max_velocity = 5.0
        self.acceleration = 0.2
        self.deceleration = 0.3
        self.rotation_speed = 0.1
        self.dt = 0.1  # Time step for updates
        
        # Get terminal settings
        self.old_settings = termios.tcgetattr(sys.stdin)
        
        # Create reentrant callback group for timers
        self.callback_group = ReentrantCallbackGroup()
        
        # Create timers for state updates and keyboard input
        self.create_timer(self.dt, self.update_state, callback_group=self.callback_group)
        self.create_timer(0.01, self.check_keyboard, callback_group=self.callback_group)  # Check keyboard more frequently
        
        # Print instructions
        self.print_instructions()
    
    def print_instructions(self):
        print("\nSheepdog Teleoperation Controls:")
        print("  W/Up Arrow: Accelerate forward")
        print("  S/Down Arrow: Decelerate to stop")
        print("  A/Left Arrow: Rotate left")
        print("  D/Right Arrow: Rotate right")
        print("  Q: Quit")
        print("\nPress any key to start...")
    
    def get_key(self):
        """Get a single keypress from the terminal"""
        try:
            tty.setraw(sys.stdin.fileno())
            select.select([sys.stdin], [], [], 0)
            key = sys.stdin.read(1)
        finally:
            termios.tcsetattr(sys.stdin, termios.TCSADRAIN, self.old_settings)
        return key
    
    def check_keyboard(self):
        """Check for keyboard input in a non-blocking way"""
        key = self.get_key()
        if key == 'q':
            print("\nQuitting teleoperation...")
            rclpy.shutdown()
        else:
            self.handle_key(key)
    
    def update_state(self):
        """Update dog state based on current velocities"""
        # Update position based on velocity and yaw
        self.position[0] += self.velocity * math.cos(self.yaw) * self.dt
        self.position[1] += self.velocity * math.sin(self.yaw) * self.dt
        
        # Update yaw based on angular velocity
        self.yaw += self.angular_velocity * self.dt
        
        # Normalize yaw to [-pi, pi]
        self.yaw = math.atan2(math.sin(self.yaw), math.cos(self.yaw))
        
        # Publish current state
        self.publish_command()
    
    def handle_key(self, key):
        """Handle keypress to update velocities"""
        if key == 'w' or key == '\x1b[A':  # W or Up Arrow
            self.velocity = min(self.velocity + self.acceleration * self.dt, self.max_velocity)
        elif key == 's' or key == '\x1b[B':  # S or Down Arrow
            self.velocity = max(self.velocity - self.deceleration * self.dt, 0.0)
        elif key == 'a' or key == '\x1b[D':  # A or Left Arrow
            self.angular_velocity = self.rotation_speed
        elif key == 'd' or key == '\x1b[C':  # D or Right Arrow
            self.angular_velocity = -self.rotation_speed
        else:
            self.angular_velocity = 0.0
    
    def publish_command(self):
        """Publish the current dog state as a command"""
        msg = PoseStamped()
        msg.header.stamp = self.get_clock().now().to_msg()
        msg.header.frame_id = "map"
        
        # Set position
        msg.pose.position.x = float(self.position[0])
        msg.pose.position.y = float(self.position[1])
        msg.pose.position.z = 0.0
        
        # Convert yaw to quaternion
        msg.pose.orientation.x = 0.0
        msg.pose.orientation.y = 0.0
        msg.pose.orientation.z = math.sin(self.yaw / 2.0)
        msg.pose.orientation.w = math.cos(self.yaw / 2.0)
        
        self.command_pub.publish(msg)

def main():
    rclpy.init()
    node = SheepdogTeleop()
    try:
        rclpy.spin(node)
    except KeyboardInterrupt:
        print("\nTeleoperation interrupted by user")
    finally:
        # Restore terminal settings
        termios.tcsetattr(sys.stdin, termios.TCSADRAIN, node.old_settings)
        rclpy.shutdown()

if __name__ == '__main__':
    main() 