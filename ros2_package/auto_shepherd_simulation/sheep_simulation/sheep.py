import numpy as np
import pygame
import random

class Sheep:
    # Movement states
    GRAZING = 0  # Standing still or moving very slowly while eating
    WALKING = 1  # Normal walking speed
    MOVING = 2  # Moving faster, usually when flocking or avoiding something
    
    def __init__(self, x, y, width, height):
        self.position = np.array([x, y], dtype=float)
        self.velocity = np.random.rand(2) * 2 - 1  # Random initial velocity
        self.acceleration = np.zeros(2)
        
        # Speed parameters
        self.max_speeds = {
            self.GRAZING: 0.5,   # Very slow when grazing
            self.WALKING: 2.0,   # Normal walking speed
            self.MOVING: 4.0   # Faster speed for flocking/avoiding
        }
        self.current_state = self.WALKING
        self.max_speed = self.max_speeds[self.current_state]
        self.max_force = 0.2
        
        # State transition parameters
        self.state_timer = 0
        self.min_state_duration = 60  # Minimum frames to stay in a state
        self.grazing_chance = 0.3     # Chance to start grazing when possible
        self.grazing_duration_range = (120, 300)  # Range of frames to graze
        self.current_grazing_duration = 0  # Current grazing duration target
        
        self.width = width
        self.height = height
        self.size = 10
        self.color = (255, 255, 255)  # White color for sheep
        self.perception_radius = 50  # How far the sheep can see other sheep
        self.protected_radius = 20   # Minimum distance sheep try to maintain from each other
        self.boundary_margin = 40  # Distance from edge where sheep start to turn
        
        # Initialize weights with default values
        self.alignment_weight = 1.0
        self.cohesion_weight = 0.23
        self.separation_weight = 6.0
        self.dog_repulsion_weight = 8.0  # Weight for avoiding the dog
        self.dog_repulsion_radius = 100  # How far the sheep can sense the dog

    def update_state(self):
        self.state_timer += 1
        
        # Only consider state changes after minimum duration
        if self.state_timer < self.min_state_duration:
            return
            
        # Check if we should change state
        if self.current_state == self.GRAZING:
            # End grazing after duration
            if self.state_timer >= self.current_grazing_duration:
                self.current_state = self.WALKING
                self.state_timer = 0
        else:
            # Consider starting to graze
            if (self.current_state == self.WALKING and 
                random.random() < self.grazing_chance and 
                np.linalg.norm(self.velocity) < 0.5):  # Only graze when moving slowly
                self.current_state = self.GRAZING
                self.state_timer = 0
                self.current_grazing_duration = random.randint(*self.grazing_duration_range)
        
        # Update max speed based on current state
        self.max_speed = self.max_speeds[self.current_state]

    def apply_force(self, force):
        self.acceleration += force

    def avoid_dog(self, dog):
        """Avoid the sheepdog"""
        to_dog = self.position - np.array([dog.x, dog.y])
        distance = np.linalg.norm(to_dog)
        
        if distance < self.dog_repulsion_radius:
            # Stronger repulsion when closer to the dog
            strength = 1.0 - (distance / self.dog_repulsion_radius)
            return self.normalize(to_dog) * strength * self.dog_repulsion_weight
        
        return np.zeros(2)

    def flock(self, sheep_list, dog=None):
        alignment = self.align(sheep_list)
        cohesion = self.cohesion(sheep_list)
        separation = self.separation(sheep_list)
        dog_repulsion = self.avoid_dog(dog)

        # Apply weights to each behavior
        alignment *= self.alignment_weight
        cohesion *= self.cohesion_weight
        separation *= self.separation_weight

        # Calculate total force
        total_force = alignment + cohesion + separation + dog_repulsion
        force_magnitude = np.linalg.norm(total_force)
        
        # If force is strong enough, switch to moving
        if force_magnitude > 1.0 and self.current_state != self.GRAZING:
            self.current_state = self.MOVING
            self.max_speed = self.max_speeds[self.current_state]
        elif force_magnitude < 0.5 and self.current_state == self.MOVING:
            self.current_state = self.WALKING
            self.max_speed = self.max_speeds[self.current_state]

        self.apply_force(total_force)

    def align(self, sheep_list):
        steering = np.zeros(2)
        total = 0
        for sheep in sheep_list:
            if sheep != self:
                distance = np.linalg.norm(self.position - sheep.position)
                if distance < self.perception_radius:
                    steering += sheep.velocity
                    total += 1
        if total > 0:
            steering /= total
            steering = self.normalize(steering) * self.max_speed
            steering -= self.velocity
            steering = self.limit(steering, self.max_force)
        return steering

    def cohesion(self, sheep_list):
        steering = np.zeros(2)
        total = 0
        for sheep in sheep_list:
            if sheep != self:
                distance = np.linalg.norm(self.position - sheep.position)
                if distance < self.perception_radius:
                    steering += sheep.position
                    total += 1
        if total > 0:
            steering /= total
            return self.seek(steering)
        return steering

    def separation(self, sheep_list):
        steering = np.zeros(2)
        total = 0
        for sheep in sheep_list:
            if sheep != self:
                distance = np.linalg.norm(self.position - sheep.position)
                if distance < self.protected_radius and distance > 0:  # Only separate if within protected radius
                    # Calculate how much we need to move away
                    diff = self.position - sheep.position
                    diff = self.normalize(diff)
                    # The closer we are, the stronger the separation force
                    strength = (self.protected_radius - distance) / self.protected_radius
                    diff *= strength
                    steering += diff
                    total += 1
        if total > 0:
            steering /= total
            steering = self.normalize(steering) * self.max_speed
            steering -= self.velocity
            steering = self.limit(steering, self.max_force)
        return steering

    def seek(self, target):
        desired = target - self.position
        desired = self.normalize(desired) * self.max_speed
        steering = desired - self.velocity
        return self.limit(steering, self.max_force)

    def avoid_boundaries(self):
        steering = np.zeros(2)
        
        # Check left boundary
        if self.position[0] < self.boundary_margin:
            steering[0] += 1
        # Check right boundary
        elif self.position[0] > self.width - self.boundary_margin:
            steering[0] -= 1
            
        # Check top boundary
        if self.position[1] < self.boundary_margin:
            steering[1] += 1
        # Check bottom boundary
        elif self.position[1] > self.height - self.boundary_margin:
            steering[1] -= 1
            
        if np.any(steering != 0):
            steering = self.normalize(steering) * self.max_speed
            steering -= self.velocity
            steering = self.limit(steering, self.max_force)
            return steering
        return np.zeros(2)

    def update(self):
        # Update movement state
        self.update_state()
        
        # Apply boundary avoidance
        boundary_force = self.avoid_boundaries()
        self.apply_force(boundary_force)
        
        # Update position and velocity
        self.velocity += self.acceleration
        self.velocity = self.limit(self.velocity, self.max_speed)
        self.position += self.velocity
        self.acceleration = np.zeros(2)

        # Keep sheep within boundaries
        self.position[0] = np.clip(self.position[0], 0, self.width)
        self.position[1] = np.clip(self.position[1], 0, self.height)

        # Ensure position values are valid
        self.position = np.nan_to_num(self.position, nan=0.0)

    def draw(self, screen):
        # Draw sheep as a white circle
        pygame.draw.circle(screen, self.color, 
                         (int(self.position[0]), int(self.position[1])), 
                         self.size)
        
        # Draw state indicator
        if self.current_state == self.GRAZING:
            # Draw a small green dot when grazing
            pygame.draw.circle(screen, (0, 255, 0),
                             (int(self.position[0]), int(self.position[1])),
                             3)
        elif self.current_state == self.MOVING:
            # Draw a small red dot when moving
            pygame.draw.circle(screen, (255, 0, 0),
                             (int(self.position[0]), int(self.position[1])),
                             3)

    @staticmethod
    def normalize(vector):
        norm = np.linalg.norm(vector)
        if norm == 0:
            return vector
        return vector / norm

    @staticmethod
    def limit(vector, max_value):
        norm = np.linalg.norm(vector)
        if norm > max_value:
            return vector / norm * max_value
        return vector 