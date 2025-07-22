import pygame
import math
import numpy as np

class SheepDog:
    def __init__(self, position, velocity=None, yaw=0, coord_transformer=None):
        # State
        self.x = position[0]  # x position in meters
        self.y = position[1]  # y position in meters
        self.yaw = yaw  # Direction in radians
        
        # Velocity
        if velocity is None:
            velocity = [0, 0]
        self.velocity = velocity
        
        # Physical properties
        self.size = 0.5  # Size in meters
        self.color = (255, 192, 203)  # Pink color for Babe
        
        # Coordinate transformer
        self.coord_transformer = coord_transformer

    def set_position(self, x, y):
        """Set the position while keeping within field bounds"""
        if self.coord_transformer is None:
            raise ValueError("Coordinate transformer must be set before setting position")
            
        # Check if new position is within field boundary
        if self.coord_transformer.is_point_in_field(x, y):
            self.x = x
            self.y = y
        else:
            # If outside boundary, find closest point on boundary
            closest_point = self.coord_transformer.get_closest_boundary_point(x, y)
            self.x = closest_point[0]
            self.y = closest_point[1]

    def get_state(self):
        """Return current state in the standard format"""
        return {
            'position': [self.x, self.y],
            'velocity': self.velocity
        }

    def draw(self, screen):
        # Convert world coordinates to screen coordinates
        screen_x, screen_y = self.coord_transformer.world_to_screen(self.x, self.y)
        screen_size = self.size * self.coord_transformer.scale
        
        # Create a surface for the dog
        dog_surface = pygame.Surface((screen_size * 2, screen_size * 2), pygame.SRCALPHA)
        
        # Draw the body (oval)
        pygame.draw.ellipse(dog_surface, self.color, (0, 0, screen_size * 2, screen_size * 2))
        
        # Draw the snout (smaller oval)
        snout_color = (255, 182, 193)  # Lighter pink for snout
        pygame.draw.ellipse(dog_surface, snout_color, 
                          (screen_size * 1.2, screen_size * 0.8, 
                           screen_size * 0.8, screen_size * 0.6))
        
        # Draw the ears (triangles)
        ear_color = (255, 182, 193)  # Lighter pink for ears
        pygame.draw.polygon(dog_surface, ear_color, [
            (screen_size * 0.3, screen_size * 0.3),
            (screen_size * 0.5, screen_size * 0.1),
            (screen_size * 0.7, screen_size * 0.3)
        ])
        pygame.draw.polygon(dog_surface, ear_color, [
            (screen_size * 1.3, screen_size * 0.3),
            (screen_size * 1.5, screen_size * 0.1),
            (screen_size * 1.7, screen_size * 0.3)
        ])
        
        # Draw the eyes (small circles)
        eye_color = (0, 0, 0)  # Black for eyes
        pygame.draw.circle(dog_surface, eye_color, 
                         (int(screen_size * 0.7), int(screen_size * 0.7)), 
                         int(screen_size * 0.1))
        pygame.draw.circle(dog_surface, eye_color, 
                         (int(screen_size * 1.3), int(screen_size * 0.7)), 
                         int(screen_size * 0.1))
        
        # Draw the nostrils (small circles)
        nostril_color = (0, 0, 0)  # Black for nostrils
        pygame.draw.circle(dog_surface, nostril_color, 
                         (int(screen_size * 1.3), int(screen_size * 1.0)), 
                         int(screen_size * 0.05))
        pygame.draw.circle(dog_surface, nostril_color, 
                         (int(screen_size * 1.5), int(screen_size * 1.0)), 
                         int(screen_size * 0.05))

        # Convert yaw to degrees for pygame rotation
        angle_degrees = math.degrees(self.yaw)
        rotated_dog = pygame.transform.rotate(dog_surface, angle_degrees)
        
        # Get the rect of the rotated surface
        dog_rect = rotated_dog.get_rect(center=(screen_x, screen_y))
        
        # Draw the rotated dog
        screen.blit(rotated_dog, dog_rect)

class SheepDogController:
    def __init__(self, sheepdog, width, height):
        self.sheepdog = sheepdog
        self.max_speed = 8.0  # Maximum speed in meters per second
        self.min_speed = 0
        self.current_speed = 0
        self.acceleration = 6.0 # meters per second per second
        self.deceleration = 6.0  # meters per second per second
        self.rotation_speed = 0.1  # Radians per frame

    def update(self, keys, dt):
        """Update sheepdog position and velocity
        
        Args:
            keys: Dictionary of pressed keys
            dt: Time step in seconds
        """
        # Skip update if dt is too small
        if dt < 0.001:  # Skip updates smaller than 1ms
            return

        # Handle rotation
        if keys[pygame.K_LEFT] or keys[pygame.K_a]:
            self.sheepdog.yaw += self.rotation_speed
        if keys[pygame.K_RIGHT] or keys[pygame.K_d]:
            self.sheepdog.yaw -= self.rotation_speed

        # Handle speed control
        if keys[pygame.K_UP] or keys[pygame.K_w]:
            # Increase speed
            self.current_speed = min(self.max_speed, self.current_speed + self.acceleration * dt)
        elif keys[pygame.K_DOWN] or keys[pygame.K_s]:
            # Decrease speed
            self.current_speed = max(self.min_speed, self.current_speed - self.deceleration * dt)
        else:
            # Natural deceleration when no keys are pressed
            self.current_speed = max(self.min_speed, self.current_speed - self.deceleration * 0.5 * dt)

        # Calculate movement based on current speed and direction
        dx = math.cos(self.sheepdog.yaw) * self.current_speed * dt
        dy = -math.sin(self.sheepdog.yaw) * self.current_speed * dt

        # Update velocity (in meters per second)
        self.sheepdog.velocity = [self.current_speed * math.cos(self.sheepdog.yaw),
                                -self.current_speed * math.sin(self.sheepdog.yaw)]

        # Update position using the sheepdog's set_position method
        self.sheepdog.set_position(self.sheepdog.x + dx, self.sheepdog.y + dy) 