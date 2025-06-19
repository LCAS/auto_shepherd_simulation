import rclpy
from rclpy.node import Node
from geometry_msgs.msg import Pose, Point, Quaternion, PoseStamped
from nav_msgs.msg import Path
from std_msgs.msg import Float64MultiArray, Header
import numpy as np
from builtin_interfaces.msg import Duration

class RosInterface(Node):
    def __init__(self):
        super().__init__('ros_interface')
        self.drone_publisher = self.create_publisher(PoseStamped, '/drone/pose', 10)
        self.dog_publisher = self.create_publisher(PoseStamped, '/dog/pose', 10)
        self.sheep_publisher = self.create_publisher(Path, '/sheep/poses', 10)
        self.sheep_goal_publisher = self.create_publisher(PoseStamped, '/sheep/goal_pose', 10)
        self.dog_command_subscription = self.create_subscription(PoseStamped, '/dog/command', self.dog_command_callback, 10)
        self.timer = self.create_timer(0.1, self.timer_callback)

    def create_header(self):
        header = Header()
        header.stamp = self.get_clock().now().to_msg()
        header.frame_id = 'map'
        return header

    def create_pose_stamped(self, x, y, z, qw=1.0, qx=0.0, qy=0.0, qz=0.0):
        pose_stamped = PoseStamped()
        pose_stamped.header = self.create_header()
        pose_stamped.pose.position = Point(x=x, y=y, z=z)
        pose_stamped.pose.orientation = Quaternion(x=qx, y=qy, z=qz, w=qw)
        return pose_stamped

    def timer_callback(self):
        # Publish drone pose
        drone_pose = self.create_pose_stamped(1.0, 2.0, 3.0)
        self.drone_publisher.publish(drone_pose)

        # Publish dog pose
        dog_pose = self.create_pose_stamped(-10.0, 10.0, 0.0)
        self.dog_publisher.publish(dog_pose)

        # Publish sheep poses as a Path
        sheep_path = Path()
        sheep_path.header = self.create_header()

        # Example sheep poses with individual timestamps
        sheep_path.poses = [
            self.create_pose_stamped(6.0, 7.0, 0.0),
            self.create_pose_stamped(8.0, 9.0, 0.0)
        ]
        self.sheep_publisher.publish(sheep_path)

        # Publish sheep goal pose
        sheep_goal_pose = self.create_pose_stamped(-38.0, 90.0, 0.0)
        self.sheep_goal_publisher.publish(sheep_goal_pose)

    def dog_command_callback(self, msg):
        target_x = msg.pose.position.x
        target_y = msg.pose.position.y
        # orientation is already in quaternion form if you need it
        self.get_logger().info(
            f"Received dog command: Move to ({target_x:.2f}, {target_y:.2f})"
        )

def main(args=None):
    rclpy.init(args=args)
    ros_interface = RosInterface()
    rclpy.spin(ros_interface)
    ros_interface.destroy_node()
    rclpy.shutdown()

if __name__ == '__main__':
    main()
