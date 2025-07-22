from typing import List, Tuple

import rclpy
from rclpy.node import Node
from rclpy.qos import QoSProfile, ReliabilityPolicy, DurabilityPolicy, HistoryPolicy

from std_msgs.msg import Header
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
        self.gps_path_pub = self.create_publisher(Path, '/field/gps_fence/path', self.get_qos())
        self.field_path_pub = self.create_publisher(Path, '/field/fence/path', self.get_qos())
        self.gps_polygon_pub = self.create_publisher(PolygonStamped, '/field/gps_fence/polygon', self.get_qos())
        self.field_polygon_pub = self.create_publisher(PolygonStamped, '/field/fence/polygon', self.get_qos())
        self.static_broadcaster = tf2_ros.StaticTransformBroadcaster(self)

        # Load raw (lat, lon) coordinates
        map_file_path = '/home/ros/map/map1.yaml'
        self.get_logger().info(f"Attempting to load map for MapperNode from: {map_file_path}")
        self.raw_latlon_coords = load_coords_from_yaml(map_file_path)

        # Initialize the MapConverter
        self.map_converter = MapConverter(self.raw_latlon_coords)
        map_data = self.map_converter.get_map_data()

        # Store map origin (in UTM)
        self.origin_utm_x = map_data['origin_utm_x']
        self.origin_utm_y = map_data['origin_utm_y']

        # Get metric coords path and polygon
        self.relative_xy_meters = map_data['map_coords_xy_meters']
        self.get_logger().info(f"Map converted. Origin (UTM X, Y): ({self.origin_utm_x:.3f}, {self.origin_utm_y:.3f})")

        # Create messages for the path
        self.path_gps = self.create_path_poses(self.raw_latlon_coords)
        self.path_poses = self.create_path_poses(self.relative_xy_meters)
        self.polygon_gps = self.create_polygon_points(self.raw_latlon_coords)
        self.polygon_points = self.create_polygon_points(self.relative_xy_meters)

        # Publish the Path, PolygonStamped, and static transform messages
        self.publish_msgs()
        self.get_logger().info("Map published once as Path and Polygon. Static transform published. Node will now spin indefinitely.")

    def get_qos(self):
        qos_profile = QoSProfile(
            durability=DurabilityPolicy.TRANSIENT_LOCAL,
            history=HistoryPolicy.KEEP_LAST,
            depth=1
        )
        return qos_profile

    def create_path_poses(self, xy_meters: List[Tuple[float, float]]) -> List[PoseStamped]:
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

    def create_polygon_points(self, xy_meters: List[Tuple[float, float]]) -> List[Point32]:
        """
        Helper to create a list of Point32 messages for PolygonStamped.
        """
        points = []
        for x_m, y_m in xy_meters:
            p32 = Point32()
            p32.x = y_m
            p32.y = x_m
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

    def header(self, frame):
        header = Header()
        header.stamp = self.get_clock().now().to_msg()
        header.frame_id = frame
        return header

    def publish_msgs(self):

        # Pubilsh Path (metric)
        msg = Path()
        msg.header = self.header(frame='map')
        msg.poses = self.path_poses
        self.field_path_pub.publish(msg) #BROWN

        # Pubilsh Path (gps)
        msg = Path()
        msg.header = self.header(frame='world')
        msg.poses = self.path_gps
        self.gps_path_pub.publish(msg) #GREEN

        # Pubilsh Polygon (metric)
        msg = PolygonStamped()
        msg.header = self.header(frame='map')
        msg.polygon.points = self.polygon_points
        self.field_polygon_pub.publish(msg) #ORANGE

        # Publish Polygon (gps)
        msg = PolygonStamped()
        msg.header = self.header(frame='world')
        msg.polygon.points = self.polygon_gps
        self.gps_polygon_pub.publish(msg) #BLUE

        self.get_logger().info(f'Published Field Boundary with {len(self.path_poses)}.')

        # Publish static transform
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
