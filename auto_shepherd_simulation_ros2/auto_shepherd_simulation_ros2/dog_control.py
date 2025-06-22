import rclpy
from rclpy.node import Node
from geometry_msgs.msg import PoseStamped, Point, Quaternion
from nav_msgs.msg import Path
from std_msgs.msg import Float64MultiArray
import numpy as np
import math
# from shapely.geometry import Point, LineString
from auto_shepherd_simulation_ros2.tmpDogSim.dog_control_lib import find_best_dog_position, pure_pursuit, plot_current_state
from auto_shepherd_simulation_ros2.utils.geo_converter import load_coords_from_yaml, MapConverter

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
        self.create_subscription(PoseStamped, '/goal_pose',
                                 self._goal_cb, 10)

        # state caches ----------------------------------------------------
        self.dog_xy   = None              # (x, y)
        self._planned_dog_xy = None       # (x, y) of the last planned dog position
        self.sheep_xy = None              # Nx2 array
        self.goal_xy  = None              # (x, y)

        # control timer ----------------------------------------------------
        self.timer = self.create_timer(0.1, self._control_step)

        yaml_map_file_path = "/home/ros/map/map1.yaml"
        print(f"Attempting to load field coordinates from: {yaml_map_file_path}")
        try:
            field_coords_latlon = load_coords_from_yaml(yaml_map_file_path)
            print(f"Successfully loaded {len(field_coords_latlon)} coordinates from YAML.")
        except (FileNotFoundError, ValueError) as e:
            print(f"Failed to load coordinates from YAML: {e}")
            print("Please ensure the file path is correct and the YAML format matches 'field_boundary: - latitude: X - longitude: Y'.")
            print("Exiting example.")
            exit(1) # Exit if cannot load map data


        # Create Map Bounding Box & Convert All Coords
        try:
            self.map_converter = MapConverter(field_coords_latlon)
            map_data = self.map_converter.get_map_data()

            self.field_boundary = map_data['map_coords_xy_meters']

        except ValueError as e:
            print(f"Error during map conversion: {e}")
            self.map_converter = None # Ensure map_converter is not set if initialization failed


    # ------------ message callbacks -------------------------------------
    def _dog_cb(self, msg: PoseStamped):
        self.dog_xy = (msg.pose.position.x, msg.pose.position.y)

    def _sheep_cb(self, msg: Path):
        self.sheep_xy = np.array(
            [[p.pose.position.x, p.pose.position.y] for p in msg.poses])

    def _goal_cb(self, msg: PoseStamped):
        self.goal_xy = (msg.pose.position.x, msg.pose.position.y)

    def _init_boundary_follow(self):
        pts   = np.array(self.field_boundary)
        dog_x, dog_y = self.dog_xy            # latest live pose
        # distance to every vertex
        dists = np.hypot(pts[:,0]-dog_x, pts[:,1]-dog_y)
        self.wp_index = int(dists.argmin())   # closest vertex
        self.wp_index_init = int(dists.argmin())
        # choose direction (CW vs CCW) by, e.g., lowest steering angle
        self.wp_dir   = +1                    # +1 = CCW, –1 = CW
        self.lap_done = False
        vx, vy = pts[self.wp_index]
        self.get_logger().info(
            f"Starting boundary-follow at vertex {self.wp_index} "
            f"({vx:.2f}, {vy:.2f})"
        )

    def _boundary_follow_step(self):
        LOOKAHEAD = 2.0      # metres
        STEP = 1.0
        LAP_THRESH = 3.0     # metres to re-hit wp[0] and finish

        if self.lap_done or self.field_boundary is None:
            return None, None

        dog_x, dog_y = (self._planned_dog_xy
                    if self._planned_dog_xy is not None
                    else self.dog_xy)

        # ---------- 1. choose look-ahead target ----------------------------
        while True:
            tgt = self.field_boundary[self.wp_index]
            d   = np.hypot(tgt[0]-dog_x, tgt[1]-dog_y)
            if d > LOOKAHEAD:
                break                              # keep this wp
            # reached => advance along polygon
            self.wp_index = (self.wp_index + self.wp_dir) % len(self.field_boundary)

            # lap-complete test: wrapped around & close to start
            if self.wp_index == self.wp_index_init and d < LAP_THRESH:
                self.lap_done = True
                self.get_logger().info("Boundary lap completed!")
                return dog_x, dog_y

        # ---------- 2. call path controller --------------------------------
        xd_opt, yd_opt, _ = pure_pursuit((dog_x, dog_y), tgt, LOOKAHEAD, step=STEP)
        return xd_opt, yd_opt

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
            self._init_boundary_follow()
        else:
            xd_start, yd_start = self._planned_dog_xy # LATER calls → last plan

        xs, ys = self.sheep_xy[:, 0], self.sheep_xy[:, 1]
        xc, yc = self.goal_xy

        # if not self.lap_done:
        #     xd_opt, yd_opt = self._boundary_follow_step()
        #     print(f"Boundary follow: ({xd_opt:.2f}, {yd_opt:.2f})")
        # else:
        xd_opt, yd_opt = find_best_dog_position(xs, ys, xd_start, yd_start, xc, yc, self.field_boundary)
        print(f"Optimised dog position: ({xd_opt:.2f}, {yd_opt:.2f})")

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
