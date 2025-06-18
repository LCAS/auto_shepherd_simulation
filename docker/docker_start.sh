#!/bin/bash

# --- Configuration ---
COMPOSE_FILE="docker-compose.yml" # Your docker compose file name
SERVICE_NAME="ros2_dev"         # The name of your service in docker-compose.yml

if [[ "$OSTYPE" == "linux-gnu"* ]]; then
    HOST_DISPLAY_VAR="$DISPLAY"
elif [[ "$OSTYPE" == "darwin"* || "$OSTYPE" == "msys"* || "$OSTYPE" == "win32"* ]]; then
    HOST_DISPLAY_VAR="host.docker.internal:0"
fi

# --- Build the Docker Compose services ---
echo "Building Docker Compose services..."
docker compose -f "${COMPOSE_FILE}" build "${SERVICE_NAME}"
if [ $? -ne 0 ]; then
    echo "Docker Compose build failed! Aborting."
    exit 1
fi
echo "Docker Compose build successful."

# --- Check and Stop/Remove existing container ---
echo "Running docker compose down to ensure a clean start..."
docker compose -f "${COMPOSE_FILE}" down --remove-orphans > /dev/null 2>&1 || true
echo "Docker Compose cleanup completed."

# --- Run the Docker Compose service interactively ---
echo "Starting Docker Compose service '${SERVICE_NAME}' in interactive mode..."

# docker compose -f "${COMPOSE_FILE}" run --rm "${SERVICE_NAME}" bash
docker compose -f "${COMPOSE_FILE}" run \
    -e "HOST_DISPLAY_VAR=${HOST_DISPLAY_VAR}" \
    # "${DOCKER_RUN_ARGS[@]}" \
    "${SERVICE_NAME}" bash

echo "Docker container session ended."
# If you want to run services in the background, you'd use 'docker compose up -d'
# and then 'docker compose exec SERVICE_NAME bash' to get a shell.
# But for a single interactive dev session, 'run --rm' is ideal.
