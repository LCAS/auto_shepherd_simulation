import rclpy
from rclpy.node import Node
from geometry_msgs.msg import Pose, Point, Quaternion, PoseArray, PoseStamped
from std_msgs.msg import Float64MultiArray, Header
import numpy as np
from builtin_interfaces.msg import Duration

class RosInterface(Node):
    def __init__(self):
        super().__init__('ros_interface')
        self.drone_publisher = self.create_publisher(PoseStamped, '/drone/pose', 10)
        self.dog_publisher = self.create_publisher(PoseStamped, '/dog/pose', 10)
        self.sheep_publisher = self.create_publisher(PoseArray, '/sheep/poses', 10)
        self.dog_command_subscription = self.create_subscription(Float64MultiArray, '/dog/command', self.dog_command_callback, 10)
        self.timer = self.create_timer(0.1, self.timer_callback)

    def create_header(self):
        header = Header()
        header.stamp = self.get_clock().now().to_msg()
        header.frame_id = 'map'
        return header

    def timer_callback(self):
        # Publish drone pose
        drone_pose = PoseStamped()
        drone_pose.header = self.create_header()
        drone_pose.pose.position = Point(x=1.0, y=2.0, z=3.0)
        drone_pose.pose.orientation = Quaternion(w=1.0)
        self.drone_publisher.publish(drone_pose)

        # Publish dog pose
        dog_pose = PoseStamped()
        dog_pose.header = self.create_header()
        dog_pose.pose.position = Point(x=4.0, y=5.0, z=0.0)
        dog_pose.pose.orientation = Quaternion(w=1.0)
        self.dog_publisher.publish(dog_pose)

        # Publish sheep poses as a PoseArray
        sheep_poses = PoseArray()
        sheep_poses.header = self.create_header()
        
        # Example sheep poses
        sheep_poses.poses = [
            Pose(position=Point(x=6.0, y=7.0, z=0.0), orientation=Quaternion(w=1.0)),
            Pose(position=Point(x=8.0, y=9.0, z=0.0), orientation=Quaternion(w=1.0))
        ]
        self.sheep_publisher.publish(sheep_poses)

    def dog_command_callback(self, msg):
        # Handle incoming dog command
        if len(msg.data) >= 2:
            target_x = msg.data[0]
            target_y = msg.data[1]
            self.get_logger().info(f'Received dog command: Move to ({target_x}, {target_y})')
        else:
            self.get_logger().warn('Received invalid dog command format')

def main(args=None):
    rclpy.init(args=args)
    ros_interface = RosInterface()
    rclpy.spin(ros_interface)
    ros_interface.destroy_node()
    rclpy.shutdown()

if __name__ == '__main__':
    main() 