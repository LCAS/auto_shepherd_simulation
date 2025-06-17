import os

import rclpy
from rclpy.node import Node
from rclpy.qos import QoSProfile, ReliabilityPolicy, HistoryPolicy
from nav_msgs.msg import Path as Path

import sheep_simulation

class DogControlSimulator(Node):
    def __init__(self):
        super().__init__('dog_control_simulator')

        # Default the storage points for the animal data
        self.sheep_poses = {}

        # Subscribe to input data
        self.create_subscription(Path, '/dog_poses', self._dog_cb, 10)
        self.create_subscription(Path, '/sheep_poses', self._sheep_cb, 10)

        # Setup output channels
        self.create_subscription(Path, '/dog_paths', 10)


    def _dog_cb(self, msg):
        timestep = msg.header.secs
        dog_name = msg.header.frame_id or 'dog'

        # Initialise storage
        if timestep not in self.dog_poses:
            self.dog_poses[timestep] = dict()

        # Save dog position at timestep
        self.dog_poses[timestep][dog_name] = msg

    def _sheep_cb(self, msg):
        # Get current timestep
        timestep = msg.header.stamp.secs
        for sheep in msg.poses:

            # Create sheep creator
            name = sheep.header.frame_id
            if name not in self.sheep_poses:
                self.sheep_poses[name] = dict()

            # Get prior pose
            prior = None
            if self.sheep_poses[name]:
                prior = self.sheep_pose[name][-1]

            # Save sheep pose data
            pos = sheep.pose.position
            self.sheep_poses[name].append(dict())
            self.sheep_poses[name][-1]['position'] = [pos.x, pos.y]
            if prior:
                px, py = prior
                self.sheep_poses[name][-1]['velocity'] = [pos.x - px, pos.y - py]

        # Run the simulation
        self.run_simulation(msg.header.secs, sheeps)

    def run_simulation(self, timestep):
        print('Simulating begun')
        N = 5

        # Execute simulation
        sheeps = [s[-1] for s in self.sheep_poses]
        sheep_simulation.main(dogs, sheeps)

        # Execute simulation
        for i in range(N):
            sheep_simulation.upate(1, self.dog_pose)
            sheep_estimates = sheep_simulation.getstate()


def main():
    rclpy.init()
    node = DogControlSimulator()
    rclpy.spin(node)
    rclpy.shutdown()

if __name__ == '__main__':
    main()
