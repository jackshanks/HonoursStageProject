namespace TrafficSim.Models;

public class TrafficNode(double x, double y, int gridX, int gridY)
{
    public Guid Id { get; } = Guid.NewGuid();
    public double X { get; } = x;
    public double Y { get; } = y;

    public int GridX { get; } = gridX;
    public int GridY { get; } = gridY;

    public List<Lane> OutgoingLanes { get; } = [];

    public List<Lane> IncomingLanes { get; } = [];

    public Enums Enums { get; set; } = Enums.Regular;

    // TODO: pathfinding
    public Lane? GetNextLane()
    {
        return OutgoingLanes.Count == 0 ? null : OutgoingLanes[0];
    }
}