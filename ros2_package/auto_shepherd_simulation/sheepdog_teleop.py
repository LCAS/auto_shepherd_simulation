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
        self.velocity = 0.0  # Current forward velocity
        self.angular_velocity = 0.0  # Current angular velocity
        
        # Control parameters
        self.max_velocity = 30.0  # Increased max speed
        self.acceleration = 2.0  # Increased for more responsive acceleration
        self.deceleration = 1.0  # Increased for faster stopping
        self.natural_deceleration = 0.5  # Increased for faster natural slowdown
        self.rotation_speed = 1.0  # Increased for faster turning
        self.dt = 0.1  # Time step for updates
        
        # Thread control
        self.running = True
        self.state_lock = threading.Lock()
        self.forward_pressed = False  # Track if forward key is pressed
        self.left_pressed = False    # Track if left key is pressed
        self.right_pressed = False   # Track if right key is pressed
        
        # Get terminal settings
        self.old_settings = termios.tcgetattr(sys.stdin)
        
        # Create reentrant callback group for timer
        self.callback_group = ReentrantCallbackGroup()
        
        # Create timer for state updates
        self.create_timer(self.dt, self.update_state, callback_group=self.callback_group)
        
        # Start keyboard input thread
        self.keyboard_thread = threading.Thread(target=self.keyboard_loop)
        self.keyboard_thread.start()
        
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
    
    def keyboard_loop(self):
        """Thread function for handling keyboard input"""
        while self.running:
            key = self.get_key()
            if key == 'q':
                print("\nQuitting teleoperation...")
                self.running = False
                rclpy.shutdown()
                break
            
            with self.state_lock:
                self.handle_key(key)
            
            # Small sleep to prevent CPU hogging
            time.sleep(0.01)
    
    def update_state(self):
        """Update dog state based on current velocities"""
        with self.state_lock:
            # Apply natural deceleration when no forward key is pressed
            if not self.forward_pressed and self.velocity > 0:
                self.velocity = max(0.0, self.velocity - self.natural_deceleration * self.dt)
            
            # Update position based on velocity and yaw
            self.position[0] += self.velocity * math.cos(self.yaw) * self.dt
            self.position[1] += self.velocity * math.sin(self.yaw) * self.dt
            
            # Update yaw based on angular velocity
            self.yaw += self.angular_velocity * self.dt
            
            # Normalize yaw to [-pi, pi]
            self.yaw = math.atan2(math.sin(self.yaw), math.cos(self.yaw))
            
            # Reset angular velocity if no turning keys are pressed
            if not (self.left_pressed or self.right_pressed):
                self.angular_velocity = 0.0
            
            # Publish current state
            self.publish_command()
    
    def handle_key(self, key):
        """Handle keypress to update velocities"""
        if key == 'w' or key == '\x1b[A':  # W or Up Arrow
            self.forward_pressed = True
            self.velocity = min(self.velocity + self.acceleration * self.dt, self.max_velocity)
        elif key == 's' or key == '\x1b[B':  # S or Down Arrow
            self.forward_pressed = False
            self.velocity = max(self.velocity - self.deceleration * self.dt, 0.0)
        elif key == 'a' or key == '\x1b[D':  # A or Left Arrow
            self.left_pressed = True
            self.angular_velocity = self.rotation_speed
        elif key == 'd' or key == '\x1b[C':  # D or Right Arrow
            self.right_pressed = True
            self.angular_velocity = -self.rotation_speed
        elif key == '':  # No key pressed
            self.forward_pressed = False
            self.left_pressed = False
            self.right_pressed = False
    
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
        # Stop the keyboard thread
        node.running = False
        if node.keyboard_thread.is_alive():
            node.keyboard_thread.join()
        
        # Restore terminal settings
        termios.tcsetattr(sys.stdin, termios.TCSADRAIN, node.old_settings)
        rclpy.shutdown()

if __name__ == '__main__':
    main() 