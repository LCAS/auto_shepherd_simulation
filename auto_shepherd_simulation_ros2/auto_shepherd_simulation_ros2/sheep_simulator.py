import os
import math

import rclpy
from rclpy.node import Node
from rclpy.qos import QoSProfile, ReliabilityPolicy, HistoryPolicy, DurabilityPolicy
from rclpy.callback_groups import ReentrantCallbackGroup as RCG

from std_msgs.msg import ColorRGBA, UInt16
from geometry_msgs.msg import Point, Vector3, PoseStamped, PoseWithCovarianceStamped
from nav_msgs.msg import Path as Path
from visualization_msgs.msg import Marker, MarkerArray

from auto_shepherd_simulation_ros2.sheep_simulation.simulation import Simulation
from auto_shepherd_simulation_ros2.utils.geo_converter import MapConverter, load_coords_from_yaml

class SheepSimulator(Node):
    def __init__(self):
        super().__init__('dog_control_simulator')

        # Default the storage points for the animal data
        self.sheep_poses = {}    # {sheep_name: [dict, dict, ...]}
        self.sheep_pose_store = {}
        # self.dog_poses   = {}    # {timestep: {dog_name: PoseStamped}}
        self.dog_state   = None
        self.dog_command = None  # Store the latest dog command
        self.simulation  = None  # build later when we get data
        self.field_boundary = None  # build later when we get data
        self.map_converter = None  # build later when we get data
        self.simulation = None
        self.num_sheep = None

        # Create QoS profile
        qos_profile = QoSProfile(
            reliability=ReliabilityPolicy.RELIABLE,
            history=HistoryPolicy.KEEP_LAST,
            depth=10
        )
        self.frame = 0

        # For dog connections
        self.create_subscription(PoseStamped, '/dog/pose', self._dog_cb, self.get_qos())
        self.create_subscription(PoseStamped, '/dog/command', self._dog_command_cb, qos_profile, callback_group=RCG())
        self.create_publisher(Path, '/dog_paths', qos_profile)

        # For sheep connections
        self.create_subscription(Path, '/sheep/poses', self._sheep_cb, qos_profile)
        self.create_subscription(UInt16, '/sheep/randomise', self._sheep_randomise_cb, self.get_qos()) # For sim data
        self.sheep_sim_pub = self.create_publisher(Path, '/sheep/poses_sim', qos_profile)

        # Setup rviz visuals
        self.marker_pub = self.create_publisher(MarkerArray, '/simulation_markers', qos_profile)

        # Load the field boundaries
        self.create_subscription(Path, "/field/gps_fence/path", self._gps_fence_cb, self.get_qos())
        self.create_subscription(Path, "/field/fence/path", self._fence_cb, self.get_qos())

        # Start the simulator
        self.start_simulation()

    def start_simulation(self):
        print('Sim init attempt')

        # Exit if data not ready
        if not self.field_boundary: return
        if not self.map_converter: return

        # Exit if already started
        if self.simulation: return

        print('Sim init')

        # Start Simulation
        self.simulation = Simulation(self.field_boundary, 800, 600, sheep_states=None, sheepdog_state=None, spawn_random=False)
        self.dt = 0.05
        self.sim_step_timer = self.create_timer(self.dt, self.run_sim_step, callback_group=RCG())
        print('Dog Control Simulator Initialised')

        # Initialise the sheep
        if self.num_sheep:
            self.simulation.num_sheep = self.num_sheep
            self.simulation.sheep_list = []
            self.simulation._initialize_sheep(None, spawn_random=True)
            self.get_logger().info(f"Initialised {self.num_sheep} sheep.")


    def get_qos(self):
        qos_profile = QoSProfile(
            durability=DurabilityPolicy.TRANSIENT_LOCAL,
            history=HistoryPolicy.KEEP_LAST,
            depth=1
        )
        return qos_profile

    def _gps_fence_cb(self, msg):
        print('GPS Fence cb')
        field_coords_latlon = [(p.pose.position.x, p.pose.position.y) for p in msg.poses]
        self.map_converter = MapConverter(field_coords_latlon)

        # Start the simulator
        self.start_simulation()

    def _fence_cb(self, msg):
        print('Fence cb')
        field_coords = [(p.pose.position.y, p.pose.position.x) for p in msg.poses]
        self.field_boundary = field_coords

        # Start the simulator
        self.start_simulation()

    def _dog_command_cb(self, msg):
        """Callback for dog command messages"""

        self.dog_command = {
            'position': [msg.pose.position.y, msg.pose.position.x],
            'orientation': [msg.pose.orientation.x, msg.pose.orientation.y,
                          msg.pose.orientation.z, msg.pose.orientation.w],
            'velocity': [0,0]
        }
        self.get_logger().info(f'Received new dog command: {self.dog_command}')


    def _dog_cb(self, msg):
        self.dog_state = {
            'position': [msg.pose.position.x, msg.pose.position.y],
            'velocity': [0.0, 0.0]          # you can compute real velocity later
        }
        self.get_logger().info(f"Callback detected for dog now at x:{msg.pose.position.x}, y:{msg.pose.position.y}.")

    def _sheep_randomise_cb(self, msg):
        print('Sheep init cb')

        # If map data not available yet, skip
        if not self.simulation:
            self.num_sheep = msg.data
            return

        self.simulation.num_sheep = msg.data
        self.simulation.sheep_list = []
        self.simulation._initialize_sheep(None, spawn_random=True)
        self.get_logger().info(f"Initialised {msg.data} sheep.")

    def _sheep_cb(self, msg):

        # Skip frames if not needed
        self.frame += 1
        #if not str(self.frame).endswith('0'): return

        # If map data not available yet, skip
        if not self.map_converter: return

        # Get current timestep
        print('_______________')
        self.sheep_poses = {}
        for sheep in msg.poses:

            # Create sheep creator
            name = sheep.header.frame_id
            if name not in self.sheep_poses:
                self.sheep_poses[name] = []
            if name not in self.sheep_pose_store:
                self.sheep_pose_store[name] = []

            # Get prior pose
            prior = None
            if self.sheep_pose_store[name]:
                prior = self.sheep_pose_store[name][-1]['position']

            # Save sheep pose data
            pos = sheep.pose.position
            x, y = self.map_converter.latlon_to_xy(pos.x, pos.y)
            print(pos.x, pos.y, x, y, self.map_converter.origin_lat, self.map_converter.origin_lon)
            current = {'position': [x, y]}
            self.sheep_pose_store[name].append(current)
            if prior:
                px, py = prior
                current['velocity'] = [x - px, y - py]
                self.sheep_poses[name].append(current)

        total_tracked = len([len(s) for s in self.sheep_poses if s])
        self.get_logger().info(f"Callback detected with {len(msg.poses)} sheep, totalling {total_tracked} tracked target ids.")

        # Flatten current sheep snapshot into list of dicts
        sheep_states = [hist[-1] for hist in self.sheep_poses.values() if hist]
        self.get_logger().info(f"Sheep positions updated to {len(sheep_states)} sheep.")

        # Update sheep positions in simulation to input
        self.simulation._initialize_sheep(sheep_states)
        [print(s[-1]) for s in self.sheep_poses.values() if s]


    def publish_simulation_state(self, state):
        """Convert simulation state to MarkerArray and publish for RViz visualization"""
        marker_array = MarkerArray()

        # Create markers for sheep
        for i, sheep_state in enumerate(state['sheep']):
            # Create sheep marker
            sheep_marker = Marker()
            sheep_marker.header.frame_id = "field_frame"
            sheep_marker.header.stamp = self.get_clock().now().to_msg()
            sheep_marker.ns = "sheep"
            sheep_marker.id = i
            sheep_marker.type = Marker.CYLINDER
            sheep_marker.action = Marker.ADD

            # Set sheep position
            sheep_marker.pose.position.x = sheep_state['position'][1]
            sheep_marker.pose.position.y = sheep_state['position'][0]
            sheep_marker.pose.position.z = 0.0

            # Set sheep size
            sheep_marker.scale = Vector3(x=1.0 , y=1.0, z=1.0)

            # Set sheep color (white)
            sheep_marker.color = ColorRGBA(r=1.0, g=1.0, b=1.0, a=1.0)

            # Add velocity arrow
            if 'velocity' in sheep_state:
                vel_marker = Marker()
                vel_marker.header = sheep_marker.header
                vel_marker.ns = "sheep_velocity"
                vel_marker.id = i
                vel_marker.type = Marker.ARROW
                vel_marker.action = Marker.ADD

                # Set arrow points
                start_point = Point(x=sheep_state['position'][1],
                                  y=sheep_state['position'][0],
                                  z=0.0)
                end_point = Point(x=sheep_state['position'][1] + sheep_state['velocity'][1],
                                y=sheep_state['position'][0] + sheep_state['velocity'][0],
                                z=0.0)
                vel_marker.points = [start_point, end_point]

                # Set arrow properties
                vel_marker.scale = Vector3(x=1.0, y=1.0, z=0.5)
                vel_marker.color = ColorRGBA(r=0.0, g=1.0, b=0.0, a=1.0)

                marker_array.markers.append(vel_marker)

            marker_array.markers.append(sheep_marker)

        # Create marker for sheepdog
        dog_state = state['sheepdog']
        dog_marker = Marker()
        dog_marker.header.frame_id = "field_frame"
        dog_marker.header.stamp = self.get_clock().now().to_msg()
        dog_marker.ns = "sheepdog"
        dog_marker.id = 0
        dog_marker.type = Marker.CYLINDER
        dog_marker.action = Marker.ADD

        # Set dog position
        dog_marker.pose.position.x = dog_state['position'][1]
        dog_marker.pose.position.y = dog_state['position'][0]
        dog_marker.pose.position.z = 0.0

        # Set dog size (slightly larger than sheep)
        dog_marker.scale = Vector3(x=1.0, y=1.0 , z=1.0)

        # Set dog color (brown)
        dog_marker.color = ColorRGBA(r=1.0, g=0.0, b=0.0, a=1.0)

        # Add velocity arrow for dog
        if 'velocity' in dog_state:
            vel_marker = Marker()
            vel_marker.header = dog_marker.header
            vel_marker.ns = "dog_velocity"
            vel_marker.id = 0
            vel_marker.type = Marker.ARROW
            vel_marker.action = Marker.ADD

            # Set arrow points
            start_point = Point(x=dog_state['position'][1],
                              y=dog_state['position'][0],
                              z=0.0)
            end_point = Point(x=dog_state['position'][1] + dog_state['velocity'][1],
                            y=dog_state['position'][0] + dog_state['velocity'][0],
                            z=0.0)
            vel_marker.points = [start_point, end_point]

            # Set arrow properties
            vel_marker.scale = Vector3(x=0.1, y=0.2, z=0.1)
            vel_marker.color = ColorRGBA(r=1.0, g=0.0, b=0.0, a=1.0)

            marker_array.markers.append(vel_marker)

        marker_array.markers.append(dog_marker)

        # Publish the markers
        self.marker_pub.publish(marker_array)


    def publish_sheep_path(self, state):
        """Convert simulation['sheep'] list → nav_msgs/Path and publish."""
        path_msg = Path()
        path_msg.header.frame_id = "map"
        path_msg.header.stamp = self.get_clock().now().to_msg()

        poses = []
        for i, sheep in enumerate(state['sheep']):
            ps = PoseStamped()
            ps.header = path_msg.header          # same stamp / frame
            ps.header.frame_id = f"sheep_{i}"    # optional: unique id
            ps.pose.position.x = sheep['position'][1]
            ps.pose.position.y = sheep['position'][0]
            ps.pose.position.z = 0.0
            poses.append(ps)

        path_msg.poses = poses
        self.sheep_sim_pub.publish(path_msg)


    def run_sim_step(self, timestep=None):
        if self.simulation is None:          # wait until we have initialised it
            return

        # Execute simulation step
        self.simulation.update(self.dt, self.dog_command)
        sheep_estimates = self.simulation.get_state()

        # Publish visualization markers
        self.publish_simulation_state(sheep_estimates)
        # Publish sheep path
        self.publish_sheep_path(sheep_estimates)


def main():
    rclpy.init()
    node = SheepSimulator()
    rclpy.spin(node)
    rclpy.shutdown()


if __name__ == '__main__':
    main()
