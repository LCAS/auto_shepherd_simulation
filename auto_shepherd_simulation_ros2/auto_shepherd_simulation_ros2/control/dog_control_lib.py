
import math as maths
import random

import numpy as np
import cv2
import matplotlib.pyplot as plt
from matplotlib.path import Path
from scipy.spatial import ConvexHull, Delaunay
from sklearn.cluster import DBSCAN

from std_msgs.msg import Header, ColorRGBA
from builtin_interfaces.msg import Duration
from geometry_msgs.msg import Point
from visualization_msgs.msg import Marker, MarkerArray

from auto_shepherd_simulation_ros2.sheep_simulation.simulation import Simulation
from auto_shepherd_simulation_ros2.utils.geo_converter import load_coords_from_yaml, MapConverter

def circle_around_points(points):
    diff = points[:, np.newaxis, :] - points[np.newaxis, :, :]
    distances = np.sqrt(np.sum(diff**2, axis=-1))
    max_distance_indices = np.unravel_index(np.argmax(distances), distances.shape)
    midpoint = (points[max_distance_indices[0]] + points[max_distance_indices[1]])/2
    radius = distances[max_distance_indices[0], max_distance_indices[1]] / 2
    return midpoint, radius

def get_direction(x, y, xd, yd):
    dx = x - xd
    dy = y - yd
    avg_dx = np.mean(dx)
    avg_dy = np.mean(dy)
    return normalise_velocities(avg_dx, avg_dy)
    #return normalise_velocities(*(np.random.random(2)-0.5))

def normalise_velocities(*values):
    v2, sumvalues = [], sum(np.abs(values))
    for val in values:
        v2.append(val/sumvalues)
    return v2

def angle(xbase, ybase, xnew, ynew):
    return maths.acos((xbase*xnew + ybase*ynew)/(maths.sqrt(xbase**2+ybase**2)*maths.sqrt(xnew**2+ynew**2)))

def cost(x, y, xd, yd, xc, yc, simulation, distance_weight = 1):
    # get the new direction of the flock given the current sheep positions and future dog position
    # xvel, yvel = get_direction(x, y, xd, yd)
    # position and velocity,
    sheepdog_state = {'position': [xd, yd], 'velocity': [0, 0]}
    simulation.update(0.05, sheepdog_state)
    new_state = simulation.get_state()
    x_new = []
    y_new = []
    for i, sheep in enumerate(new_state['sheep']):
        x_new.append(sheep['position'][0])
        y_new.append(sheep['position'][1])
    xvel, yvel = get_direction(np.array(x_new), np.array(y_new), x, y)

    # get the angle between that and the desired direction
    xmean, ymean = np.mean(x), np.mean(y)
    xveldesired, yveldesired = xc - xmean, yc - ymean
    xveldesired, yveldesired = normalise_velocities(xveldesired, yveldesired)
    return angle(xvel, yvel, xveldesired, yveldesired) - distance_weight * np.linalg.norm(np.array([xd,yd] - np.array([xc,yc])))
    #return angle(xvel, yvel, xveldesired, yveldesired) # penalise distance to closest sheep, reject if within 2m of sheep


def color_for_label(label):
    random.seed(label)
    return ColorRGBA(r=random.random(), g=random.random(), b=random.random(), a=0.3)

def render_dbscan_convex_hulls(pub, db, points, frame_id="field_frame", z=0.0):
    marker_array = MarkerArray()
    labels = db.labels_
    unique_labels = set(labels)
    
    marker_id = 0
    for label in unique_labels:
        if label == -1:
            continue  # Skip noise

        cluster_points = points[labels == label]

        if len(cluster_points) < 3:
            continue  # Can't form a hull

        try:
            hull = ConvexHull(cluster_points[:, :2])
            delaunay = Delaunay(cluster_points[hull.vertices, :2])
        except:
            continue  # Skip invalid hull

        marker = Marker()
        marker.header = Header(frame_id=frame_id)
        marker.ns = "dbscan_hulls"
        marker.id = marker_id
        marker.type = Marker.TRIANGLE_LIST
        marker.action = Marker.ADD
        marker.scale.x = marker.scale.y = marker.scale.z = 1.0
        marker.pose.orientation.w = 1.0
        marker.color = color_for_label(label)
        marker.lifetime = Duration(sec=1, nanosec=500_000_000)  # 1.5 seconds

        for simplex in delaunay.simplices:
            for idx in simplex:
                pt = cluster_points[hull.vertices[idx]]
                p = Point()
                p.x, p.y = pt[0], pt[1]
                p.z = pt[2] if pt.shape[0] == 3 else z
                marker.points.append(p)

        marker_array.markers.append(marker)
        marker_id += 1

    pub.publish(marker_array)


def render_targets_points(targets_pub, scores, best):
    marker_array = MarkerArray()
    header = Header(frame_id="field_frame")

    max_score = max(
        (abs(s['cost']) for s in scores.values() if s['cost'] != -1),
        default=1.0
    )

    for i, data in scores.items():
        marker = Marker()
        marker.header = header
        marker.ns = "target_points"
        marker.id = i
        marker.type = Marker.CYLINDER
        marker.action = Marker.ADD

        marker.pose.position.x = data['x']
        marker.pose.position.y = data['y']
        marker.pose.position.z = 0.2

        # Twice as big and proportional to normalized score (except for -1)
        if data['cost'] == -1:
            scale = 0.3  # default size for invalid scores
        elif data['cost'] == -2:
            scale = 0.3  # default size for invalid scores
        else:
            scale = 2.0 * (0.2 + 0.8 * (abs(data['cost']) / max_score))

        marker.scale.x = marker.scale.y = scale
        marker.scale.z = 0.1  # Cylinder height

        # Color conditions
        if data == best:
            marker.color = ColorRGBA(r=0.0, g=1.0, b=0.0, a=0.5)  # Green
        elif data['cost'] == -1:
            marker.color = ColorRGBA(r=0.0, g=0.0, b=0.0, a=0.5)  # Black
        elif data['cost'] == -2:
            marker.color = ColorRGBA(r=0.0, g=0.0, b=1.0, a=0.5)  # Blue
        else:
            marker.color = ColorRGBA(r=1.0, g=1.0, b=0.0, a=0.5)  # Yellow

        marker.lifetime.sec = 1
        marker_array.markers.append(marker)

    targets_pub.publish(marker_array)


def find_best_dog_position(x, y, xd, yd, xc, yc, field_boundary,  # ← flock, dog, goal
                           radius_d=1.4, n_candidates=15, early_exit_threshold=5,
                           default_goto=np.asarray((0,0)),
                           boundary_pub=None, targets_pub=None):

    """Return optimal dog (x_d*, y_d*) given current flock and goal."""
    points = np.stack([x, y], axis=1)
    goal_point = np.array([xc,yc])

    db = DBSCAN(eps=10, min_samples=1).fit(points)
    if boundary_pub:
        render_dbscan_convex_hulls(boundary_pub, db, points)
    labels = db.labels_
    cluster_distances = []
    print(f"{len(np.unique(labels))} clusters found")
    if len(np.unique(labels)) != 1:
        for cluster in np.unique(labels):
            centre_of_cluster = np.mean(points[labels==cluster],axis=0)
            distance_to_goal = np.linalg.norm(centre_of_cluster-goal_point)
            cluster_distances.append([cluster, distance_to_goal])
        cluster_distances.sort(key=lambda x: x[1])
        points = points[labels==cluster_distances[-1][0]]
        # # set the goal the second furthest cluster
        # (xc, yc), r = circle_around_points(points[labels==cluster_distances[-2][0]])

    (xmean, ymean), radius_sheep = circle_around_points(points)
    radius_sheep = max(radius_sheep, 1) # ensure single sheep clusters have a radius

    d = np.linalg.norm(np.asarray([xmean, ymean]) - np.asarray([xd, yd]))
    if np.linalg.norm(np.asarray([xmean, ymean]) - np.asarray([xc, yc])) < 5:
        d = np.linalg.norm(np.asarray([-10, 10]) - np.asarray([xd, yd]))
        if d < 3:
            optimal_xd, optimal_yd = xd, yd
        else:
            closest_point = (xd + (-10 - xd) * radius_d / d, yd + (10 - yd) * radius_d / d)
            points = np.array([closest_point])
            optimal_xd, optimal_yd = closest_point
        scores = {0: {'x': optimal_xd, 'y': optimal_yd, 'cost': 1}}
        best = min(scores.values(), key=lambda s: s['cost'])
        optimal_xd, optimal_yd = best['x'], best['y']

    # 
    elif d > radius_d + radius_sheep:
        print("Moving towards sheep")
        closest_point = (xd + (xmean - xd) * radius_d / d, yd + (ymean - yd) * radius_d / d)
        points = np.array([closest_point])
        optimal_xd, optimal_yd = closest_point
        scores = {0: {'x': optimal_xd, 'y': optimal_yd, 'cost': 1}}
        best = min(scores.values(), key=lambda s: s['cost'])
        optimal_xd, optimal_yd = best['x'], best['y']

    # 
    elif d < abs(radius_d - radius_sheep):
        print("Moving away from sheep")
        d = np.linalg.norm(default_goto - np.asarray([xd, yd]))
        closest_point = (xd + (default_goto[0] - xd) * radius_d / d, yd + (default_goto[1] - yd) * radius_d / d)
        points = np.array([closest_point])
        optimal_xd, optimal_yd = closest_point
        scores = {0: {'x': optimal_xd, 'y': optimal_yd, 'cost': 1}}
        best = min(scores.values(), key=lambda s: s['cost'])
        optimal_xd, optimal_yd = best['x'], best['y']

    # Otherwise is actively pressuring the closest group
    else:
        print("Herding sheep")
        # get points in an arc around the sheep
        angle_a = np.arccos((radius_sheep**2 + d**2 - radius_d**2) / (2 * radius_sheep * d))
        angle_start = np.arctan2(yd - ymean, xd - xmean)
        angle_range = (angle_start - angle_a, angle_start + angle_a)
        random_angles = np.random.uniform(angle_range[0], angle_range[1], n_candidates)
        points = np.asarray([xmean, ymean]) + radius_sheep * np.column_stack([np.cos(random_angles), np.sin(random_angles)])

        # set up simulation
        sheep_states = [{'position': [x, y], 'velocity': [0, 0]} for x, y in zip(x, y)]
        sheepdog_state = {'position': [xd, yd], 'velocity': [0, 0]}
        simulation = Simulation(field_boundary, 800, 600, sheep_states=sheep_states, sheepdog_state=sheepdog_state)

        # optimise new dog position
        last_update = 0
        optimal_xd, optimal_yd, optimal_cost = xd, yd, cost(x, y, xd, yd, xc, yc, simulation)
        scores = dict()
        for i, (new_xd, new_yd) in enumerate(points):
            scores[i] = {'x': new_xd, 'y': new_yd, 'cost': -1}

            # Check if point is within map
            map_polygon = Path(np.array(field_boundary))
            if not map_polygon.contains_point((new_xd, new_yd)):
                scores[i]['cost'] = -2
                continue

            # Calculate cost of point using simulation
            new_cost = cost(x, y, new_xd, new_yd, xc, yc, simulation)
            scores[i]['cost'] = new_cost

            # If cost is better, reject old cost
            if new_cost < optimal_cost:
                optimal_cost = new_cost
                optimal_xd, optimal_yd = new_xd, new_yd
                last_update += 1
                print(f"Best Cost: {optimal_cost}")

            # Exit once min threshold is completed
            #if i-last_update > early_exit_threshold:
            #    break

        best = min(scores.values(), key=lambda s: s['cost'])
        optimal_xd, optimal_yd = best['x'], best['y']

    # publish the options for points
    if targets_pub:
        render_targets_points(targets_pub, scores, best)
    return optimal_xd, optimal_yd

def pure_pursuit(dog_xy, target_xy, lookahead=2.0, step=0.5):
    """
    Minimal pure-pursuit helper.

    Parameters
    ----------
    dog_xy      : (x, y) tuple – current dog position.
    target_xy   : (x, y) tuple – look-ahead goal on the path.
    lookahead   : float – distance the controller ‘looks’ ahead (m).
    step        : float – how far the dog moves this control cycle (m).

    Returns
    -------
    x_next, y_next  : the next set-point for the dog (often just published
                      as a PoseStamped).
    """
    xd, yd   = dog_xy
    xt, yt   = target_xy

    # 1.  distance and heading to the look-ahead point
    dx, dy   = xt - xd, yt - yd
    dist     = maths.hypot(dx, dy)

    if dist < 1e-6:        # already there → hold position
        return xt, yt, None

    # 2.  normalised direction vector
    ux, uy   = dx / dist, dy / dist

    # 3.  advance by `step` (or full distance if closer)
    move     = min(step, dist)
    x_next   = xd + ux * move
    y_next   = yd + uy * move

    # (optional) diagnostics – could plot candidate points
    dbg_pts  = np.array([[xt, yt], [x_next, y_next]])

    return x_next, y_next, dbg_pts


def plot_current_state(x, y, xd, yd, xc, yc, optimal_xd, optimal_yd, radius_d=1.5):
    (xmean, ymean), radius_sheep = circle_around_points(np.stack([x, y], axis=1))

    fig, ax = plt.subplots()
    fig.set_figheight(6)
    fig.set_figwidth(6)
    plt.scatter(x, y)
    plt.scatter([xmean], [ymean], s=1000, alpha=0.5)
    xvel, yvel = get_direction(x, y, xd, yd)
    xvel, yvel = normalise_velocities(xvel, yvel)
    plt.arrow(xmean, ymean, xvel, yvel)

    xveldesired, yveldesired = xc - xmean, yc - ymean
    xveldesired, yveldesired = normalise_velocities(xveldesired, yveldesired)
    plt.arrow(xmean, ymean, xveldesired, yveldesired)

    print(angle(xvel, yvel, xveldesired, yveldesired))

    plt.scatter([xd],[yd])
    plt.scatter([xc],[yc], s=10000, alpha=0.2)

    circle_a = plt.Circle((xd, yd), radius_d, color='r', fill=False, label='Circle A')
    circle_b = plt.Circle((xmean, ymean), radius_sheep, color='b', fill=False, label='Circle B')
    ax.add_patch(circle_a)
    ax.add_patch(circle_b)
    plt.scatter([optimal_xd], [optimal_yd], s=100, alpha=0.5, c="r")


    plt.xlim(0,10)
    plt.ylim(0,10)
    plt.savefig("tmp.png")
    plt.close()
    im=cv2.imread("tmp.png")
    cv2.imshow("win", im)
    cv2.waitKey(1)
