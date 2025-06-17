#!/bin/bash

# --- Configuration ---
COMPOSE_FILE="docker-compose.yml" # Your docker compose file name
SERVICE_NAME="ros2_dev"         # The name of your service in docker-compose.yml

# --- X11 Setup (for GUI applications like RViz) ---
# Only run this if you're on Linux and need GUI apps
# For Windows/macOS with Docker Desktop, X11 forwarding is more complex and
# might require a separate X server (e.g., VcXsrv on Windows, XQuartz on macOS)
# and possibly different DISPLAY settings.
if [[ "$OSTYPE" == "linux-gnu"* ]]; then
    echo "Setting up X11 permissions for Docker..."
    # Allow local connections to the X server
    xhost +local:docker > /dev/null 2>&1 || { echo "Warning: xhost command failed. X11 forwarding might not work."; }
else
    echo "Skipping X11 setup (not on Linux). GUI apps may require manual configuration."
fi

# --- Build the Docker Compose services ---
echo "Building Docker Compose services..."
docker compose -f "${COMPOSE_FILE}" build "${SERVICE_NAME}"
if [ $? -ne 0 ]; then
    echo "Docker Compose build failed! Aborting."
    exit 1
fi
echo "Docker Compose build successful."

# --- Check and Stop/Remove existing container (managed by docker compose) ---
# Docker Compose handles container management fairly well, but an explicit down
# ensures a clean start if a previous session was interrupted.
echo "Ensuring no previous container is running for '${SERVICE_NAME}'..."
docker compose -f "${COMPOSE_FILE}" down --remove-orphans > /dev/null 2>&1

# --- Run the Docker Compose service interactively ---
echo "Starting Docker Compose service '${SERVICE_NAME}'..."
# The '-d' flag would run in detached mode (background)
# Without '-d', it runs interactively, attaching to the container's output.
docker compose -f "${COMPOSE_FILE}" up --force-recreate --no-start "${SERVICE_NAME}"
docker compose -f "${COMPOSE_FILE}" start "${SERVICE_NAME}"
# Attach to the running container's shell
docker attach "${CONTAINER_NAME:-dogsheep_ros2_devcontainer}" # Uses the container name from compose file, or a default

echo "Docker container exited. To stop detached container run 'docker compose -f ${COMPOSE_FILE} down'."
