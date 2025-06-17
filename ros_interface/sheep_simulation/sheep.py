import pygame
import math
import random

class Sheep:
    # State constants
    GRAZING = 0  # Standing still, grazing
    WALKING = 1  # Moving at normal speed
    MOVING = 2   # Moving faster, usually when flocking or avoiding something

    def __init__(self, position, velocity=None, width=None, height=None):
        # State
        self.x = position[0]
        self.y = position[1]
        
        # Physics
        if velocity is None:
            velocity = [0, 0]
        self.velocity = velocity
        self.acceleration = [0, 0]
        
        # Screen boundaries
        self.width = width
        self.height = height
        
        # Movement state
        self.current_state = self.GRAZING  # Start in grazing state
        self.walking_threshold = 1.0  # Threshold to start walking
        self.moving_threshold = 2.0   # Threshold to start moving quickly
        
        # Speed multipliers for different states
        self.grazing_speed_multiplier = 0.05  # Slower when grazing
        self.walking_speed_multiplier = 0.15  # Moderate speed when walking
        self.moving_speed_multiplier = 1.2   # Faster when moving quickly
        
        # Flocking parameters
        self.alignment_weight = 1.0
        self.cohesion_weight = 0.23
        self.separation_weight = 6.0
        
        # Physical properties
        self.size = 10
        self.color = (255, 255, 255)  # White
        self.max_speed = 4
        self.max_force = 0.2
        
        # Perception parameters
        self.perception_radius = 50  # How far the sheep can see other sheep
        self.protected_radius = 20   # Minimum distance sheep try to maintain from each other
        self.boundary_margin = 40    # Distance from edge where sheep start to turn
        self.dog_repulsion_weight = 8.0  # Weight for avoiding the dog
        self.dog_repulsion_radius = 100  # How far the sheep can sense the dog

    def get_state(self):
        """Return current state in the standard format"""
        return {
            'position': [self.x, self.y],
            'velocity': self.velocity
        }

    def update_state(self):
        """Update the sheep's movement state based on external forces"""
        # Calculate the magnitude of external forces
        force_magnitude = math.sqrt(self.acceleration[0]**2 + self.acceleration[1]**2)
        
        # State transitions based purely on force magnitude
        if force_magnitude > self.moving_threshold:
            self.current_state = self.MOVING
        elif force_magnitude > self.walking_threshold:
            self.current_state = self.WALKING
        else:
            self.current_state = self.GRAZING
            # Reset velocity and acceleration when grazing
            self.velocity = [0, 0]
            self.acceleration = [0, 0]

    def apply_force(self, force):
        """Apply a force to the sheep's acceleration"""
        self.acceleration[0] += force[0]
        self.acceleration[1] += force[1]

    def seek(self, target):
        """Seek behavior - move towards a target"""
        desired = [target[0] - self.x, target[1] - self.y]
        distance = math.sqrt(desired[0]**2 + desired[1]**2)
        
        if distance > 0:
            desired[0] = (desired[0] / distance) * self.max_speed
            desired[1] = (desired[1] / distance) * self.max_speed
            
            steer = [
                desired[0] - self.velocity[0],
                desired[1] - self.velocity[1]
            ]
            
            # Limit force
            force_magnitude = math.sqrt(steer[0]**2 + steer[1]**2)
            if force_magnitude > self.max_force:
                steer[0] = (steer[0] / force_magnitude) * self.max_force
                steer[1] = (steer[1] / force_magnitude) * self.max_force
                
            return steer
        return [0, 0]

    def flee(self, target):
        """Flee behavior - move away from a target"""
        desired = [self.x - target[0], self.y - target[1]]
        distance = math.sqrt(desired[0]**2 + desired[1]**2)
        
        if distance > 0:
            desired[0] = (desired[0] / distance) * self.max_speed
            desired[1] = (desired[1] / distance) * self.max_speed
            
            steer = [
                desired[0] - self.velocity[0],
                desired[1] - self.velocity[1]
            ]
            
            # Limit force
            force_magnitude = math.sqrt(steer[0]**2 + steer[1]**2)
            if force_magnitude > self.max_force:
                steer[0] = (steer[0] / force_magnitude) * self.max_force
                steer[1] = (steer[1] / force_magnitude) * self.max_force
                
            return steer
        return [0, 0]

    def align(self, sheep_list):
        """Alignment behavior - match velocity with neighbors"""
        if not sheep_list:
            return [0, 0]
            
        avg_velocity = [0, 0]
        count = 0
        
        for sheep in sheep_list:
            if sheep != self:
                distance = math.sqrt((self.x - sheep.x)**2 + (self.y - sheep.y)**2)
                if distance < self.perception_radius:
                    avg_velocity[0] += sheep.velocity[0]
                    avg_velocity[1] += sheep.velocity[1]
                    count += 1
                
        if count > 0:
            avg_velocity[0] /= count
            avg_velocity[1] /= count
            
            # Normalize and scale
            magnitude = math.sqrt(avg_velocity[0]**2 + avg_velocity[1]**2)
            if magnitude > 0:
                avg_velocity[0] = (avg_velocity[0] / magnitude) * self.max_speed
                avg_velocity[1] = (avg_velocity[1] / magnitude) * self.max_speed
                
            # Calculate steering force
            steer = [
                avg_velocity[0] - self.velocity[0],
                avg_velocity[1] - self.velocity[1]
            ]
            
            # Limit force
            force_magnitude = math.sqrt(steer[0]**2 + steer[1]**2)
            if force_magnitude > self.max_force:
                steer[0] = (steer[0] / force_magnitude) * self.max_force
                steer[1] = (steer[1] / force_magnitude) * self.max_force
                
            return steer
        return [0, 0]

    def cohesion(self, sheep_list):
        """Cohesion behavior - move towards center of neighbors"""
        if not sheep_list:
            return [0, 0]
            
        center = [0, 0]
        count = 0
        
        for sheep in sheep_list:
            if sheep != self:
                distance = math.sqrt((self.x - sheep.x)**2 + (self.y - sheep.y)**2)
                if distance < self.perception_radius:
                    center[0] += sheep.x
                    center[1] += sheep.y
                    count += 1
                
        if count > 0:
            center[0] /= count
            center[1] /= count
            return self.seek(center)
        return [0, 0]

    def separation(self, sheep_list):
        """Separation behavior - avoid crowding neighbors"""
        if not sheep_list:
            return [0, 0]
            
        steer = [0, 0]
        count = 0
        
        for sheep in sheep_list:
            if sheep != self:
                dx = self.x - sheep.x
                dy = self.y - sheep.y
                distance = math.sqrt(dx**2 + dy**2)
                
                if distance > 0 and distance < self.protected_radius:
                    # Calculate repulsion force
                    dx = dx / distance
                    dy = dy / distance
                    # The closer we are, the stronger the separation force
                    strength = (self.protected_radius - distance) / self.protected_radius
                    steer[0] += dx * strength
                    steer[1] += dy * strength
                    count += 1
                    
        if count > 0:
            steer[0] /= count
            steer[1] /= count
            
            # Normalize and scale
            magnitude = math.sqrt(steer[0]**2 + steer[1]**2)
            if magnitude > 0:
                steer[0] = (steer[0] / magnitude) * self.max_speed
                steer[1] = (steer[1] / magnitude) * self.max_speed
                
            # Calculate steering force
            steer[0] -= self.velocity[0]
            steer[1] -= self.velocity[1]
            
            # Limit force
            force_magnitude = math.sqrt(steer[0]**2 + steer[1]**2)
            if force_magnitude > self.max_force:
                steer[0] = (steer[0] / force_magnitude) * self.max_force
                steer[1] = (steer[1] / force_magnitude) * self.max_force
                
        return steer

    def avoid_boundaries(self):
        """Avoid screen boundaries"""
        steer = [0, 0]
        
        # Check left boundary
        if self.x < self.boundary_margin:
            steer[0] += 1
        # Check right boundary
        elif self.x > self.width - self.boundary_margin:
            steer[0] -= 1
            
        # Check top boundary
        if self.y < self.boundary_margin:
            steer[1] += 1
        # Check bottom boundary
        elif self.y > self.height - self.boundary_margin:
            steer[1] -= 1
            
        if math.sqrt(steer[0]**2 + steer[1]**2) > 0:
            # Normalize and scale
            magnitude = math.sqrt(steer[0]**2 + steer[1]**2)
            steer[0] = (steer[0] / magnitude) * self.max_speed
            steer[1] = (steer[1] / magnitude) * self.max_speed
            
            # Calculate steering force
            steer[0] -= self.velocity[0]
            steer[1] -= self.velocity[1]
            
            # Limit force
            force_magnitude = math.sqrt(steer[0]**2 + steer[1]**2)
            if force_magnitude > self.max_force:
                steer[0] = (steer[0] / force_magnitude) * self.max_force
                steer[1] = (steer[1] / force_magnitude) * self.max_force
                
            return steer
        return [0, 0]

    def avoid_dog(self, dog):
        """Avoid the sheepdog"""
        to_dog = [self.x - dog.x, self.y - dog.y]
        distance = math.sqrt(to_dog[0]**2 + to_dog[1]**2)
        
        if distance < self.dog_repulsion_radius:
            # Stronger repulsion when closer to the dog
            strength = 1.0 - (distance / self.dog_repulsion_radius)
            # Normalize the direction
            if distance > 0:
                to_dog[0] = to_dog[0] / distance
                to_dog[1] = to_dog[1] / distance
            # Apply repulsion force
            return [
                to_dog[0] * strength * self.dog_repulsion_weight,
                to_dog[1] * strength * self.dog_repulsion_weight
            ]
        return [0, 0]

    def flock(self, sheep_list, sheepdog):
        """Apply flocking behaviors"""
        # Reset acceleration
        self.acceleration = [0, 0]
        
        # Get forces from each behavior
        align = self.align(sheep_list)
        cohere = self.cohesion(sheep_list)
        separate = self.separation(sheep_list)
        
        # Apply weights
        align[0] *= self.alignment_weight
        align[1] *= self.alignment_weight
        cohere[0] *= self.cohesion_weight
        cohere[1] *= self.cohesion_weight
        separate[0] *= self.separation_weight
        separate[1] *= self.separation_weight
        
        # Apply forces
        self.apply_force(align)
        self.apply_force(cohere)
        self.apply_force(separate)
        
        # Apply sheepdog repulsion with original behavior
        dog_force = self.avoid_dog(sheepdog)
        self.apply_force(dog_force)
        
        # Apply boundary avoidance
        boundary_force = self.avoid_boundaries()
        self.apply_force(boundary_force)
        
        # Update state based on movement
        if math.sqrt(self.velocity[0]**2 + self.velocity[1]**2) > self.max_speed * 0.8:
            self.current_state = self.MOVING
        elif math.sqrt(self.velocity[0]**2 + self.velocity[1]**2) < self.max_speed * 0.2:
            self.current_state = self.WALKING

    def update(self):
        """Update the sheep's position and state"""
        # Update state based on external forces
        self.update_state()
        
        # Update velocity based on acceleration
        self.velocity[0] += self.acceleration[0]
        self.velocity[1] += self.acceleration[1]
        
        # Limit speed based on state
        speed = math.sqrt(self.velocity[0]**2 + self.velocity[1]**2)
        if self.current_state == self.GRAZING:
            max_speed = self.max_speed * self.grazing_speed_multiplier
        elif self.current_state == self.WALKING:
            max_speed = self.max_speed * self.walking_speed_multiplier
        else:  # MOVING state
            max_speed = self.max_speed * self.moving_speed_multiplier
        
        if speed > max_speed:
            self.velocity[0] = (self.velocity[0] / speed) * max_speed
            self.velocity[1] = (self.velocity[1] / speed) * max_speed
        
        # Update position
        self.x += self.velocity[0]
        self.y += self.velocity[1]
        
        # Keep within screen bounds
        self.x = max(0, min(self.x, self.width))
        self.y = max(0, min(self.y, self.height))
        
        # Reset acceleration
        self.acceleration = [0, 0]

    def draw(self, screen):
        """Draw the sheep on the screen"""
        # Draw the sheep body
        pygame.draw.circle(screen, self.color, (int(self.x), int(self.y)), self.size)
        
        # Draw state indicator
        if self.current_state == self.GRAZING:
            # Draw a small green dot when grazing
            pygame.draw.circle(screen, (0, 255, 0), (int(self.x), int(self.y)), 3)
        elif self.current_state == self.WALKING:
            # Draw a small blue dot when walking
            pygame.draw.circle(screen, (0, 0, 255), (int(self.x), int(self.y)), 3)
        else:  # MOVING state
            # Draw a small red dot when moving quickly
            pygame.draw.circle(screen, (255, 0, 0), (int(self.x), int(self.y)), 3)

    @staticmethod
    def normalize(vector):
        norm = math.sqrt(vector[0]**2 + vector[1]**2)
        if norm == 0:
            return vector
        return [vector[0] / norm, vector[1] / norm]

    @staticmethod
    def limit(vector, max_value):
        norm = math.sqrt(vector[0]**2 + vector[1]**2)
        if norm > max_value:
            return [vector[0] / norm * max_value, vector[1] / norm * max_value]
        return vector 