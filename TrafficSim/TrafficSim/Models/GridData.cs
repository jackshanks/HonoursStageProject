namespace TrafficSim.Models;

/// <summary>
/// JSON entry for the entire grid
/// </summary>
public class GridData
{
    public int GridWidth { get; set; }
    public int GridHeight { get; set; }
    public double CellSizeMeters { get; set; }
    public List<CellData> Cells { get; set; } = [];
}

/// <summary>
/// JSON entry for a single cell
/// </summary>
public class CellData
{
    public int X { get; set; }
    public int Y { get; set; }
    public CellType Type { get; set; }
    public TrafficDirection Direction { get; set; }
    public JunctionType JunctionType { get; set; }
    public int SpeedLimitMph { get; set; }
    public List<TrafficDirection> GiveWayDirections { get; set; } = [];
}
