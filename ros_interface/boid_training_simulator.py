import os

import rclpy
from rclpy.node import Node
from rclpy.qos import QoSProfile, ReliabilityPolicy, HistoryPolicy
from nav_msgs.msg import Path as Path

import sheep_simulation

class DogSheepSimulator(Node):
    def __init__(self):
        super().__init__('dog_sheep_simulator')

        # Load existing weights
        self.weight_file_path = os.getenv('WEIGHT_FILE', '/tmp/weights.yaml')
        with open(self.weight_file_path) as f:
            self.weight_data = yaml.safe_load(f) or []

        # Default the storage points for the animal data
        self.dog_poses = {}
        self.sheep_poses = {}

        # Subscribe to input data
        self.create_subscription(Path, '/dog_poses', self._dog_cb, 10)
        self.create_subscription(Path, '/sheep_poses', self._sheep_cb, 10)

    def _dog_cb(self, msg):
        timestep = msg.header.secs
        dog_name = msg.header.frame_id or 'dog'

        # Initialise storage
        if timestep not in self.dog_poses:
            self.dog_poses[timestep] = dict()

        # Save dog position at timestep
        self.dog_poses[timestep][dog_name] = msg

    def _sheep_cb(self, msg):
        timestep = msg.header.secs
        self.sheep_poses[timestep] = msg
        self.run_simulation(msg.header.secs)

    def run_simulation(self, timestep):
        print('Simulating begun')
        N = 5

        # Execute simulation
        if str(timestep - N) in self.sheep_poses:

            # Extract sheep poses and velocities
            sheeps = dict()
            sheeps['pose'] = self.sheep_poses[timestep]
            sheeps['velocities'] = self.sheep_poses[timestep-1] - self.sheep_poses[timestep]

            # Initialise simulation
            sheep_simulation.main(dog_pose, sheep_initial_poses, sheep)

        # Execute simulation
        for i in range(N):
            sheep_simulation.upate(1, self.dog_pose)
            sheep_estimates = sheep_simulation.getstate()


def main():
    rclpy.init()
    node = DogSheepSimulator()
    rclpy.spin(node)
    node.weights.close()
    rclpy.shutdown()

if __name__ == '__main__':
    main()
