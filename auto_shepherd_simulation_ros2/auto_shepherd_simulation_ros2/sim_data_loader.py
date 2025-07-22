import time
import numpy as np
from copy import deepcopy

import rclpy
from rclpy.node import Node
from rclpy.qos import QoSProfile, DurabilityPolicy, HistoryPolicy

from std_msgs.msg import Header, UInt16
from geometry_msgs.msg import Point, Quaternion, PoseStamped, PoseWithCovarianceStamped
from nav_msgs.msg import Path
from visualization_msgs.msg import Marker, MarkerArray



class SimDataLoader(Node):
    def __init__(self):
        super().__init__('sim_data_loader')


        #TODO: dog and goal initialisation could come form a config file.
        #TODO: fences should be published form here too


        # Connect to dog topics
        self.dog_publisher = self.create_publisher(PoseStamped, '/dog/pose', self.get_qos())
        self.dog_location_sub = self.create_subscription(PoseWithCovarianceStamped, '/initialpose', self.dog_initialpose_cb, 10)
        

        # Connect to target topics
        self.sheep_goal_subscriber = self.create_subscription(PoseStamped, '/goal_pose', self.goal_pose_cb, 10)
        self.sheep_goal_publisher = self.create_publisher(PoseStamped, '/sheep/goal', self.get_qos())
        self.sheep_goal_marker_pub = self.create_publisher(MarkerArray, '/sheep/goal_marker', 10)


        # Connect to sheep topics
        self.sheep_publisher = self.create_publisher(UInt16, '/sheep/randomise', self.get_qos())
        self.start_simulated_sheep()


    def get_qos(self):
        qos_profile = QoSProfile(
            durability=DurabilityPolicy.TRANSIENT_LOCAL,
            history=HistoryPolicy.KEEP_LAST,
            depth=1
        )
        return qos_profile

    #########################
    ##         DOG         ##
    #########################

    def dog_initialpose_cb(self, msg):
        # Sub to redirect /initialpose to /dog/pose
        self.get_logger().info(f"Initialising dog to: x={msg.pose.pose.position.x} y={msg.pose.pose.position.y}")
        ps = PoseStamped()
        ps.pose = msg.pose.pose
        ps.header = msg.header
        self.dog_publisher.publish(ps)


    #########################
    ##        SHEEP        ##
    #########################

    def start_simulated_sheep(self):
        # Publish to the initialise random sheep poses
        count = 40
        self.get_logger().info(f"Initialising {count} sheep randomly.")
        self.sheep_publisher.publish(UInt16(data=count))

    def goal_pose_cb(self, msg):
        # Sub to redirect /goal_pose to /sheep/goal
        self.get_logger().info(f"Setting goal pose to: x={msg.pose.position.x} y={msg.pose.position.y}")
        self.sheep_goal_publisher.publish(msg)

        # Publish markers
        colors = [(1.0, 0.0, 0.0), (1.0, 1.0, 1.0)]  # red, white
        radii = [2.5, 2.0, 1.5, 1.0, 0.5]          # outer to inner
        scale = 1.0
        marker_array = MarkerArray()
        for i, radius in enumerate(radii):
            marker = Marker()
            marker.header = msg.header
            marker.ns = 'target'
            marker.id = i
            marker.type = Marker.CYLINDER
            marker.action = Marker.ADD
            marker.pose = deepcopy(msg.pose)
            marker.pose.position.z = msg.pose.position.z + (i*0.02)
            marker.scale.x = marker.scale.y = (radius * scale) * 2
            marker.scale.z = 0.02
            color = colors[i % len(colors)]
            marker.color.r = color[0]
            marker.color.g = color[1]
            marker.color.b = color[2]
            marker.color.a = 1.0
            marker.lifetime.sec = 0
            marker_array.markers.append(marker)
        self.sheep_goal_marker_pub.publish(marker_array)


def main(args=None):
    rclpy.init(args=args)
    sim_data_loader = SimDataLoader()
    rclpy.spin(sim_data_loader)
    sim_data_loader.destroy_node()
    rclpy.shutdown()

if __name__ == '__main__':
    main()
