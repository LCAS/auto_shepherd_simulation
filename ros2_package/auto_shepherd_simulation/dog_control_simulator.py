import os

import rclpy
from rclpy.node import Node
from rclpy.qos import QoSProfile, ReliabilityPolicy, HistoryPolicy
from nav_msgs.msg import Path as Path
from visualization_msgs.msg import Marker, MarkerArray
from geometry_msgs.msg import Point, Vector3, PoseStamped
from std_msgs.msg import ColorRGBA
from rclpy.callback_groups import ReentrantCallbackGroup as RCG

from auto_shepherd_simulation.sheep_simulation.simulation import Simulation

class DogControlSimulator(Node):
    def __init__(self):
        super().__init__('dog_control_simulator')

        # Default the storage points for the animal data
        self.sheep_poses = {}    # {sheep_name: [dict, dict, ...]}
        # self.dog_poses   = {}    # {timestep: {dog_name: PoseStamped}}
        self.dog_state   = None
        self.dog_command = None  # Store the latest dog command
        self.simulation  = None  # build later when we get data

        # Create QoS profile
        qos_profile = QoSProfile(
            reliability=ReliabilityPolicy.RELIABLE,
            history=HistoryPolicy.KEEP_LAST,
            depth=10
        )

        # Subscribe to input data
        self.create_subscription(PoseStamped, '/dog/pose', self._dog_cb, qos_profile)
        self.create_subscription(Path, '/sheep/poses', self._sheep_cb, qos_profile)
        self.create_subscription(PoseStamped, '/dog/command', self._dog_command_cb, qos_profile, callback_group=RCG())

        # Setup output channels
        self.create_publisher(Path, '/dog_paths', qos_profile)
        self.marker_pub = self.create_publisher(MarkerArray, '/simulation_markers', qos_profile)
        self.sheep_sim_pub = self.create_publisher(Path, '/sheep/poses_sim', qos_profile)

        self.sim_step_timer = self.create_timer(0.1, self.run_sim_step, callback_group=RCG())

        # print('Dog Control Simulator Initialised')
        

    def _dog_command_cb(self, msg):
        """Callback for dog command messages"""
        
        self.dog_command = {
            'position': [msg.pose.position.x, msg.pose.position.y],
            'orientation': [msg.pose.orientation.x, msg.pose.orientation.y, 
                          msg.pose.orientation.z, msg.pose.orientation.w],
            'velocity': [0,0]
        }
        self.get_logger().info(f'Received new dog command: {self.dog_command}')

    def _dog_cb(self, msg):
        # timestep = msg.header.secs
        # dog_name = msg.header.frame_id or 'dog'

        # Initialise storage
        # if timestep not in self.dog_poses:
        #     self.dog_poses[timestep] = dict()

        # Save dog position at timestep
        # self.dog_poses[timestep][dog_name] = msg

        self.dog_state = {
            'position': [msg.pose.position.x, msg.pose.position.y],
            'velocity': [0.0, 0.0]          # you can compute real velocity later
        }

    def _sheep_cb(self, msg):
        # Get current timestep
        timestep = msg.header.stamp.sec
        for sheep in msg.poses:

            # Create sheep creator
            name = sheep.header.frame_id
            if name not in self.sheep_poses:
                self.sheep_poses[name] = []

            # Get prior pose
            prior = None
            if self.sheep_poses[name]:
                prior = self.sheep_poses[name][-1]['position']

            # Save sheep pose data
            pos = sheep.pose.position
            current = {'position': [pos.x, pos.y]}
            if prior:
                px, py = prior
                current['velocity'] = [pos.x - px, pos.y - py]

            self.sheep_poses[name].append(current)
        print("sheep poses ready")
        print(self.simulation, self.dog_state)
        # ------------- create Simulation once -----------------------------
        if self.simulation is None and self.dog_state is not None:
            # flatten current sheep snapshot into list of dicts
            sheep_states = [hist[-1] for hist in self.sheep_poses.values()]
            self.simulation = Simulation(
                800, 600,
                sheep_states=sheep_states,
                sheepdog_state=self.dog_state
            )
            self.get_logger().info('Simulation initialised with '
                                f'{len(sheep_states)} sheep.')

    def publish_simulation_state(self, state):
        """Convert simulation state to MarkerArray and publish for RViz visualization"""
        marker_array = MarkerArray()
        
        # Create markers for sheep
        for i, sheep_state in enumerate(state['sheep']):
            # Create sheep marker
            sheep_marker = Marker()
            sheep_marker.header.frame_id = "map"
            sheep_marker.header.stamp = self.get_clock().now().to_msg()
            sheep_marker.ns = "sheep"
            sheep_marker.id = i
            sheep_marker.type = Marker.CYLINDER
            sheep_marker.action = Marker.ADD
            
            # Set sheep position
            sheep_marker.pose.position.x = sheep_state['position'][0]
            sheep_marker.pose.position.y = sheep_state['position'][1]
            sheep_marker.pose.position.z = 0.0
            
            # Set sheep size
            sheep_marker.scale = Vector3(x=5.0 , y=5.0, z=1.0)
            
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
                start_point = Point(x=sheep_state['position'][0], 
                                  y=sheep_state['position'][1], 
                                  z=0.0)
                end_point = Point(x=sheep_state['position'][0] + sheep_state['velocity'][0],
                                y=sheep_state['position'][1] + sheep_state['velocity'][1],
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
        dog_marker.header.frame_id = "map"
        dog_marker.header.stamp = self.get_clock().now().to_msg()
        dog_marker.ns = "sheepdog"
        dog_marker.id = 0
        dog_marker.type = Marker.CYLINDER
        dog_marker.action = Marker.ADD
        
        # Set dog position
        dog_marker.pose.position.x = dog_state['position'][0]
        dog_marker.pose.position.y = dog_state['position'][1]
        dog_marker.pose.position.z = 0.0
        
        # Set dog size (slightly larger than sheep)
        dog_marker.scale = Vector3(x=5.0, y=5.0 , z=2.5)
        
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
            start_point = Point(x=dog_state['position'][0], 
                              y=dog_state['position'][1], 
                              z=0.0)
            end_point = Point(x=dog_state['position'][0] + dog_state['velocity'][0],
                            y=dog_state['position'][1] + dog_state['velocity'][1],
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
            ps.pose.position.x = sheep['position'][0]
            ps.pose.position.y = sheep['position'][1]
            ps.pose.position.z = 0.0
            poses.append(ps)

        path_msg.poses = poses
        self.sheep_sim_pub.publish(path_msg)


    def run_sim_step(self, timestep=None):
        if self.simulation is None:          # wait until we have initialised it
            return
                
        # Execute simulation step
        self.simulation.update(self.dog_command)
        sheep_estimates = self.simulation.get_state()
        # Publish visualization markers
        self.publish_simulation_state(sheep_estimates)
        # Publish sheep path
        self.publish_sheep_path(sheep_estimates)


def main():
    rclpy.init()
    node = DogControlSimulator()
    rclpy.spin(node)
    rclpy.shutdown()

if __name__ == '__main__':
    main()
