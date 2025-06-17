# Sheep Flocking Simulation

This project simulates a flock of sheep using the boids algorithm, which creates emergent flocking behavior through simple rules. The simulation uses Pygame for visualization and NumPy for vector calculations.

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

1. Navigate to this directory
2. Install the required packages:
```bash
pip install -r requirements.txt
```

## Running the Simulation

To run the simulation, simply execute:
```bash
python simulation.py
```

## Controls

- ESC or close the window to exit the simulation

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