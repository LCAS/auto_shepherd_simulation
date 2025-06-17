import pygame
import sys
import random
from sheep import Sheep
from sheepdog import SheepDog, SheepDogController

# Initialize Pygame
pygame.init()

# Screen dimensions
WIDTH = 800
HEIGHT = 600

# Colors
BLACK = (0, 0, 0)
GREEN = (34, 139, 34)  # Grass color
WHITE = (255, 255, 255)
GRAY = (128, 128, 128)

# Create the screen
screen = pygame.display.set_mode((WIDTH, HEIGHT))
pygame.display.set_caption("Sheep Flocking Simulation")

# Slider class
class Slider:
    def __init__(self, x, y, width, height, min_val, max_val, initial_val, name):
        self.rect = pygame.Rect(x, y, width, height)
        self.min_val = min_val
        self.max_val = max_val
        self.value = initial_val
        self.name = name
        self.dragging = False
        self.handle_rect = pygame.Rect(x + (initial_val - min_val) / (max_val - min_val) * width - 5,
                                     y - 5, 10, height + 10)

    def draw(self, screen):
        # Draw slider track
        pygame.draw.rect(screen, GRAY, self.rect)
        # Draw handle
        pygame.draw.rect(screen, WHITE, self.handle_rect)
        # Draw value text
        font = pygame.font.Font(None, 24)
        text = font.render(f"{self.name}: {self.value:.1f}", True, WHITE)
        screen.blit(text, (self.rect.x, self.rect.y - 20))

    def handle_event(self, event):
        if event.type == pygame.MOUSEBUTTONDOWN:
            if self.handle_rect.collidepoint(event.pos):
                self.dragging = True
        elif event.type == pygame.MOUSEBUTTONUP:
            self.dragging = False
        elif event.type == pygame.MOUSEMOTION and self.dragging:
            rel_x = max(0, min(event.pos[0] - self.rect.x, self.rect.width))
            self.value = self.min_val + (rel_x / self.rect.width) * (self.max_val - self.min_val)
            self.handle_rect.x = self.rect.x + rel_x - 5

# Create sliders
sliders = {
    'alignment': Slider(50, HEIGHT - 100, 200, 10, 0, 2, 1.0, "Alignment"),
    'cohesion': Slider(50, HEIGHT - 70, 200, 10, 0, 2, 0.23, "Cohesion"),
    'separation': Slider(50, HEIGHT - 40, 200, 10, 0, 10, 6, "Separation")
}

# Create a list of sheep with random positions
num_sheep = 50
sheep_list = []
for _ in range(num_sheep):
    # Add some margin from the edges to avoid immediate boundary effects
    margin = 50
    x = random.uniform(margin, WIDTH - margin)
    y = random.uniform(margin, HEIGHT - margin)
    sheep_list.append(Sheep(x, y, WIDTH, HEIGHT))

# Create the sheepdog and its controller
sheepdog = SheepDog(WIDTH/2, HEIGHT/2)
sheepdog_controller = SheepDogController(sheepdog, WIDTH, HEIGHT)

# Main game loop
clock = pygame.time.Clock()
running = True

# Instructions font
font = pygame.font.Font(None, 24)
instructions = [
    "Use WASD or Arrow Keys to move the dog",
    "ESC to quit"
]

while running:
    # Handle events
    for event in pygame.event.get():
        if event.type == pygame.QUIT:
            running = False
        elif event.type == pygame.KEYDOWN:
            if event.key == pygame.K_ESCAPE:
                running = False
        
        # Handle slider events
        for slider in sliders.values():
            slider.handle_event(event)

    # Get keyboard state for continuous movement
    keys = pygame.key.get_pressed()
    
    # Update sheepdog position based on keyboard input
    sheepdog_controller.update(keys)

    # Fill the screen with grass color
    screen.fill(GREEN)

    # Update and draw each sheep
    for sheep in sheep_list:
        # Update weights based on slider values
        sheep.alignment_weight = sliders['alignment'].value
        sheep.cohesion_weight = sliders['cohesion'].value
        sheep.separation_weight = sliders['separation'].value
        
        sheep.flock(sheep_list, sheepdog)
        sheep.update()
        sheep.draw(screen)

    # Draw the sheepdog
    sheepdog.draw(screen)

    # Draw sliders
    for slider in sliders.values():
        slider.draw(screen)

    # Draw instructions
    for i, text in enumerate(instructions):
        text_surface = font.render(text, True, WHITE)
        screen.blit(text_surface, (WIDTH - 250, 20 + i * 25))

    # Update the display
    pygame.display.flip()

    # Cap the frame rate
    clock.tick(60)

# Quit Pygame
pygame.quit()
sys.exit() 