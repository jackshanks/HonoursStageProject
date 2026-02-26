using System.Windows;
using System.Windows.Media;
using System.Windows.Shapes;

namespace TrafficSim.Models;

public class Cell(int x, int y, double cellSizeMeters = 4.0)
{
    public int X { get; } = x;
    public int Y { get; } = y;
    public double RealWorldX { get; } = x * cellSizeMeters; // X position in meters
    public double RealWorldY { get; } = y * cellSizeMeters; // Y position in meters
    public CellType Type { get; private set; } = CellType.Empty;
    public TrafficDirection Direction { get; private set; } = TrafficDirection.None;
    
    // Flag for updating a cell visually if it needs updating
    public bool IsDirty { get; set; } = true;
    
    public void SetTypeAndDirection(CellType type, TrafficDirection direction)
    {
        if (Type == type && Direction == direction) return;
        
        Type = type;
        Direction = direction;
        IsDirty = true;
    }
    
    public void SetDirection(TrafficDirection direction)
    {
        if (Direction == direction) return;
        
        Direction = direction;
        IsDirty = true;
    }
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