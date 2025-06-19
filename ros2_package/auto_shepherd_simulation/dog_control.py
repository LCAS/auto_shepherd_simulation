import rclpy
from rclpy.node import Node
from geometry_msgs.msg import PoseStamped, Point, Quaternion
from nav_msgs.msg import Path
from std_msgs.msg import Float64MultiArray
import numpy as np
import math
from auto_shepherd_simulation.tmpDogSim.dog_control_lib import find_best_dog_position, plot_current_state
from auto_shepherd_simulation.utils.geo_converter import load_coords_from_yaml, MapConverter

class DogController(Node):
    def __init__(self):
        super().__init__('dog_controller')

        # publishers / subscribers ---------------------------------------
        self.cmd_pub = self.create_publisher(
            PoseStamped, '/dog/command', 10)   # :contentReference[oaicite:2]{index=2}

        self.create_subscription(PoseStamped, '/dog/pose',
                                 self._dog_cb, 10)   # :contentReference[oaicite:3]{index=3}
        self.create_subscription(Path, '/sheep/poses_sim',
                                 self._sheep_cb, 10) # :contentReference[oaicite:4]{index=4}
        self.create_subscription(PoseStamped, '/sheep/goal_pose',
                                 self._goal_cb, 10)

        # state caches ----------------------------------------------------
        self.dog_xy   = None              # (x, y)
        self._planned_dog_xy = None       # (x, y) of the last planned dog position
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
        print("_control_step")

        # make sure we have the three inputs we need
        if self.sheep_xy is None or self.goal_xy is None:
            return
        if self.dog_xy is None and self._planned_dog_xy is None:
            return   # still waiting for the very first dog pose

        # ---------------------------------------------
        # choose the starting point for optimisation
        # ---------------------------------------------
        if self._planned_dog_xy is None:
            xd_start, yd_start = self.dog_xy          # FIRST call → live pose
        else:
            xd_start, yd_start = self._planned_dog_xy # LATER calls → last plan

        xs, ys = self.sheep_xy[:, 0], self.sheep_xy[:, 1]
        xc, yc = self.goal_xy

        xd_opt, yd_opt = find_best_dog_position(xs, ys, xd_start, yd_start, xc, yc)

        ps = PoseStamped()
        ps.header.stamp = self.get_clock().now().to_msg()
        ps.header.frame_id = "map"        # or any frame you prefer
        ps.pose.position = Point(x=float(xd_opt), y=float(yd_opt), z=0.0)
        ps.pose.orientation = Quaternion(w=1.0)  # identity; adjust if needed
        self.cmd_pub.publish(ps)
        self.get_logger().debug(f'Cmd ({xd_opt:.2f}, {yd_opt:.2f})')

        self._planned_dog_xy = (xd_opt, yd_opt)

        # plot
        # plot_current_state(xs, ys, xd, yd, xc, yc, xd_opt, yd_opt)
        

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
