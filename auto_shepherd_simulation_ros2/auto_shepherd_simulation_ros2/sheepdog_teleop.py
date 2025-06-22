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
import threading
import time

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
        
        # Control parameters
        self.max_speed = 5
        self.min_speed = 0
        self.current_speed = 0
        self.acceleration = 0.2
        self.deceleration = 0.3
        self.rotation_speed = 0.1  # Radians per frame
        self.dt = 0.1  # Time step for updates
        
        # Thread control
        self.running = True
        self.state_lock = threading.Lock()
        
        # Get terminal settings
        self.old_settings = termios.tcgetattr(sys.stdin)
        
        # Create reentrant callback group for timer
        self.callback_group = ReentrantCallbackGroup()
        
        # Create timers for state updates and keyboard input
        self.create_timer(self.dt, self.update_state, callback_group=self.callback_group)
        self.create_timer(0.01, self.check_keyboard, callback_group=self.callback_group)
        
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
            self.running = False
            rclpy.shutdown()
        else:
            self.handle_key(key)
    
    def update_state(self):
        """Update dog state based on current velocities"""
        with self.state_lock:
            # Natural deceleration when no keys are pressed
            self.current_speed = max(self.min_speed, self.current_speed - self.deceleration * 0.5)
            
            # Calculate movement based on current speed and direction
            dx = math.cos(self.yaw) * self.current_speed
            dy = -math.sin(self.yaw) * self.current_speed
            
            # Update position
            self.position[0] += dx
            self.position[1] += dy
            
            # Publish current state
            self.publish_command()
    
    def handle_key(self, key):
        """Handle keypress to update velocities"""
        with self.state_lock:
            # Handle speed control
            if key == 'w' or key == '\x1b[A':  # W or Up Arrow
                # Increase speed
                self.current_speed = min(self.max_speed, self.current_speed + self.acceleration)
            elif key == 's' or key == '\x1b[B':  # S or Down Arrow
                # Decrease speed
                self.current_speed = max(self.min_speed, self.current_speed - self.deceleration)
            
            # Handle rotation
            if key == 'a' or key == '\x1b[D':  # A or Left Arrow
                self.yaw += self.rotation_speed
            elif key == 'd' or key == '\x1b[C':  # D or Right Arrow
                self.yaw -= self.rotation_speed
    
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