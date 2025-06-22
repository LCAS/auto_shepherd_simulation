import math
from typing import List, Tuple, Dict, Optional
from pyproj import CRS, Transformer
import yaml
import os

# --- Global Configuration (for UTM projection) ---
# UTM Zone for Lincoln, UK is typically Zone 30N.
# EPSG:4326 is WGS84 Geographic (latitude, longitude)
# EPSG:32630 is WGS 84 / UTM Zone 30N (Easting, Northing in meters)
WGS84_CRS = CRS("EPSG:4326")
UTM_CRS = CRS("EPSG:32630") # WGS 84 / UTM Zone 30N
_transformer_wgs84_to_utm = Transformer.from_crs(WGS84_CRS, UTM_CRS, always_xy=False)
_transformer_utm_to_wgs84 = Transformer.from_crs(UTM_CRS, WGS84_CRS, always_xy=False)

class MapConverter:
    """
    A utility class to handle coordinate conversions (LatLong <-> Meters)
    relative to a fixed origin, defined by the top-left corner of a map's bounding box.
    """
    def __init__(self, map_coords_latlon: List[Tuple[float, float]]):
        if not map_coords_latlon:
            raise ValueError("map_coords_latlon cannot be empty to define a map.")

        min_lat = min(c[0] for c in map_coords_latlon)
        max_lat = max(c[0] for c in map_coords_latlon)
        min_lon = min(c[1] for c in map_coords_latlon)
        max_lon = max(c[1] for c in map_coords_latlon)

        self.bounding_box_corners_latlon = {
            'top_left': (max_lat, min_lon),
            'top_right': (max_lat, max_lon),
            'bottom_left': (min_lat, min_lon),
            'bottom_right': (min_lat, max_lon)
        }

        self.origin_lat = self.bounding_box_corners_latlon['top_left'][0]
        self.origin_lon = self.bounding_box_corners_latlon['top_left'][1]

        self.origin_utm_y, self.origin_utm_x = _transformer_wgs84_to_utm.transform(self.origin_lat, self.origin_lon)

        self.map_coords_xy_meters: List[Tuple[float, float]] = []
        for lat, lon in map_coords_latlon:
            utm_y, utm_x = _transformer_wgs84_to_utm.transform(lat, lon)

            relative_x = utm_x - self.origin_utm_x
            relative_y = utm_y - self.origin_utm_y
            self.map_coords_xy_meters.append((relative_x, relative_y))

        print(f"MapConverter initialized. Origin (Lat, Lon): ({self.origin_lat:.7f}, {self.origin_lon:.7f})")
        print(f"MapConverter Origin (UTM X, Y): ({self.origin_utm_x:.3f}, {self.origin_utm_y:.3f})")

    def get_map_data(self) -> Dict[str, any]:
        return {
            'bounding_box_corners_latlon': self.bounding_box_corners_latlon,
            'map_coords_xy_meters': self.map_coords_xy_meters,
            'origin_lat': self.origin_lat,
            'origin_lon': self.origin_lon,
            'origin_utm_x': self.origin_utm_x,
            'origin_utm_y': self.origin_utm_y,
        }

    def latlon_to_xy(self, lat: float, lon: float) -> Tuple[float, float]:
        utm_y, utm_x = _transformer_wgs84_to_utm.transform(lat, lon)
        relative_x = utm_x - self.origin_utm_x
        relative_y = utm_y - self.origin_utm_y
        return (relative_x, relative_y)

    def xy_to_latlon(self, x_meter: float, y_meter: float) -> Tuple[float, float]:
        abs_utm_x = x_meter + self.origin_utm_x
        abs_utm_y = y_meter + self.origin_utm_y
        lat, lon = _transformer_utm_to_wgs84.transform(abs_utm_y, abs_utm_x)
        return (lat, lon)


# --- Helper Function to Load from YAML ---
def load_coords_from_yaml(yaml_file_path: str) -> List[Tuple[float, float]]:
    """
    Loads latitude/longitude coordinates from a YAML file.
    Assumes YAML structure:
    field_boundary:
      - latitude: float
        longitude: float
    """
    if not os.path.exists(yaml_file_path):
        raise FileNotFoundError(f"YAML file not found: {yaml_file_path}")

    try:
        with open(yaml_file_path, 'r') as file:
            data = yaml.safe_load(file)

            if 'field_boundary' not in data or not isinstance(data['field_boundary'], list):
                raise ValueError("YAML file must contain a 'field_boundary' list.")

            coords = []
            for item in data['field_boundary']:
                if 'latitude' in item and 'longitude' in item:
                    coords.append((float(item['latitude']), float(item['longitude'])))
                else:
                    print(f"Warning: Skipping malformed point in YAML: {item}")
            return coords
    except yaml.YAMLError as e:
        raise ValueError(f"Error parsing YAML file: {e}")
    except Exception as e:
        raise ValueError(f"An unexpected error occurred while loading YAML: {e}")


if __name__ == "__main__":
    # Example for the path you previously mentioned:
    yaml_map_file_path = "/home/ros/map/map1.yaml"
    print(f"Attempting to load field coordinates from: {yaml_map_file_path}")
    try:
        field_coords_latlon = load_coords_from_yaml(yaml_map_file_path)
        print(f"Successfully loaded {len(field_coords_latlon)} coordinates from YAML.")
    except (FileNotFoundError, ValueError) as e:
        print(f"Failed to load coordinates from YAML: {e}")
        print("Please ensure the file path is correct and the YAML format matches 'field_boundary: - latitude: X - longitude: Y'.")
        print("Exiting example.")
        exit(1) # Exit if cannot load map data


    print("\n--- Case 1: Create Map Bounding Box & Convert All Coords ---")
    try:
        map_converter = MapConverter(field_coords_latlon)
        map_data = map_converter.get_map_data()

        print("\nMap Data (from Case 1):")
        print(f"  Bounding Box Corners (Lat, Lon):")
        for k, v in map_data['bounding_box_corners_latlon'].items():
            print(f"    {k}: ({v[0]:.7f}, {v[1]:.7f})")

        print(f"  Origin (Lat, Lon): ({map_data['origin_lat']:.7f}, {map_data['origin_lon']:.7f})")
        print(f"  Origin (UTM X, Y): ({map_data['origin_utm_x']:.3f}, {map_data['origin_utm_y']:.3f})")

        print("\n  Converted Map Coords (relative X, Y meters): (showing first 5)")
        for i in range(min(5, len(map_data['map_coords_xy_meters']))): # Print first 5 for brevity
            x, y = map_data['map_coords_xy_meters'][i]
            print(f"    Point {i}: X={x:.3f} m, Y={y:.3f} m")
        if len(map_data['map_coords_xy_meters']) > 5:
            print(f"    ... and {len(map_data['map_coords_xy_meters'])-5} more points.")

    except ValueError as e:
        print(f"Error during Case 1: {e}")
        map_converter = None # Ensure map_converter is not set if initialization failed


    if map_converter:
        print("\n--- Case 2: Convert New LatLong Point to XY ---")
        new_lat_lon = (53.264500000, -0.532500000) # A point somewhere near your field
        try:
            new_x, new_y = map_converter.latlon_to_xy(new_lat_lon[0], new_lat_lon[1])
            print(f"New LatLong point ({new_lat_lon[0]:.7f}, {new_lat_lon[1]:.7f}) converts to X={new_x:.3f} m, Y={new_y:.3f} m")
        except Exception as e:
            print(f"Error during Case 2: {e}")


        print("\n--- Case 3: Convert XY Point in Bounding Box to LatLong ---")
        # Let's take the very first converted point from the map for testing
        if map_data['map_coords_xy_meters']: # Ensure there are points
            test_x_meter, test_y_meter = map_data['map_coords_xy_meters'][0]
            try:
                converted_lat, converted_lon = map_converter.xy_to_latlon(test_x_meter, test_y_meter)
                print(f"XY point ({test_x_meter:.3f} m, {test_y_meter:.3f} m) converts to Lat={converted_lat:.7f}, Lon={converted_lon:.7f}")

                # Verify accuracy by comparing with original first point from field_coords_latlon
                original_first_map_lat, original_first_map_lon = field_coords_latlon[0]
                print(f"Original LatLong of this point: ({original_first_map_lat:.7f}, {original_first_map_lon:.7f})")
                print(f"Difference (Lat): {abs(original_first_map_lat - converted_lat):.10f}")
                print(f"Difference (Lon): {abs(original_first_map_lon - converted_lon):.10f}")

            except Exception as e:
                print(f"Error during Case 3: {e}")
        else:
            print("No map_coords_xy_meters available for Case 3 test.")
    else:
        print("\nSkipping Case 2 and 3 tests because MapConverter initialization failed.")
