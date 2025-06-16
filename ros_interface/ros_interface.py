import rclpy
from rclpy.node import Node
from geometry_msgs.msg import Pose, Point, Quaternion
from std_msgs.msg import Float64MultiArray
import numpy as np

class RosInterface(Node):
    def __init__(self):
        super().__init__('ros_interface')
        self.drone_publisher = self.create_publisher(Pose, '/drone/pose', 10)
        self.dog_publisher = self.create_publisher(Pose, '/dog/pose', 10)
        self.sheep_publisher = self.create_publisher(Pose, '/sheep/poses', 10)
        self.dog_command_subscription = self.create_subscription(Float64MultiArray, '/dog/command', self.dog_command_callback, 10)
        self.timer = self.create_timer(0.1, self.timer_callback)

    def timer_callback(self):
        # Publish drone pose (example: using a fixed position and orientation)
        drone_pose = Pose()
        drone_pose.position = Point(x=1.0, y=2.0, z=3.0)
        drone_pose.orientation = Quaternion(w=1.0)
        self.drone_publisher.publish(drone_pose)

        # Publish dog pose (example: using a fixed position and orientation)
        dog_pose = Pose()
        dog_pose.position = Point(x=4.0, y=5.0, z=0.0)
        dog_pose.orientation = Quaternion(w=1.0)
        self.dog_publisher.publish(dog_pose)

        # Publish sheep poses (example: using a list of fixed positions and orientations)
        sheep_poses = [
            Pose(position=Point(x=6.0, y=7.0, z=0.0), orientation=Quaternion(w=1.0)),
            Pose(position=Point(x=8.0, y=9.0, z=0.0), orientation=Quaternion(w=1.0))
        ]
        for pose in sheep_poses:
            self.sheep_publisher.publish(pose)

    def dog_command_callback(self, msg):
        # Handle incoming dog command
        self.get_logger().info('Received dog command: %s' % msg.data)

def main(args=None):
    rclpy.init(args=args)
    ros_interface = RosInterface()
    rclpy.spin(ros_interface)
    ros_interface.destroy_node()
    rclpy.shutdown()

if __name__ == '__main__':
    main() 