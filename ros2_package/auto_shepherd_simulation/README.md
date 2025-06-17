# Sheep Flocking Simulation

This project implements a sheep flocking simulation using the boids algorithm, with a controllable sheepdog (Babe the pig) that can herd the sheep.

## Features

- Realistic flocking behavior with alignment, cohesion, and separation rules
- Smooth movement and natural-looking group dynamics
- Visual representation of sheep as white circles on a green background
- Wrapping around screen edges for continuous movement

## Requirements

- Python 3.7+
- Pygame
- NumPy

## Installation

1. Create and activate a conda environment:
```bash
conda create -n shepsim python=3.10
conda activate shepsim
```

2. Install the required packages:
```bash
pip install -r requirements.txt
```

## Running the Game

To run the interactive visualization:

```bash
python simulation.py
```

### Game Controls
- W/Up Arrow: Increase speed
- S/Down Arrow: Decrease speed
- A/Left Arrow: Rotate counterclockwise
- D/Right Arrow: Rotate clockwise
- ESC: Quit

### Adjusting Flocking Behavior
Use the sliders at the bottom of the screen to adjust:
- Alignment: How strongly sheep align their direction with neighbors
- Cohesion: How strongly sheep move toward the center of the flock
- Separation: How strongly sheep avoid crowding each other

## Using the Simulation as a Module

You can import and use the simulation without the visualization for integration with other systems:

```python
from simulation import Simulation
import math

# Create a simulation with default settings
sim = Simulation(width=800, height=600)

# Or create with custom positions
sheep_positions = [(100, 100), (200, 200), (300, 300)]
sheepdog_position = (400, 400, math.pi/4)  # (x, y, yaw)
sim = Simulation(
    width=800, 
    height=600,
    sheep_positions=sheep_positions,
    sheepdog_position=sheepdog_position
)

# Update the simulation
sim.update()

# Get the current state
state = sim.get_state()
sheep_positions = state['sheep']  # List of (x, y) tuples
sheepdog_state = state['sheepdog']  # (x, y, yaw) tuple

# Adjust flocking parameters
sim.alignment_weight = 1.5
sim.cohesion_weight = 0.3
sim.separation_weight = 5.0
```

### Simulation Parameters

The simulation can be customized with the following parameters:

- `width`, `height`: Dimensions of the simulation area
- `sheep_positions`: Optional list of (x, y) tuples for initial sheep positions
- `sheepdog_position`: Optional (x, y, yaw) tuple for initial sheepdog position
- `alignment_weight`: How strongly sheep align with neighbors (default: 1.0)
- `cohesion_weight`: How strongly sheep move toward flock center (default: 0.23)
- `separation_weight`: How strongly sheep avoid crowding (default: 6.0)

## Project Structure

- `simulation.py`: Main simulation logic and game visualization
- `sheep.py`: Sheep class implementing boids algorithm
- `sheepdog.py`: Sheepdog class and controller
- `requirements.txt`: Required Python packages

## How it Works

The simulation implements three main rules that create the flocking behavior:

1. **Alignment**: Sheep try to match the average velocity of nearby sheep
2. **Cohesion**: Sheep try to move towards the average position of nearby sheep
3. **Separation**: Sheep try to avoid crowding by maintaining a minimum distance from other sheep

These simple rules combine to create complex, emergent behavior that resembles real flocking animals.

## Customization

You can modify various parameters in the code to change the behavior:

- `num_sheep`: Number of sheep in the simulation
- `max_speed`: Maximum speed of the sheep
- `max_force`: Maximum steering force
- `perception_radius`: How far each sheep can see other sheep
- `size`: Size of the sheep circles 