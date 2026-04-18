namespace TrafficSim.Models;

public enum Enums
{
    Regular,
    Spawn,
    Exit
}

public enum CellType
{
    Empty,
    Road,
    Intersection
}

public enum TrafficDirection
{
    None,
    North,
    East,
    South,
    West
}

public enum JunctionType
{
    GiveWay,
    TrafficLight
}

public enum TrafficLightPhase
{
    Green,
    Yellow,
    Red
}

public enum NodeKind
{
    Spawn,
    Exit,
    TrafficLight
}