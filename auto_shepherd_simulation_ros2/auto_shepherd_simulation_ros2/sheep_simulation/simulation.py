import pygame
import sys
import random
import numpy as np
import yaml
import os
from auto_shepherd_simulation_ros2.sheep_simulation.sheep import Sheep
from auto_shepherd_simulation_ros2.sheep_simulation.sheepdog import SheepDog, SheepDogController



# Screen dimensions
WIDTH = 800
HEIGHT = 600

# Colors
BLACK = (0, 0, 0)
GREEN = (34, 139, 34)  # Grass color
WHITE = (255, 255, 255)
GRAY = (128, 128, 128)
LIGHTGREEN = (40, 160, 40)



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

class CoordinateTransformer:
    def __init__(self, field_boundary, screen_width, screen_height):
        """Initialize coordinate transformer with field boundary and screen dimensions

        Args:
            field_boundary: List of [x, y] points in meters defining the field boundary
            screen_width: Width of the screen in pixels
            screen_height: Height of the screen in pixels
        """
        self.field_boundary = np.array(field_boundary)

        # Calculate field bounds in real-world coordinates
        self.field_min_x = np.min(self.field_boundary[:, 0])
        self.field_max_x = np.max(self.field_boundary[:, 0])
        self.field_min_y = np.min(self.field_boundary[:, 1])
        self.field_max_y = np.max(self.field_boundary[:, 1])

        # Screen dimensions
        self.screen_width = screen_width
        self.screen_height = screen_height

        # Calculate scaling factors
        self.scale_x = screen_width / (self.field_max_x - self.field_min_x)
        self.scale_y = screen_height / (self.field_max_y - self.field_min_y)

        # Use the smaller scale to maintain aspect ratio
        self.scale = min(self.scale_x, self.scale_y)

        # Calculate offsets to center the field
        self.offset_x = (screen_width - (self.field_max_x - self.field_min_x) * self.scale) / 2
        self.offset_y = (screen_height - (self.field_max_y - self.field_min_y) * self.scale) / 2

    def world_to_screen(self, x, y):
        """Convert real-world coordinates to screen coordinates"""
        screen_x = (x - self.field_min_x) * self.scale + self.offset_x
        screen_y = (y - self.field_min_y) * self.scale + self.offset_y
        return screen_x, screen_y

    def screen_to_world(self, screen_x, screen_y):
        """Convert screen coordinates to real-world coordinates"""
        x = (screen_x - self.offset_x) / self.scale + self.field_min_x
        y = (screen_y - self.offset_y) / self.scale + self.field_min_y
        return x, y

    def is_point_in_field(self, x, y):
        """Check if a point is inside the field boundary using ray casting algorithm"""
        point = np.array([x, y])
        n = len(self.field_boundary)
        inside = False

        p1x, p1y = self.field_boundary[0]
        for i in range(n + 1):
            p2x, p2y = self.field_boundary[i % n]
            if y > min(p1y, p2y):
                if y <= max(p1y, p2y):
                    if x <= max(p1x, p2x):
                        if p1y != p2y:
                            xinters = (y - p1y) * (p2x - p1x) / (p2y - p1y) + p1x
                        if p1x == p2x or x <= xinters:
                            inside = not inside
            p1x, p1y = p2x, p2y

        return inside

    def get_closest_boundary_point(self, x, y):
        """Find the closest point on the field boundary to the given point"""
        point = np.array([x, y])
        min_dist = float('inf')
        closest_point = None

        # Check each line segment of the boundary
        for i in range(len(self.field_boundary)):
            p1 = self.field_boundary[i]
            p2 = self.field_boundary[(i + 1) % len(self.field_boundary)]

            # Vector from p1 to p2
            line_vec = p2 - p1
            # Vector from p1 to point
            point_vec = point - p1

            # Project point_vec onto line_vec
            line_len = np.linalg.norm(line_vec)
            line_unitvec = line_vec / line_len
            projection = np.dot(point_vec, line_unitvec)

            # Clamp projection to line segment
            projection = max(0, min(line_len, projection))

            # Calculate closest point on line segment
            closest = p1 + projection * line_unitvec

            # Calculate distance to closest point
            dist = np.linalg.norm(point - closest)

            if dist < min_dist:
                min_dist = dist
                closest_point = closest

        return closest_point

class Simulation:
    def __init__(self, field_boundary, screen_width=800, screen_height=600, sheep_states=None, sheepdog_state=None, spawn_random=False):
        """Initialize simulation with field boundary

        Args:
            field_boundary: List of [x, y] points in meters defining the field boundary
            screen_width: Width of the screen in pixels
            screen_height: Height of the screen in pixels
            sheep_states: Optional list of sheep states
            sheepdog_state: Optional sheepdog state
        """
        self.screen_width = screen_width
        self.screen_height = screen_height

        # Initialize coordinate transformer
        self.coord_transformer = CoordinateTransformer(field_boundary, screen_width, screen_height)

        # Create the sheepdog
        if sheepdog_state is None:
            # Place sheepdog at center of field in real-world coordinates
            center_x = (self.coord_transformer.field_max_x + self.coord_transformer.field_min_x) / 2
            center_y = (self.coord_transformer.field_max_y + self.coord_transformer.field_min_y) / 2
            sheepdog_state = {
                'position': [center_x, center_y],
                'velocity': [0, 0]
            }
        self.sheepdog = SheepDog(
            position=sheepdog_state['position'],
            velocity=sheepdog_state['velocity'],
            yaw=0,  # Initial yaw
            coord_transformer=self.coord_transformer
        )

        # Create a list of sheep
        self.num_sheep = 50 if sheep_states is None else len(sheep_states)
        self.sheep_list = []
        self._initialize_sheep(sheep_states, spawn_random=spawn_random)

        # Flocking parameters
        self.alignment_weight = 1.0
        self.cohesion_weight = 0.3
        self.separation_weight = 6.0

        # for i in range(200):
        #     self.update(0.05)

    def _initialize_sheep(self, sheep_states=None, spawn_random=False):
        """Initialize sheep with given states or random positions"""
        if spawn_random:
            print('random sheep init')
            # Initialize with random positions within field boundary
            for _ in range(self.num_sheep):
                while True:
                    # Generate random position in real-world coordinates
                    x = random.uniform(self.coord_transformer.field_min_x, self.coord_transformer.field_max_x)
                    y = random.uniform(self.coord_transformer.field_min_y, self.coord_transformer.field_max_y)

                    # Check if position is inside field boundary
                    if self.coord_transformer.is_point_in_field(x, y):
                        position = [x, y]
                        velocity = [0, 0]  # Start with zero velocity
                        self.sheep_list.append(Sheep(
                            position=position,
                            velocity=velocity,
                            coord_transformer=self.coord_transformer
                        ))
                        break
        elif sheep_states is not None:
            # Initialize with given states
            self.sheep_list = []
            for state in sheep_states:
                position = state['position']
                velocity = state.get('velocity', [0.01, 0.01])  # Default to zero velocity if not provided
                self.sheep_list.append(Sheep(
                    position=position,
                    velocity=velocity,
                    coord_transformer=self.coord_transformer
                ))

    def update(self, dt=0.02, sheepdog_state=None):
        """Update the simulation state

        Args:
            dt: Time step in seconds
            sheepdog_state: Optional dictionary with 'position' and 'velocity' keys.
                          If None, the sheepdog's current state is maintained.
        """

        # Update sheepdog state if provided
        if sheepdog_state is not None:
            if not isinstance(sheepdog_state, dict) or 'position' not in sheepdog_state:
                raise ValueError("Sheepdog state must be provided as a dictionary with 'position' key")

            self.sheepdog.set_position(sheepdog_state['position'][0], sheepdog_state['position'][1])

            if 'velocity' in sheepdog_state:
                self.sheepdog.velocity = sheepdog_state['velocity']


        # Update each sheep
        for sheep in self.sheep_list:
            sheep.update(dt, self.sheep_list, self.sheepdog)

    def get_state(self):
        """Return the current state of the simulation"""
        return {
            'sheep': [sheep.get_state() for sheep in self.sheep_list],
            'sheepdog': self.sheepdog.get_state()
        }

    def draw(self, screen):
        """Draw the simulation state"""
        # Fill the screen with grass color
        screen.fill(GREEN)

        # Draw field boundary
        screen_points = []
        for point in self.coord_transformer.field_boundary:
            screen_x, screen_y = self.coord_transformer.world_to_screen(point[0], point[1])
            screen_points.append((screen_x, screen_y))
        pygame.draw.polygon(screen, BLACK, screen_points, 2)

        # Draw each sheep
        for sheep in self.sheep_list:
            sheep.draw(screen)

        # Draw the sheepdog
        self.sheepdog.draw(screen)

class Game:
    def __init__(self, width, height):
        # Initialize Pygame
        pygame.init()
        self.width = width
        self.height = height
        self.screen = pygame.display.set_mode((width, height))
        pygame.display.set_caption("Sheep Simulation")
        self.clock = pygame.time.Clock()
        self.running = True

        # Colors
        self.BLACK = (0, 0, 0)
        self.GREEN = (34, 139, 34)  # Grass color
        self.WHITE = (255, 255, 255)
        self.GRAY = (128, 128, 128)
        self.LIGHTGREEN = (40,160,40)

        # Load map configuration
        # map_file = os.path.join(os.path.dirname(os.path.dirname(os.path.dirname(os.path.dirname(__file__)))), 'configs', 'map', 'map1.yaml')
        map_file = '/home/ros/map/map1.yaml'
        field_boundary = load_map_config(map_file)

        # Initialize simulation
        self.simulation = Simulation(field_boundary, width, height, spawn_random=True)

        # Initialize sheepdog controller
        self.sheepdog_controller = SheepDogController(self.simulation.sheepdog, width, height)

        # Initialize sliders
        self.sliders = {
            'alignment': Slider(10, height - 100, 200, 20, 0, 10, 3.0, "Alignment"),
            'cohesion': Slider(10, height - 70, 200, 20, 0, 10, 5.0, "Cohesion"),
            'separation': Slider(10, height - 40, 200, 20, 0, 10, 2.5, "Separation")
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
        # Get time step in seconds
        dt = self.clock.get_time() / 1000.0  # Convert milliseconds to seconds

        # Update simulation
        self.simulation.update(dt)

        # Update sheepdog controller
        keys = pygame.key.get_pressed()
        self.sheepdog_controller.update(keys, dt)

        # Update sliders
        for slider in self.sliders.values():
            slider.handle_event(pygame.event.Event(pygame.MOUSEMOTION, {'pos': pygame.mouse.get_pos()}))

        # Update simulation parameters
        self.simulation.alignment_weight = self.sliders['alignment'].value
        self.simulation.cohesion_weight = self.sliders['cohesion'].value
        self.simulation.separation_weight = self.sliders['separation'].value

    def draw(self):
        """Draw the game state"""
        self.screen.fill(self.GREEN)

        # Draw field boundary as a visible polygon
        boundary_points = [
            self.simulation.coord_transformer.world_to_screen(x, y)
            for x, y in self.simulation.coord_transformer.field_boundary
        ]
        pygame.draw.polygon(self.screen, self.BLACK, boundary_points, width=4)  # Thicker black outline
        pygame.draw.polygon(self.screen, self.LIGHTGREEN, boundary_points, width=0)  # Fill with light blue for visibility

        # Draw sheep
        for sheep in self.simulation.sheep_list:
            sheep.draw(self.screen)

        # Draw sheepdog
        self.simulation.sheepdog.draw(self.screen)

        # Draw sliders
        for slider in self.sliders.values():
            slider.draw(self.screen)

        # Draw instructions
        for i, text in enumerate(self.instructions):
            text_surface = self.font.render(text, True, self.WHITE)
            self.screen.blit(text_surface, (self.width - 250, 20 + i * 25))

        pygame.display.flip()

    def run(self):
        """Run the game loop"""
        while self.running:
            self.running = self.handle_events()
            self.update()
            self.draw()
            self.clock.tick(60)

        pygame.quit()
        sys.exit()

def load_map_config(map_file):
    """Load map configuration from YAML file and convert coordinates to meters"""
    with open(map_file, 'r') as f:
        config = yaml.safe_load(f)

    # Convert lat/lon to meters (approximate conversion)
    # Using a simple conversion where 1 degree of latitude ≈ 111,320 meters
    # and 1 degree of longitude ≈ 111,320 * cos(latitude) meters
    boundary_points = []
    for point in config['field_boundary']:
        lat = point['latitude']
        lon = point['longitude']
        # Convert to meters relative to the first point
        if not boundary_points:
            ref_lat = lat
            ref_lon = lon
            x = 0
            y = 0
        else:
            # Convert lat/lon differences to meters
            x = (lon - ref_lon) * 111320 * np.cos(np.radians(ref_lat))
            y = (lat - ref_lat) * 111320
        boundary_points.append([x, y])

    return boundary_points

if __name__ == "__main__":
    # Create and run the game
    game = Game(800, 600)
    game.run()
