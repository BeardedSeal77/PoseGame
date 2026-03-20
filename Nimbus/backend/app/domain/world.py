"""
World Object Registry
Static positions of all objects in Webots simulation world.
Extracted from mavic_2_pro.wbt for ground-truth navigation.
"""

from typing import Optional, Dict, List


# World object positions (extracted from mavic_2_pro.wbt)
WORLD_OBJECTS = {
    'car': {
        'name': 'Tesla Model 3',
        'type': 'vehicle',
        'position': {'x': -41.5139, 'y': 4.34169, 'z': 0.31},
        'rotation': {'x': 0, 'y': 0, 'z': 1, 'angle': -0.2618053071795865},
        'description': 'Red Tesla Model 3 on the road'
    },
    'chair': {
        'name': 'Office Chair',
        'type': 'furniture',
        'position': {'x': -25.44, 'y': -2.95, 'z': 0.0},
        'rotation': {'x': 0, 'y': 0, 'z': 1, 'angle': 0},
        'description': 'Black office chair'
    },
    'human': {
        'name': 'Pedestrian',
        'type': 'person',
        'position': {'x': -8.89, 'y': -6.67, 'z': 1.27},
        'rotation': {'x': 0, 'y': 0, 'z': 1, 'angle': 1.5708},
        'description': 'Walking pedestrian'
    },
    'cabinet': {
        'name': 'Cabinet',
        'type': 'furniture',
        'position': {'x': -31.795, 'y': 13.8306, 'z': 0.0},
        'rotation': {'x': 0, 'y': 0, 'z': 1, 'angle': -2.094395307179586},
        'description': 'Wooden cabinet'
    },
    'cardboard_box': {
        'name': 'Cardboard Box',
        'type': 'object',
        'position': {'x': -0.730157, 'y': -1.22891, 'z': 0.3},
        'rotation': {'x': 0, 'y': 0, 'z': 1, 'angle': -1.8325953071795862},
        'description': 'Brown cardboard box'
    },
    'forklift': {
        'name': 'Forklift',
        'type': 'vehicle',
        'position': {'x': -17.03, 'y': -8.12, 'z': 0.81},
        'rotation': {'x': 0, 'y': 0, 'z': 1, 'angle': 0},
        'description': 'Yellow forklift'
    },
    'manhole': {
        'name': 'Square Manhole',
        'type': 'object',
        'position': {'x': 0.0, 'y': 0.0, 'z': -0.03},
        'rotation': {'x': 0, 'y': 0, 'z': 1, 'angle': 0},
        'description': 'Manhole cover at origin'
    }
}


def get_object_position(name: str) -> Optional[Dict[str, float]]:
    """
    Get world position of a named object.
    
    Args:
        name: Object identifier (e.g., "car", "human")
    
    Returns:
        Position dict {"x": ..., "y": ..., "z": ...} or None if not found
    """
    obj = WORLD_OBJECTS.get(name)
    return obj['position'] if obj else None


def get_object_info(name: str) -> Optional[dict]:
    """
    Get full information about a named object.
    
    Args:
        name: Object identifier
    
    Returns:
        Complete object dict or None
    """
    return WORLD_OBJECTS.get(name)


def list_all_objects() -> Dict[str, dict]:
    """
    Get all world objects.
    
    Returns:
        Dictionary of all objects with their info
    """
    return WORLD_OBJECTS.copy()


def list_objects_by_type(obj_type: str) -> Dict[str, dict]:
    """
    Filter objects by type.
    
    Args:
        obj_type: Type filter ("vehicle", "furniture", "person", "object")
    
    Returns:
        Filtered dictionary of objects
    """
    return {
        name: obj for name, obj in WORLD_OBJECTS.items()
        if obj['type'] == obj_type
    }


def get_object_names() -> List[str]:
    """
    Get list of all valid object names.
    
    Returns:
        List of object identifiers
    """
    return list(WORLD_OBJECTS.keys())


def calculate_distance(pos1: dict, pos2: dict) -> float:
    """
    Calculate Euclidean distance between two positions.
    
    Args:
        pos1: First position {"x": ..., "y": ..., "z": ...}
        pos2: Second position
    
    Returns:
        Distance in meters
    """
    dx = pos2['x'] - pos1['x']
    dy = pos2['y'] - pos1['y']
    dz = pos2['z'] - pos1['z']
    return (dx**2 + dy**2 + dz**2) ** 0.5


# =============================================================================
# CHECKPOINT CONFIGURATIONS (for racing)
# =============================================================================

# Pre-defined checkpoint sequences for race modes
RACE_CHECKPOINTS = {
    'beginner': ['manhole', 'cardboard_box', 'chair'],
    'intermediate': ['manhole', 'forklift', 'cabinet', 'chair'],
    'advanced': ['manhole', 'human', 'car', 'forklift', 'cabinet'],
    'expert': ['manhole', 'cardboard_box', 'human', 'forklift', 'car', 'cabinet', 'chair']
}


def get_race_checkpoints(difficulty: str = 'beginner') -> List[str]:
    """
    Get checkpoint sequence for a race difficulty.
    
    Args:
        difficulty: Race difficulty ("beginner", "intermediate", "advanced", "expert")
    
    Returns:
        Ordered list of checkpoint names
    """
    return RACE_CHECKPOINTS.get(difficulty, RACE_CHECKPOINTS['beginner']).copy()


# =============================================================================
# USAGE EXAMPLES
# =============================================================================

if __name__ == "__main__":
    # Get position of specific object
    car_pos = get_object_position('car')
    print(f"Car position: {car_pos}")
    
    # List all objects
    all_objects = list_all_objects()
    print(f"\nTotal objects: {len(all_objects)}")
    
    # Filter by type
    vehicles = list_objects_by_type('vehicle')
    print(f"\nVehicles: {list(vehicles.keys())}")
    
    # Get checkpoint sequence
    checkpoints = get_race_checkpoints('intermediate')
    print(f"\nIntermediate race checkpoints: {checkpoints}")
    
    # Calculate distance
    pos1 = {'x': 0, 'y': 0, 'z': 0}
    pos2 = get_object_position('car')
    distance = calculate_distance(pos1, pos2)
    print(f"\nDistance from origin to car: {distance:.2f}m")
