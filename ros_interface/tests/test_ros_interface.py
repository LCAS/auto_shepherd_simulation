import unittest
from unittest.mock import MagicMock, patch
from ros_interface import RosInterface
import rclpy
from geometry_msgs.msg import PoseStamped
from nav_msgs.msg import Path

class TestRosInterface(unittest.TestCase):
    def setUp(self):
        rclpy.init()
        self.ros_interface = RosInterface()
        # Mock the publishers
        self.ros_interface.drone_publisher = MagicMock()
        self.ros_interface.dog_publisher = MagicMock()
        self.ros_interface.sheep_publisher = MagicMock()

    def tearDown(self):
        self.ros_interface.destroy_node()
        rclpy.shutdown()

    def test_publish_all(self):
        # Call the timer callback
        self.ros_interface.timer_callback()

        # Check that each publisher was called once
        self.assertEqual(self.ros_interface.drone_publisher.publish.call_count, 1)
        self.assertEqual(self.ros_interface.dog_publisher.publish.call_count, 1)
        self.assertEqual(self.ros_interface.sheep_publisher.publish.call_count, 1)

        # Verify the sheep poses were published as a Path
        sheep_path = self.ros_interface.sheep_publisher.publish.call_args[0][0]
        self.assertIsInstance(sheep_path, Path)
        self.assertEqual(len(sheep_path.poses), 2)

if __name__ == '__main__':
    unittest.main()