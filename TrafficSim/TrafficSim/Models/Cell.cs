namespace TrafficSim.Models;

/// <summary>
/// Represents a single tile on the editable grid; holds setup data before being converted to physical lanes
/// </summary>
public class Cell(int x, int y, double cellSizeMeters = 4.0)
{
    // Grid coordinates
    public int X { get; } = x;
    public int Y { get; } = y;
    
    // Real-world positions in metres (scaled from grid)
    public double RealWorldX { get; } = x * cellSizeMeters; 
    public double RealWorldY { get; } = y * cellSizeMeters; 
    
    // Type of road piece (e.g. Empty, Road, Spawn, Junction)
    public CellType Type { get; private set; } = CellType.Empty;
    
    // Travel direction for vehicles across this cell
    public TrafficDirection Direction { get; private set; } = TrafficDirection.None;
    
    // Defines junction rules (GiveWay vs TrafficLights)
    public JunctionType JunctionType { get; set; } = JunctionType.GiveWay;
    
    // Directions that must give way to others
    public HashSet<TrafficDirection> GiveWayDirections { get; } = [];
    
    // Specific turns that are banned (e.g. no right turns)
    public HashSet<(TrafficDirection From, TrafficDirection To)> BlockedTurns { get; } = [];
    
    // Speed limit in mph (defaults to standard UK urban limit)
    public int SpeedLimitMph { get; private set; } = 30;
    
    // Traffic light timings (in seconds)
    public double GreenDuration { get; set; } = 20.0;
    public double YellowDuration { get; set; } = 3.0; // Amber light length
    public double AllRedDuration { get; set; } = 1.0; // Buffer time when all lights are red
    
    // Frequency of cars spawning (if cell is a spawner)
    public double SpawnRateCarsPerMinute { get; set; } = 20.0;
    
    // Probability weight for a car picking this cell as its exit
    public double ExitWeight { get; set; } = 1.0;

    public void SetTypeAndDirection(CellType type, TrafficDirection direction)
    {
        // Skip if no change is needed
        if (Type == type && Direction == direction)
        {
            return;
        }

        Type = type;
        Direction = direction;

        // Reset speed limit if the cell is wiped clean
        if (type == CellType.Empty) 
        {
            SpeedLimitMph = 30;
        } 
    }

    public void SetDirection(TrafficDirection direction)
    {
        if (Direction == direction)
        {
            return;
        }

        Direction = direction;
    }

    public void SetSpeedLimit(int mph)
    {
        // Clamp bounds roughly between residential max and national speed limit
        SpeedLimitMph = Math.Clamp(mph, 10, 70);
    }
}