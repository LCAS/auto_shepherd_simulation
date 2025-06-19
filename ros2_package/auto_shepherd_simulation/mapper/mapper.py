import rclpy
from rclpy.node import Node
from nav_msgs.msg import Path
from geometry_msgs.msg import PoseStamped, Point, Quaternion
import yaml
import os
import tf2_ros
from geometry_msgs.msg import TransformStamped
from rclpy.qos import QoSProfile, ReliabilityPolicy, DurabilityPolicy
from tf_transformations import quaternion_from_euler
from typing import List, Tuple

from auto_shepherd_simulation.utils.geo_converter import MapConverter, load_coords_from_yaml

class MapperNode(Node):
    def __init__(self):
        super().__init__('mapper_node')
        self.declare_parameter('map_file_path', '/home/ros/map/map1.yaml')

        # Define the QoS profile for a latched topic
        qos_profile = QoSProfile(
            depth=1,
            reliability=ReliabilityPolicy.RELIABLE,
            durability=DurabilityPolicy.TRANSIENT_LOCAL
        )

        # Publisher for the Path message
        self.publisher_ = self.create_publisher(
            Path,
            'FieldBoundaryPath',
            qos_profile
        )

        # Static Transform Broadcaster for the 'field_frame'
        self.static_broadcaster = tf2_ros.StaticTransformBroadcaster(self)

        # --- Integration with geo_converter.py ---

        # 1. Get the map file path from parameters
        map_file_path = self.get_parameter('map_file_path').get_parameter_value().string_value
        self.get_logger().info(f"Attempting to load map for MapperNode from: {map_file_path}")

        # 2. Load raw (lat, lon) coordinates from the YAML file using the utility function
        try:
            raw_latlon_coords = load_coords_from_yaml(map_file_path)
            if not raw_latlon_coords:
                self.get_logger().error("Loaded map data is empty. Shutting down.")
                rclpy.shutdown()
                return
        except (FileNotFoundError, ValueError) as e:
            self.get_logger().error(f"Failed to load map data from YAML: {e}. Shutting down.")
            rclpy.shutdown()
            return

        # 3. Initialize the MapConverter with the raw LatLon coordinates
        try:
            self.map_converter = MapConverter(raw_latlon_coords)
            map_data = self.map_converter.get_map_data() # Get processed data from the converter

            # Store the map's calculated origin (in UTM)
            self.origin_utm_x = map_data['origin_utm_x']
            self.origin_utm_y = map_data['origin_utm_y']

            # Get the relative X, Y meters for the path
            self.path_xy_meters = map_data['map_coords_xy_meters']

            self.get_logger().info(f"Map converted. Origin (UTM X, Y): ({self.origin_utm_x:.3f}, {self.origin_utm_y:.3f})")

        except ValueError as e:
            self.get_logger().error(f"Error initializing MapConverter: {e}. Shutting down.")
            rclpy.shutdown()
            return

        # 4. Create PoseStamped messages for the Path from the relative meter coordinates
        self.path_poses = self._create_path_poses(self.path_xy_meters)
        if not self.path_poses:
            self.get_logger().error("Failed to create Path poses from converted data. Shutting down.")
            rclpy.shutdown()
            return

        # --- Publish Data ---

        # Publish the Path message immediately after loading and converting
        self.publish_path_once()

        # Publish the static transform for 'field_frame'
        self.publish_static_transform()

        self.get_logger().info("Map published once as Path. Static transform published. Node will now spin indefinitely.")

    def _create_path_poses(self, xy_meters: List[Tuple[float, float]]) -> List[PoseStamped]:
        """
        Helper to create a list of PoseStamped messages from relative X, Y meter coordinates.
        These poses are in the 'field_frame', which will be offset by the map's UTM origin.
        """
        poses = []
        # Create an identity quaternion for the poses (no rotation)
        q = quaternion_from_euler(0, 0, 0)
        identity_orientation = Quaternion(x=q[0], y=q[1], z=q[2], w=q[3])

        current_stamp = self.get_clock().now().to_msg() # Use a single stamp for all poses in this path

        for x_m, y_m in xy_meters:
            pose_stamped = PoseStamped()
            pose_stamped.header.stamp = current_stamp
            pose_stamped.header.frame_id = 'field_frame' # Poses are relative to 'field_frame'
            pose_stamped.pose.position.x = x_m
            pose_stamped.pose.position.y = y_m
            pose_stamped.pose.position.z = 0.0 # Assuming 2D path
            pose_stamped.pose.orientation = identity_orientation
            poses.append(pose_stamped)

        # Close the loop for the path visualization in RViz by adding the first point again
        if poses:
            # Create a new PoseStamped to avoid modifying the original first pose object
            closed_loop_pose = PoseStamped()
            closed_loop_pose.header.stamp = current_stamp
            closed_loop_pose.header.frame_id = 'field_frame'
            closed_loop_pose.pose.position.x = poses[0].pose.position.x
            closed_loop_pose.pose.position.y = poses[0].pose.position.y
            closed_loop_pose.pose.position.z = poses[0].pose.position.z
            closed_loop_pose.pose.orientation = poses[0].pose.orientation
            poses.append(closed_loop_pose)

        return poses

    def publish_path_once(self):
        """Publishes the Path message containing the field boundary."""
        if not self.path_poses:
            self.get_logger().warn("No poses to publish for Path. Skipping publication.")
            return

        path_msg = Path()
        path_msg.header.stamp = self.get_clock().now().to_msg()
        path_msg.header.frame_id = 'map' # The Path itself is defined in the 'map' frame

        path_msg.poses = self.path_poses

        self.publisher_.publish(path_msg)
        self.get_logger().info(f'Published FieldBoundaryPath with {len(path_msg.poses)} poses (once, latched).')

    def publish_static_transform(self):
        """Publishes the static transform from 'map' to 'field_frame'."""
        t = TransformStamped()
        t.header.stamp = self.get_clock().now().to_msg()
        t.header.frame_id = 'map'
        t.child_frame_id = 'field_frame'

        # Set the translation to the absolute UTM coordinates of the bounding box origin.
        # This places 'field_frame' (and thus the relative Path) at its real-world location in 'map'.
        t.transform.translation.x = self.origin_utm_x
        t.transform.translation.y = self.origin_utm_y
        t.transform.translation.z = 0.0 # Assuming 2D environment

        q = quaternion_from_euler(0, 0, 0) # Identity rotation for the frame itself
        t.transform.rotation.x = q[0]
        t.transform.rotation.y = q[1]
        t.transform.rotation.z = q[2]
        t.transform.rotation.w = q[3]

        self.static_broadcaster.sendTransform(t)
        self.get_logger().info(f"Published static transform from '{t.header.frame_id}' to '{t.child_frame_id}' at bounding box UTM origin.")

def main(args=None):
    rclpy.init(args=args)
    mapper_node = MapperNode()
    rclpy.spin(mapper_node)
    mapper_node.destroy_node()
    rclpy.shutdown()

if __name__ == '__main__':
    main()
