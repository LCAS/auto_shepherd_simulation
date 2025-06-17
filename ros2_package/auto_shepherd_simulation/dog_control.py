import rclpy
from rclpy.node import Node
from geometry_msgs.msg import PoseStamped
from nav_msgs.msg import Path
from std_msgs.msg import Float64MultiArray
import numpy as np
import math
from tmpDogSim.dog_control_lib import find_best_dog_position

class DogController(Node):
    def __init__(self):
        super().__init__('dog_controller')

        # publishers / subscribers ---------------------------------------
        self.cmd_pub = self.create_publisher(
            Float64MultiArray, '/dog/command', 10)   # :contentReference[oaicite:2]{index=2}

        self.create_subscription(PoseStamped, '/dog/pose',
                                 self._dog_cb, 10)   # :contentReference[oaicite:3]{index=3}
        self.create_subscription(Path, '/sheep/poses',
                                 self._sheep_cb, 10) # :contentReference[oaicite:4]{index=4}
        self.create_subscription(PoseStamped, '/sheep/goal_pose',
                                 self._goal_cb, 10)

        # state caches ----------------------------------------------------
        self.dog_xy   = None              # (x, y)
        self.sheep_xy = None              # Nx2 array
        self.goal_xy  = None              # (x, y)

        # control timer ----------------------------------------------------
        self.timer = self.create_timer(0.1, self._control_step)

    # ------------ message callbacks -------------------------------------
    def _dog_cb(self, msg: PoseStamped):
        self.dog_xy = (msg.pose.position.x, msg.pose.position.y)

    def _sheep_cb(self, msg: Path):
        self.sheep_xy = np.array(
            [[p.pose.position.x, p.pose.position.y] for p in msg.poses])

    def _goal_cb(self, msg: PoseStamped):
        self.goal_xy = (msg.pose.position.x, msg.pose.position.y)

    # ------------ closed-loop control -----------------------------------
    def _control_step(self):
        if self.dog_xy is None or self.sheep_xy is None or self.goal_xy is None:
            return  # wait for all data

        xs, ys = self.sheep_xy[:, 0], self.sheep_xy[:, 1]
        xd, yd = self.dog_xy
        xc, yc = self.goal_xy

        xd_opt, yd_opt = find_best_dog_position(xs, ys, xd, yd, xc, yc)

        cmd = Float64MultiArray()
        cmd.data = [float(xd_opt), float(yd_opt)]
        self.cmd_pub.publish(cmd)
        self.get_logger().debug(f'Cmd ({xd_opt:.2f}, {yd_opt:.2f})')

# ----------------------------------------------------------------------
# entry-point -----------------------------------------------------------
def main(args=None):
    rclpy.init(args=args)
    node = DogController()
    rclpy.spin(node)
    node.destroy_node()
    rclpy.shutdown()

if __name__ == '__main__':
    main()
