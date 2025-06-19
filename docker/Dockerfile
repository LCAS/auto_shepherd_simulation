# ──────────────────────────────
# 1. Base – ROS 2 Humble
# ──────────────────────────────
FROM osrf/ros:humble-desktop-full

RUN apt-get update && apt-get install -y --no-install-recommends gnupg2 \
    && apt-key adv --keyserver keyserver.ubuntu.com --recv-keys F42ED6FBAB17C654

RUN echo "deb http://packages.ros.org/ros2-testing/ubuntu $(lsb_release -cs) main" \
    | tee /etc/apt/sources.list.d/ros2-testing.list

ARG  DEBIAN_FRONTEND=noninteractive
ARG  USERNAME=ros
ARG  UID=1000
ARG  GID=1000

# ──────────────────────────────
# 2. Non‑root user with sudo
# ──────────────────────────────

RUN apt-get update && apt-get install -y sudo git \
    && addgroup --gid ${GID} ${USERNAME} \
    && adduser  --uid ${UID} --gid ${GID} --disabled-password --gecos "" ${USERNAME} \
    && echo "${USERNAME} ALL=(ALL) NOPASSWD:ALL" >> /etc/sudoers \
    && addgroup dialout || true && usermod -a -G dialout ${USERNAME}

# ──────────────────────────────
# 3. Empty workspace skeleton for submodules
#    (sources come in as volumes at run‑time)
# ──────────────────────────────

# 1. Empty workspace skeleton for base drivers
ENV BASE_WS=/home/${USERNAME}/base_ws
RUN mkdir -p ${BASE_WS}/src \
    && chown -R ${USERNAME}:${USERNAME} ${BASE_WS}

# ──────────────────────────────
# 4. Empty directory for storing bash scripts
#    (sources come in as volumes at run‑time)
# ──────────────────────────────
ENV BASH_SCRIPTS=/home/${USERNAME}/bash_scripts
RUN mkdir -p ${BASH_SCRIPTS} \
    && chown -R ${USERNAME}:${USERNAME} ${BASH_SCRIPTS}

# ──────────────────────────────
# 5. Empty directory for storing bash scripts
#    (sources come in as volumes at run‑time)
# ──────────────────────────────
ENV ROSBAG_DIR=/home/${USERNAME}/rosbags
RUN mkdir -p ${ROSBAG_DIR} \
    && chown -R ${USERNAME}:${USERNAME} ${ROSBAG_DIR}

# ──────────────────────────────
# 6. Toolchain + rosdep
#    (still running as root)
# ──────────────────────────────
# 1. Core build & ROS helpers
RUN apt-get update && \
    apt-get install -y --no-install-recommends \
    sudo git build-essential \
    python3 python3-pip \
    python3-colcon-common-extensions \
    python3-rosdep python3-rosdep-modules && \
    rm -rf /var/lib/apt/lists/*

# 2. Convenience CLI tools
RUN apt-get update && \
    apt-get install -y --no-install-recommends \
    vim nano htop tmux net-tools tree && \
    rm -rf /var/lib/apt/lists/*


RUN apt-get update && apt-get install -y --no-install-recommends \
    libxkbcommon-x11-0 \
    libxcb-icccm4 \
    libxcb-image0 \
    libxcb-keysyms1 \
    libxcb-randr0 \
    libxcb-render-util0 \
    libxcb-xinerama0 \
    libxcb-xinput0 \
    libxcb-xfixes0 \
    libxcb-shape0 \
    libxcb-cursor0 \
    libgl1-mesa-glx \
    libegl1 \
    libxcb-xkb1 \
    libfontconfig1 \
    libfreetype6 \
    xterm \
    # Optional: If you need to debug locale issues with Qt
    # locales \
    # locales-all \
    && apt-get clean \
    && rm -rf /var/lib/apt/lists/*


# 3. Extra ROS debs used by your workspace
RUN apt-get update && \
    apt-get install -y --no-install-recommends \
    ros-humble-launch-testing \
    ros-humble-tf-transformations \
    ros-humble-launch-pytest \
    ros-humble-ament-cmake-core \
    libasio-dev \
    ros-humble-joy-linux \
    && \
    rm -rf /var/lib/apt/lists/*

# 4. Deps used for CAN connection
RUN apt-get update && \
    apt-get install -y --no-install-recommends \
    iproute2 can-utils && \
    rm -rf /var/lib/apt/lists/*


# 5. Python pip installs
RUN python3 -m pip install --no-cache-dir --upgrade \
    pip wheel \
    setuptools==58.2.0 \
    packaging==24.2 \
    numpy==1.23.5 \
    scipy \
    matplotlib \
    pyproj \
    pygame \
    scikit-learn \
    opencv-python \
    pyserial \
    tmule==1.5.9

# ──────────────────────────────
# 7. Drop privileges
# ──────────────────────────────
USER ${USERNAME}
WORKDIR /home/${USERNAME}

# ──────────────────────────────
# 8. Friendly hook:
#    (source extension)
# ──────────────────────────────
RUN sed -i 's/^#\(force_color_prompt\)/\1/' /home/${USERNAME}/.bashrc

# ── Friendly hook: source your extension if it exists ───────────────
RUN echo '[ -f $HOME/bash_scripts/bashrc_extensions.sh ] && \
    source $HOME/bash_scripts/bashrc_extensions.sh' \
    >> /home/${USERNAME}/.bashrc

RUN echo "function s(){  tmule -c ~/bash_scripts/launcher.yaml -W 3 launch ; }" \
    >> /home/ros/.bashrc

RUN echo "function t(){  tmule -c ~/bash_scripts/launcher.yaml terminate ; }" \
    >> /home/ros/.bashrc

RUN echo "function r(){  tmule -c ~/bash_scripts/launcher.yaml -W 3 relaunch ; }" \
    >> /home/ros/.bashrc

# ──────────────────────────────
# 9. Default shell
# ──────────────────────────────
CMD ["bin/bash"]
