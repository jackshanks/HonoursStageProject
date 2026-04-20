namespace TrafficSim.Models;

/// <summary>
/// A point where lanes connect in the road network
/// </summary>
public class TrafficNode(double x, double y, int gridX, int gridY)
{
    public Guid Id { get; } = Guid.NewGuid();
    
    public double X { get; } = x;
    public double Y { get; } = y;

    public int GridX { get; } = gridX;
    public int GridY { get; } = gridY;

    public List<Lane> OutgoingLanes { get; } = [];
    public List<Lane> IncomingLanes { get; } = [];

    public NodeType NodeType { get; set; } = NodeType.Regular;

    public bool IsGiveWay { get; set; }
    
    // Nodes that have priority over this node
    public List<TrafficNode> PriorityNodes { get; } = [];

    public TrafficLightController? TrafficLight { get; set; }

    // The direction this node approaches a junction from
    public TrafficDirection ApproachDirection { get; set; } = TrafficDirection.None;

    // Fallback in case A* fails to find a path
    public Lane? GetNextLane()
    {
        Console.WriteLine($"Node {Id} has failed A* pathfinding, choosing random lane.");
        return OutgoingLanes.Count == 0 ? null : OutgoingLanes[Random.Shared.Next(OutgoingLanes.Count)];
    }
}