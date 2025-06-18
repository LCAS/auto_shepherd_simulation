import rclpy
from rclpy.node import Node
# from geometry_msgs.msg import PolygonStamped, Point32 # No longer used for main path
from nav_msgs.msg import Path # New: Import Path message
from geometry_msgs.msg import PoseStamped, Point, Quaternion # PoseStamped, Point (for position), Quaternion (for orientation)
import yaml
import os
import tf2_ros
from geometry_msgs.msg import TransformStamped
from rclpy.qos import QoSProfile, ReliabilityPolicy, DurabilityPolicy

# For UTM conversion
from pyproj import CRS, Transformer
from tf_transformations import quaternion_from_euler # Helper for creating quaternions

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

        # Change publisher type to Path
        self.publisher_ = self.create_publisher(
            Path,
            'FieldBoundaryPath', # New topic name for clarity
            qos_profile
        )

        self.static_broadcaster = tf2_ros.StaticTransformBroadcaster(self)

        # UTM Conversion setup (for Lincoln, UK - UTM Zone 30N)
        wgs84_crs = CRS("EPSG:4326") # Latitude, Longitude
        utm30n_crs = CRS("EPSG:32630") # WGS 84 / UTM Zone 30N (meters)
        self.transformer = Transformer.from_crs(wgs84_crs, utm30n_crs)

        self.polygon_data = self.load_map_from_yaml()
        if not self.polygon_data:
            self.get_logger().error("Failed to load map data from specified path. Shutting down.")
            rclpy.shutdown()
            return

        # Perform UTM conversion on loaded data
        self.utm_poses = self.convert_to_utm_poses()
        if not self.utm_poses:
            self.get_logger().error("Failed to convert polygon data to UTM poses. Shutting down.")
            rclpy.shutdown()
            return

        # Publish the Path message immediately after loading and converting
        self.publish_path_once()

        # Publish the static transform after the map data is ready
        self.publish_static_transform()

        self.get_logger().info("Map published once as Path. Static transform published. Node will now spin indefinitely.")

    def load_map_from_yaml(self):
        map_file_path = self.get_parameter('map_file_path').get_parameter_value().string_value
        self.get_logger().info(f"Attempting to load map from: {map_file_path}")

        if not os.path.exists(map_file_path):
            self.get_logger().error(f"Map file not found at: {map_file_path}")
            return None

        try:
            with open(map_file_path, 'r') as file:
                data = yaml.safe_load(file)
                if 'field_boundary' in data and isinstance(data['field_boundary'], list):
                    return data.get('field_boundary')
                else:
                    self.get_logger().error("YAML file does not contain a 'field_boundary' list under the 'field_boundary' key.")
                    return None
        except yaml.YAMLError as e:
            self.get_logger().error(f"Error parsing YAML file: {e}")
            return None
        except Exception as e:
            self.get_logger().error(f"An unexpected error occurred while loading map: {e}")
            return None

    def convert_to_utm_poses(self):
        if not self.polygon_data:
            return []

        converted_poses = []
        # Calculate a local origin for the 'field_frame' based on the first point's UTM coordinates
        # This makes the path appear around (0,0) in RViz's 'field_frame'
        # while 'field_frame' itself is transformed to its true UTM location in 'map' frame.
        first_lat = float(self.polygon_data[0]['latitude'])
        first_lon = float(self.polygon_data[0]['longitude'])
        self.origin_utm_x, self.origin_utm_y = self.transformer.transform(first_lat, first_lon)
        self.get_logger().info(f"UTM Origin for 'field_frame' set to: ({self.origin_utm_x}, {self.origin_utm_y})")


        # Create an identity quaternion for the poses (no rotation)
        # tf_transformations.quaternion_from_euler(roll, pitch, yaw)
        # (0,0,0) means no rotation.
        q = quaternion_from_euler(0, 0, 0)
        identity_orientation = Quaternion(x=q[0], y=q[1], z=q[2], w=q[3])


        for point_data in self.polygon_data:
            lat = float(point_data['latitude'])
            lon = float(point_data['longitude'])

            utm_x, utm_y = self.transformer.transform(lat, lon)

            pose_stamped = PoseStamped()
            pose_stamped.header.stamp = self.get_clock().now().to_msg()
            pose_stamped.header.frame_id = 'field_frame' # Poses are relative to 'field_frame'

            # Position in meters, relative to the chosen origin of 'field_frame'
            pose_stamped.pose.position.x = utm_x - self.origin_utm_x
            pose_stamped.pose.position.y = utm_y - self.origin_utm_y
            pose_stamped.pose.position.z = 0.0 # Assuming 2D path

            pose_stamped.pose.orientation = identity_orientation # No rotation

            converted_poses.append(pose_stamped)

        # To close the loop for the path visualization in RViz
        if converted_poses:
            first_pose = converted_poses[0]
            # Create a new PoseStamped with updated stamp if needed, or just append the first one
            # Appending the first one directly is common for closing a loop.
            closed_loop_pose = PoseStamped()
            closed_loop_pose.header.stamp = self.get_clock().now().to_msg()
            closed_loop_pose.header.frame_id = 'field_frame'
            closed_loop_pose.pose = first_pose.pose
            converted_poses.append(closed_loop_pose)


        return converted_poses


    def publish_path_once(self):
        if not self.utm_poses:
            self.get_logger().warn("No UTM poses to publish for Path.")
            return

        path_msg = Path()
        path_msg.header.stamp = self.get_clock().now().to_msg()
        path_msg.header.frame_id = 'map' # The Path itself is in the 'map' frame

        path_msg.poses = self.utm_poses # Assign the list of PoseStamped messages

        self.publisher_.publish(path_msg)
        self.get_logger().info(f'Published FieldBoundaryPath with {len(path_msg.poses)} poses (once, latched).')


    def publish_static_transform(self):
        t = TransformStamped()
        t.header.stamp = self.get_clock().now().to_msg()
        t.header.frame_id = 'map'
        t.child_frame_id = 'field_frame'

        # This transform places 'field_frame' at the UTM coordinates of your first map point
        t.transform.translation.x = self.origin_utm_x
        t.transform.translation.y = self.origin_utm_y
        t.transform.translation.z = 0.0

        q = quaternion_from_euler(0, 0, 0) # Identity rotation for the frame itself
        t.transform.rotation.x = q[0]
        t.transform.rotation.y = q[1]
        t.transform.rotation.z = q[2]
        t.transform.rotation.w = q[3]

        self.static_broadcaster.sendTransform(t)
        self.get_logger().info(f"Published static transform from '{t.header.frame_id}' to '{t.child_frame_id}' at UTM origin.")


def main(args=None):
    rclpy.init(args=args)
    mapper_node = MapperNode()
    rclpy.spin(mapper_node)
    mapper_node.destroy_node()
    rclpy.shutdown()

if __name__ == '__main__':
    main()
