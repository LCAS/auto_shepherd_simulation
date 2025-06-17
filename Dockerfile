# Dockerfile
# Located in the root of your project: auto_shepherd_simulation/Dockerfile

# Use a standard ROS 2 Humble base image
FROM ros:humble-ros-base-jammy

# Set environment variables for ROS 2 sourcing
ENV ROS_DISTRO humble
ENV DEBIAN_FRONTEND noninteractive

# Update apt and install essential build tools and common ROS 2 Python dependencies
RUN apt-get update && apt-get install -y \
    build-essential \
    python3-pip \
    python3-colcon-common-extensions \
    ros-humble-rclpy \
    ros-humble-geometry-msgs \
    ros-humble-std-msgs \
    # Add any other specific ros-humble-* packages that your ros_interface code uses
    # (e.g., ros-humble-tf2-ros, ros-humble-nav2-msgs, etc.)
    --no-install-recommends && \
    rm -rf /var/lib/apt/lists/*

# Create a ROS 2 workspace directory in the container
WORKDIR /ros2_ws

# Copy your 'ros_interface' package into the 'src' directory of the workspace
# The path './ros_interface' is relative to the Docker build context (which is auto_shepherd_simulation/)
COPY ros_interface ./src/ros_interface

# Install ROS dependencies for your packages within the workspace
# This command needs to source ROS 2 and be run in the correct context
RUN /bin/bash -c "source /opt/ros/$ROS_DISTRO/setup.bash && \
    rosdep update && \
    rosdep install -y --from-paths src --ignore-src --rosdistro $ROS_DISTRO --skip-keys 'ros_interface'" # --skip-keys avoids rosdep trying to install your own package

# Build your ROS 2 workspace
# This compiles C++ packages and installs Python packages (if setup.py is present)
# RUN /bin/bash -c "source /opt/ros/$ROS_DISTRO/setup.bash && \
#     colcon build --symlink-install"

# Define the default command to run when the container starts.
# It sources the main ROS 2 setup and your workspace's setup, then keeps the shell open.
# This allows you to then run your ROS 2 nodes manually inside the container.
CMD ["/bin/bash", "-c", "source /opt/ros/$ROS_DISTRO/setup.bash && source install/setup.bash && exec bash"]

# Optional: If your ROS 2 nodes expose ports (e.g., for rosbridge_server, specific network interfaces)
# EXPOSE 9090
