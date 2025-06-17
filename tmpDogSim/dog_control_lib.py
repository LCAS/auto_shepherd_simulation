
import math as maths
import cv2
import matplotlib.pyplot as plt
import numpy as np
from sklearn.datasets import make_blobs

def smallest_enclosing_circle(points):
    center = np.mean(points, axis=0)
    radius = np.max(np.linalg.norm(points - center, axis=1))
    return center, radius

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

def cost(x, y, xd, yd, xc, yc):
    # get the new direction of the flock
    xvel, yvel = get_direction(x, y, xd, yd)
    # get the angle between that and the desired direction
    xmean, ymean = np.mean(x), np.mean(y)
    xveldesired, yveldesired = xc - xmean, yc - ymean
    xveldesired, yveldesired = normalise_velocities(xveldesired, yveldesired)
    return angle(xvel, yvel, xveldesired, yveldesired) # penalise distance to closest sheep, reject if within 2m of sheep


# --- keep your maths helpers and find_best_dog_position -----------------
def find_best_dog_position(x, y, xd, yd, xc, yc,  # ← flock, dog, goal
                           radius_d=1.5, n_candidates=30):
    """Return optimal dog (x_d*, y_d*) given current flock and goal."""
    (xmean, ymean), radius_sheep = smallest_enclosing_circle(np.stack([x, y], axis=1))
    radius_sheep += .5
    d = np.linalg.norm(np.asarray([xmean, ymean]) - np.asarray([xd, yd]))

    if d > radius_d + radius_sheep or d < abs(radius_d - radius_sheep):
        closest_point = (xd + (xmean - xd) * radius_d / d, yd + (ymean - yd) * radius_d / d)
        points = np.array([closest_point])
        optimal_xd, optimal_yd = closest_point
    else:
        # get points around sheep
        angle_a = np.arccos((radius_sheep**2 + d**2 - radius_d**2) / (2 * radius_sheep * d))
        angle_start = np.arctan2(yd - ymean, xd - xmean)
        angle_range = (angle_start - angle_a, angle_start + angle_a)
        random_angles = np.random.uniform(angle_range[0], angle_range[1], 10)
        points = np.asarray([xmean, ymean]) + radius_sheep * np.column_stack([np.cos(random_angles), np.sin(random_angles)])

        # optimise new dog position
        last_update = 0
        optimal_xd, optimal_yd, optimal_cost = xd, yd, cost(x, y, xd, yd, xc, yc)
        for i, (new_xd, new_yd) in enumerate(points):
            new_cost = cost(x, y, new_xd, new_yd, xc, yc)
            if new_cost < optimal_cost:
                optimal_cost = new_cost
                optimal_xd, optimal_yd = new_xd, new_yd
                last_update += 1
                print(f"Best Cost: {optimal_cost}")
            if i-last_update > 10: break
    return optimal_xd, optimal_yd
