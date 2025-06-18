import pygame
import sys
import random
from sheep import Sheep
from sheepdog import SheepDog, SheepDogController



# Screen dimensions
WIDTH = 800
HEIGHT = 600

# Colors
BLACK = (0, 0, 0)
GREEN = (34, 139, 34)  # Grass color
WHITE = (255, 255, 255)
GRAY = (128, 128, 128)



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

class Simulation:
    def __init__(self, width, height, sheep_states=None, sheepdog_state=None):
        self.width = width
        self.height = height
        
        # Create the sheepdog
        if sheepdog_state is None:
            sheepdog_state = {
                'position': [width/2, height/2],
                'velocity': [0, 0]
            }
        self.sheepdog = SheepDog(
            position=sheepdog_state['position'],
            velocity=sheepdog_state['velocity'],
            yaw=0  # Initial yaw
        )
        
        # Create a list of sheep
        self.num_sheep = 50 if sheep_states is None else len(sheep_states)
        self.sheep_list = []
        self._initialize_sheep(sheep_states)
        
        # Flocking parameters
        self.alignment_weight = 1.0
        self.cohesion_weight = 0.3
        self.separation_weight = 6.0

    def _initialize_sheep(self, sheep_states=None):
        """Initialize sheep with given states or random positions"""
        if sheep_states is None:
            # Initialize with random positions
            for _ in range(self.num_sheep):
                margin = 50
                position = [
                    random.uniform(margin, self.width - margin),
                    random.uniform(margin, self.height - margin)
                ]
                velocity = [0, 0]  # Start with zero velocity
                self.sheep_list.append(Sheep(
                    position=position,
                    velocity=velocity,
                    width=self.width,
                    height=self.height
                ))
        else:
            # Initialize with given states
            for state in sheep_states:
                if not isinstance(state, dict) or 'position' not in state:
                    raise ValueError("Sheep states must be provided as dictionaries with 'position' key")
                position = state['position']
                velocity = state.get('velocity', [0, 0])  # Default to zero velocity if not provided
                self.sheep_list.append(Sheep(
                    position=position,
                    velocity=velocity,
                    width=self.width,
                    height=self.height
                ))

    def update(self, sheepdog_state=None):
        """Update the simulation state
        
        Args:
            sheepdog_state: Optional dictionary with 'position' and 'velocity' keys.
                          If None, the sheepdog's current state is maintained.
        """
        # Update sheepdog state if provided
        if sheepdog_state is not None:
            if not isinstance(sheepdog_state, dict) or 'position' not in sheepdog_state:
                raise ValueError("Sheepdog state must be provided as a dictionary with 'position' key")
            self.sheepdog.x = sheepdog_state['position'][0]
            self.sheepdog.y = sheepdog_state['position'][1]
            if 'velocity' in sheepdog_state:
                self.sheepdog.velocity = sheepdog_state['velocity']
            self.sheepdog.set_screen_bounds(self.width, self.height)
        
        # Update each sheep
        for sheep in self.sheep_list:
            # Update weights
            sheep.alignment_weight = self.alignment_weight
            sheep.cohesion_weight = self.cohesion_weight
            sheep.separation_weight = self.separation_weight
            
            # Update position
            sheep.flock(self.sheep_list, self.sheepdog)
            sheep.update()

    def get_state(self):
        """Return the current state of the simulation"""
        return {
            'sheep': [sheep.get_state() for sheep in self.sheep_list],
            'sheepdog': self.sheepdog.get_state()
        }

class Game:
    def __init__(self, width, height):
        # Initialize Pygame
        pygame.init()
        
        # Screen setup
        self.width = width
        self.height = height
        self.screen = pygame.display.set_mode((width, height))
        pygame.display.set_caption("Sheep Flocking Simulation")
        
        # Colors
        self.BLACK = (0, 0, 0)
        self.GREEN = (34, 139, 34)  # Grass color
        self.WHITE = (255, 255, 255)
        self.GRAY = (128, 128, 128)
        
        # Create simulation
        self.simulation = Simulation(width, height)
        
        # Create controller for keyboard input
        self.sheepdog_controller = SheepDogController(self.simulation.sheepdog, width, height)
        
        # Create sliders Slider(x, y, width, height, min_val, max_val, initial_val, name)
        self.sliders = {
            'alignment': Slider(50, height - 100, 200, 10, 0, 10, 3.0, "Alignment"), # change from 1.0 to 2.0
            'cohesion': Slider(50, height - 70, 200, 10, 0, 10, 5.0, "Cohesion"), # change from 0.23 to 5.0
            'separation': Slider(50, height - 40, 200, 10, 0, 10, 6.0, "Separation") # keep at 6.0
        }
        
        # Instructions
        self.font = pygame.font.Font(None, 24)
        self.instructions = [
            "Use WASD or Arrow Keys to move the dog",
            "ESC to quit"
        ]

    def handle_events(self):
        """Handle pygame events"""
        for event in pygame.event.get():
            if event.type == pygame.QUIT:
                return False
            elif event.type == pygame.KEYDOWN:
                if event.key == pygame.K_ESCAPE:
                    return False
            
            # Handle slider events
            for slider in self.sliders.values():
                slider.handle_event(event)
        return True

    def update(self):
        """Update game state"""
        # Get keyboard state for continuous movement
        keys = pygame.key.get_pressed()
        
        # Update sheepdog position based on keyboard input
        self.sheepdog_controller.update(keys)
        
        # Get current sheepdog state
        sheepdog_state = self.simulation.sheepdog.get_state()
        
        # Update simulation with current sheepdog state
        self.simulation.update(sheepdog_state)
        
        # Update simulation parameters from sliders
        self.simulation.alignment_weight = self.sliders['alignment'].value
        self.simulation.cohesion_weight = self.sliders['cohesion'].value
        self.simulation.separation_weight = self.sliders['separation'].value

    def draw(self):
        """Draw the game state"""
        # Fill the screen with grass color
        self.screen.fill(self.GREEN)

        # Draw each sheep
        for sheep in self.simulation.sheep_list:
            sheep.draw(self.screen)

        # Draw the sheepdog
        self.simulation.sheepdog.draw(self.screen)

        # Draw sliders
        for slider in self.sliders.values():
            slider.draw(self.screen)

        # Draw instructions
        for i, text in enumerate(self.instructions):
            text_surface = self.font.render(text, True, self.WHITE)
            self.screen.blit(text_surface, (self.width - 250, 20 + i * 25))

        # Update the display
        pygame.display.flip()

    def run(self):
        """Run the game loop"""
        clock = pygame.time.Clock()
        running = True

        while running:
            running = self.handle_events()
            self.update()
            self.draw()
            clock.tick(60)

        pygame.quit()
        sys.exit()

if __name__ == "__main__":
    # Create and run the game
    game = Game(800, 600)
    game.run() 