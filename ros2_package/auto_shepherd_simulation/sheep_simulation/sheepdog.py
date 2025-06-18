import pygame
import math

class SheepDog:
    def __init__(self, position, velocity=None, yaw=0):
        # State
        self.x = position[0]
        self.y = position[1]
        self.yaw = yaw  # Direction in radians
        
        # Velocity
        if velocity is None:
            velocity = [0, 0]
        self.velocity = velocity
        
        # Physical properties
        self.size = 10  # Size of the pig
        self.color = (255, 192, 203)  # Pink color for Babe
        
        # Screen boundaries
        self.width = None
        self.height = None

    def set_screen_bounds(self, width, height):
        """Set the screen boundaries for position checking"""
        self.width = width
        self.height = height

    def set_position(self, x, y):
        """Set the position while keeping within screen bounds"""
        if self.width is None or self.height is None:
            raise ValueError("Screen bounds must be set before setting position")
            
        self.x = max(self.size, min(self.width - self.size, x))
        self.y = max(self.size, min(self.height - self.size, y))

    def get_state(self):
        """Return current state in the standard format"""
        return {
            'position': [self.x, self.y],
            'velocity': self.velocity
        }

    def draw(self, screen):
        # Create a surface for the pig
        pig_surface = pygame.Surface((self.size * 2, self.size * 2), pygame.SRCALPHA)
        
        # Draw the body (oval)
        pygame.draw.ellipse(pig_surface, self.color, (0, 0, self.size * 2, self.size * 2))
        
        # Draw the snout (smaller oval)
        snout_color = (255, 182, 193)  # Lighter pink for snout
        pygame.draw.ellipse(pig_surface, snout_color, 
                          (self.size * 1.2, self.size * 0.8, 
                           self.size * 0.8, self.size * 0.6))
        
        # Draw the ears (triangles)
        ear_color = (255, 182, 193)  # Lighter pink for ears
        pygame.draw.polygon(pig_surface, ear_color, [
            (self.size * 0.3, self.size * 0.3),
            (self.size * 0.5, self.size * 0.1),
            (self.size * 0.7, self.size * 0.3)
        ])
        pygame.draw.polygon(pig_surface, ear_color, [
            (self.size * 1.3, self.size * 0.3),
            (self.size * 1.5, self.size * 0.1),
            (self.size * 1.7, self.size * 0.3)
        ])
        
        # Draw the eyes (small circles)
        eye_color = (0, 0, 0)  # Black for eyes
        pygame.draw.circle(pig_surface, eye_color, 
                         (int(self.size * 0.7), int(self.size * 0.7)), 
                         int(self.size * 0.1))
        pygame.draw.circle(pig_surface, eye_color, 
                         (int(self.size * 1.3), int(self.size * 0.7)), 
                         int(self.size * 0.1))
        
        # Draw the nostrils (small circles)
        nostril_color = (0, 0, 0)  # Black for nostrils
        pygame.draw.circle(pig_surface, nostril_color, 
                         (int(self.size * 1.3), int(self.size * 1.0)), 
                         int(self.size * 0.05))
        pygame.draw.circle(pig_surface, nostril_color, 
                         (int(self.size * 1.5), int(self.size * 1.0)), 
                         int(self.size * 0.05))

        # Convert yaw to degrees for pygame rotation
        angle_degrees = math.degrees(self.yaw)
        rotated_pig = pygame.transform.rotate(pig_surface, angle_degrees)
        
        # Get the rect of the rotated surface
        pig_rect = rotated_pig.get_rect(center=(self.x, self.y))
        
        # Draw the rotated pig
        screen.blit(rotated_pig, pig_rect)

class SheepDogController:
    def __init__(self, sheepdog, width, height):
        self.sheepdog = sheepdog
        self.sheepdog.set_screen_bounds(width, height)
        self.max_speed = 5
        self.min_speed = 0
        self.current_speed = 0
        self.acceleration = 0.2
        self.deceleration = 0.3
        self.rotation_speed = 0.1  # Radians per frame

    def update(self, keys):
        # Handle rotation
        if keys[pygame.K_LEFT] or keys[pygame.K_a]:
            self.sheepdog.yaw += self.rotation_speed
        if keys[pygame.K_RIGHT] or keys[pygame.K_d]:
            self.sheepdog.yaw -= self.rotation_speed

        # Handle speed control
        if keys[pygame.K_UP] or keys[pygame.K_w]:
            # Increase speed
            self.current_speed = min(self.max_speed, self.current_speed + self.acceleration)
        elif keys[pygame.K_DOWN] or keys[pygame.K_s]:
            # Decrease speed
            self.current_speed = max(self.min_speed, self.current_speed - self.deceleration)
        else:
            # Natural deceleration when no keys are pressed
            self.current_speed = max(self.min_speed, self.current_speed - self.deceleration * 0.5)

        # Calculate movement based on current speed and direction
        dx = math.cos(self.sheepdog.yaw) * self.current_speed
        dy = -math.sin(self.sheepdog.yaw) * self.current_speed

        # Update velocity
        self.sheepdog.velocity = [dx, dy]

        # Update position using the sheepdog's set_position method
        self.sheepdog.set_position(self.sheepdog.x + dx, self.sheepdog.y + dy) 