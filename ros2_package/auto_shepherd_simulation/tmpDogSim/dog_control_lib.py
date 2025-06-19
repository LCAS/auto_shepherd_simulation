
import math as maths
import cv2
import matplotlib.pyplot as plt
from matplotlib.path import Path
import numpy as np
from sklearn.cluster import DBSCAN
from auto_shepherd_simulation.sheep_simulation.simulation import Simulation
from auto_shepherd_simulation.utils.geo_converter import load_coords_from_yaml, MapConverter

try:
    mc = MapConverter(load_coords_from_yaml("/home/ros/map/map1.yaml"))
except:
    mc = MapConverter(load_coords_from_yaml("../configs/map/map1.yaml"))

map_polygon = Path(np.array(mc.map_coords_xy_meters))
#check if points are valid using `map_polygon.contains_point(point)`

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


def find_best_dog_position(x, y, xd, yd, xc, yc, field_boundary,  # ← flock, dog, goal
                           radius_d=1.4, n_candidates=15, early_exit_threshold=5,
                           default_goto=np.asarray((0,0))):
    """Return optimal dog (x_d*, y_d*) given current flock and goal."""

    points = np.stack([x, y], axis=1)
    goal_point = np.array([xc,yc])

    db = DBSCAN(eps=10, min_samples=1).fit(points)
    labels = db.labels_
    furthest_distance, furthest_cluster = -1, -1
    print(f"{len(np.unique(labels))} clusters found")
    for cluster in np.unique(labels):
        centre_of_cluster = np.mean(points[labels==cluster],axis=0)
        distance_to_goal = np.linalg.norm(centre_of_cluster-goal_point)
        if distance_to_goal > furthest_distance:
            furthest_distance, furthest_cluster = distance_to_goal, cluster
    points = points[labels==furthest_cluster]

    (xmean, ymean), radius_sheep = circle_around_points(points)
    radius_sheep += .05 # ensure single sheep clusters have a radius
    
    d = np.linalg.norm(np.asarray([xmean, ymean]) - np.asarray([xd, yd]))
    if np.linalg.norm(np.asarray([xmean, ymean]) - np.asarray([xc, yc])) < 5:
        d = np.linalg.norm(np.asarray([-10, 10]) - np.asarray([xd, yd]))
        closest_point = (xd + (-10 - xd) * radius_d / d, yd + (10 - yd) * radius_d / d)
        points = np.array([closest_point])
        optimal_xd, optimal_yd = closest_point
    elif d > radius_d + radius_sheep:
        print("Moving towards sheep")
        closest_point = (xd + (xmean - xd) * radius_d / d, yd + (ymean - yd) * radius_d / d)
        points = np.array([closest_point])
        optimal_xd, optimal_yd = closest_point
    elif d < abs(radius_d - radius_sheep):
        print("Moving away from sheep")
        d = np.linalg.norm(default_goto - np.asarray([xd, yd]))
        closest_point = (xd + (default_goto[0] - xd) * radius_d / d, yd + (default_goto[1] - yd) * radius_d / d)
        points = np.array([closest_point])
        optimal_xd, optimal_yd = closest_point
    else:
        print("Herding sheep")
        # get points around sheep
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
        for i, (new_xd, new_yd) in enumerate(points):
            if not map_polygon.contains_point((new_xd, new_yd)): continue
            new_cost = cost(x, y, new_xd, new_yd, xc, yc, simulation)
            if new_cost < optimal_cost:
                optimal_cost = new_cost
                optimal_xd, optimal_yd = new_xd, new_yd
                last_update += 1
                print(f"Best Cost: {optimal_cost}")
            if i-last_update > early_exit_threshold: break
    if not map_polygon.contains_point((optimal_xd, optimal_yd)): optimal_xd, optimal_yd = xd, yd
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
