
import math as maths
import cv2
import matplotlib.pyplot as plt
import numpy as np
from sklearn.datasets import make_blobs

# Setup

X, y = make_blobs(n_samples=100, centers=1, n_features=2,random_state=0)
x, y = X[:,0], X[:,1]
if np.min(x) < 0: x -= np.min(x)
if np.min(y) < 0: y -= np.min(y)

xd, yd = 8, 8
radius_d = 1.5
xc, yc = np.random.random()*10, np.random.random()*10

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

def cost(x, y, xd, yd):
    # get the new direction of the flock
    xvel, yvel = get_direction(x, y, xd, yd)
    # get the angle between that and the desired direction
    xmean, ymean = np.mean(x), np.mean(y)
    xveldesired, yveldesired = xc - xmean, yc - ymean
    xveldesired, yveldesired = normalise_velocities(xveldesired, yveldesired)
    return angle(xvel, yvel, xveldesired, yveldesired) # penalise distance to closest sheep, reject if within 2m of sheep


for loop in range(10000000):
    
    print(loop)
    if loop % 100 == 0:
        xc, yc = np.random.random()*10, np.random.random()*10

    (xmean, ymean), radius_sheep = smallest_enclosing_circle(np.stack([x,y],axis=1))
    radius_sheep += .5
    # get the overlap between sheep and dog
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
        optimal_xd, optimal_yd, optimal_cost = xd, yd, cost(x, y, xd, yd)
        for i, (new_xd, new_yd) in enumerate(points):
            new_cost = cost(x, y, new_xd, new_yd)
            if new_cost < optimal_cost:
                optimal_cost = new_cost
                optimal_xd, optimal_yd = new_xd, new_yd
                last_update += 1
                print(f"Best Cost: {optimal_cost}")
            if i-last_update > 10: break

    # plot
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
    plt.scatter(points[:,0], points[:,1])
    plt.scatter([optimal_xd], [optimal_yd], s=100, alpha=0.5, c="r")


    plt.xlim(0,10)
    plt.ylim(0,10)
    plt.savefig("tmp.png")
    plt.close()
    im=cv2.imread("tmp.png")
    cv2.imshow("win", im)
    cv2.waitKey(1)

    # update values
    xd, yd = optimal_xd, optimal_yd
    xvel, yvel = get_direction(x, y, xd, yd)
    x += xvel/3
    y += yvel/3
    x = np.where(x<0,0,x)
    x = np.where(x>10,10,x)
    y = np.where(y<0,0,y)
    y = np.where(y>10,10,y)
    
    

