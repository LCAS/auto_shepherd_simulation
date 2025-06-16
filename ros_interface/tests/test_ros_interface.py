import unittest
from unittest.mock import MagicMock
import rclpy
from geometry_msgs.msg import Pose
from std_msgs.msg import Float64MultiArray
from ros_interface import RosInterface

class TestRosInterface(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        rclpy.init()

    @classmethod
    def tearDownClass(cls):
        rclpy.shutdown()

    def setUp(self):
        self.ros_interface = RosInterface()
        # Patch the publish methods directly
        self.ros_interface.drone_publisher.publish = MagicMock()
        self.ros_interface.dog_publisher.publish = MagicMock()
        self.ros_interface.sheep_publisher.publish = MagicMock()

    def tearDown(self):
        self.ros_interface.destroy_node()

    def test_publish_all(self):
        self.ros_interface.timer_callback()
        self.ros_interface.drone_publisher.publish.assert_called_once()
        self.ros_interface.dog_publisher.publish.assert_called_once()
        self.assertEqual(self.ros_interface.sheep_publisher.publish.call_count, 2)

    def test_initialization(self):
        self.assertIsNotNone(self.ros_interface.drone_publisher)
        self.assertIsNotNone(self.ros_interface.dog_publisher)
        self.assertIsNotNone(self.ros_interface.sheep_publisher)
        self.assertIsNotNone(self.ros_interface.dog_command_subscription)

if __name__ == '__main__':
    unittest.main()