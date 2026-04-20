namespace TrafficSim.Models;

/// <summary>
/// Structural role of a traffic node in the simulation
/// </summary>
public enum NodeType
{
    Regular, // Standard point on a road
    Spawn,   // Where vehicles enter the simulation
    Exit     // Where vehicles leave the simulation
}

/// <summary>
/// Type of grid cell laid down in the editor
/// </summary>
public enum CellType
{
    Empty,        // Grass/Nothing
    Road,         // Standard straight or curved road
    Intersection  // Junction tile
}

/// <summary>
/// Cardinal direction for tile flow
/// </summary>
public enum TrafficDirection
{
    None,
    North,
    East,
    South,
    West
}

/// <summary>
/// Mechanism controlling this junction
/// </summary>
public enum JunctionType
{
    GiveWay,     // Priority-based yielding
    TrafficLight // Timed light cycle
}

/// <summary>
/// State of a traffic light
/// </summary>
public enum TrafficLightPhase
{
    Green,
    Yellow,
    Red
}

/// <summary>
/// High-level categorization for node reporting/logic
/// </summary>
public enum NodeKind
{
    Spawn,
    Exit,
    TrafficLight
}