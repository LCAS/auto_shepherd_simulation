from typing import List, Tuple

import rclpy
from rclpy.node import Node
from rclpy.qos import QoSProfile, ReliabilityPolicy, DurabilityPolicy, HistoryPolicy

from nav_msgs.msg import Path
from geometry_msgs.msg import PolygonStamped, PoseStamped, Quaternion, Point32, TransformStamped

import tf2_ros
from tf_transformations import quaternion_from_euler

from auto_shepherd_simulation_ros2.utils.geo_converter import MapConverter, load_coords_from_yaml

class MapperNode(Node):
    def __init__(self):
        super().__init__('mapper_node')

        #TODO: Fences should not be published from here, they should be published from sim_data_loader.py instead

        # Configure publishers
        self.publisher_path_ = self.create_publisher(Path, 'FieldBoundaryPath', self.get_qos())
        self.publisher_polygon_ = self.create_publisher(PolygonStamped, 'field', self.get_qos())
        self.static_broadcaster = tf2_ros.StaticTransformBroadcaster(self)

        # Load raw (lat, lon) coordinates
        map_file_path = '/home/ros/map/map1.yaml'
        self.get_logger().info(f"Attempting to load map for MapperNode from: {map_file_path}")
        raw_latlon_coords = load_coords_from_yaml(map_file_path)

        # Initialize the MapConverter
        self.map_converter = MapConverter(raw_latlon_coords)
        map_data = self.map_converter.get_map_data()

        # Store map origin (in UTM)
        self.origin_utm_x = map_data['origin_utm_x']
        self.origin_utm_y = map_data['origin_utm_y']

        # Get metric coords path and polygon
        self.relative_xy_meters = map_data['map_coords_xy_meters']
        self.get_logger().info(f"Map converted. Origin (UTM X, Y): ({self.origin_utm_x:.3f}, {self.origin_utm_y:.3f})")

        # Create messages for the path
        self.path_poses = self._create_path_poses(self.relative_xy_meters)
        self.polygon_points = self._create_polygon_points(self.relative_xy_meters)

        # Publish the Path, PolygonStamped, and static transform messages
        self.publish_path_once()
        self.publish_polygon_once()
        self.publish_static_transform()
        self.get_logger().info("Map published once as Path and Polygon. Static transform published. Node will now spin indefinitely.")

    def get_qos(self):
        qos_profile = QoSProfile(
            durability=DurabilityPolicy.TRANSIENT_LOCAL,
            history=HistoryPolicy.KEEP_LAST,
            depth=1
        )
        return qos_profile

    def _create_path_poses(self, xy_meters: List[Tuple[float, float]]) -> List[PoseStamped]:
        """
        Helper to create a list of PoseStamped messages from relative X, Y meter coordinates.
        These poses are in the 'field_frame', which will be offset by the map's UTM origin.
        """
        poses = []
        q = quaternion_from_euler(0, 0, 0)
        identity_orientation = Quaternion(x=q[0], y=q[1], z=q[2], w=q[3])
        current_stamp = self.get_clock().now().to_msg()

        for x_m, y_m in xy_meters:
            pose_stamped = PoseStamped()
            pose_stamped.header.stamp = current_stamp
            pose_stamped.header.frame_id = 'field_frame'
            pose_stamped.pose.position.x = y_m
            pose_stamped.pose.position.y = x_m
            pose_stamped.pose.position.z = 0.0
            pose_stamped.pose.orientation = identity_orientation
            poses.append(pose_stamped)

        # Close the loop by adding the first point again
        if poses:
            closed_loop_pose = PoseStamped()
            closed_loop_pose.header.stamp = current_stamp
            closed_loop_pose.header.frame_id = 'field_frame'
            closed_loop_pose.pose.position.x = poses[0].pose.position.x
            closed_loop_pose.pose.position.y = poses[0].pose.position.y
            closed_loop_pose.pose.position.z = poses[0].pose.position.z
            closed_loop_pose.pose.orientation = poses[0].pose.orientation
            poses.append(closed_loop_pose)
        return poses

    def _create_polygon_points(self, xy_meters: List[Tuple[float, float]]) -> List[Point32]:
        """
        Helper to create a list of Point32 messages for PolygonStamped.
        """
        points = []
        for x_m, y_m in xy_meters:
            p32 = Point32()
            p32.x = x_m
            p32.y = y_m
            p32.z = 0.0 # Assuming 2D polygon
            points.append(p32)

        # PolygonStamped is implicitly closed if first and last points are the same.
        # Adding the first point at the end ensures closure for clear visualization.
        if points:
            first_point = Point32()
            first_point.x = points[0].x
            first_point.y = points[0].y
            first_point.z = points[0].z
            points.append(first_point)
        return points

    def publish_path_once(self):
        """Publishes the Path message containing the field boundary."""
        if not self.path_poses:
            self.get_logger().warn("No poses to publish for Path. Skipping publication.")
            return

        path_msg = Path()
        path_msg.header.stamp = self.get_clock().now().to_msg()
        path_msg.header.frame_id = 'map' # The Path itself is defined in the 'map' frame (absolute)

        path_msg.poses = self.path_poses

        self.publisher_path_.publish(path_msg)
        self.get_logger().info(f'Published FieldBoundaryPath with {len(path_msg.poses)} poses (once, latched).')

    def publish_polygon_once(self):
        """Publishes the PolygonStamped message containing the field boundary."""
        if not self.polygon_points:
            self.get_logger().warn("No points to publish for PolygonStamped. Skipping publication.")
            return

        polygon_msg = PolygonStamped()
        polygon_msg.header.stamp = self.get_clock().now().to_msg()
        # The PolygonStamped is in 'field_frame' because its points are relative to that origin
        polygon_msg.header.frame_id = 'field_frame'
        polygon_msg.polygon.points = self.polygon_points

        self.publisher_polygon_.publish(polygon_msg)
        self.get_logger().info(f'Published PolygonStamped with {len(polygon_msg.polygon.points)} points (once, latched).')

    def publish_static_transform(self):
        """Publishes the static transform from 'map' to 'field_frame'."""
        t = TransformStamped()
        t.header.stamp = self.get_clock().now().to_msg()
        t.header.frame_id = 'map'
        t.child_frame_id = 'field_frame'

        # Set the translation to the absolute UTM coordinates of the bounding box origin.
        # This places 'field_frame' (and thus the relative Path/Polygon) at its real-world location in 'map'.
        t.transform.translation.x = 0.0 #self.origin_utm_y
        t.transform.translation.y = 0.0 #self.origin_utm_x
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
