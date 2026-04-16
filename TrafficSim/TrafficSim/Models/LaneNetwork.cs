namespace TrafficSim.Models;

public class LaneNetwork
{
    private readonly Dictionary<Guid, TrafficNode> _nodes = new();
    private readonly Dictionary<Guid, Lane> _lanes = new();
    
    private readonly Dictionary<(int, int), TrafficNode> _cellToNode = new();
    
    public IReadOnlyCollection<TrafficNode> Nodes => _nodes.Values;

    public IEnumerable<TrafficNode> SpawnNodes => _nodes.Values.Where(n => n.Enums == Enums.Spawn);

    public IEnumerable<TrafficNode> ExitNodes => _nodes.Values.Where(n => n.Enums == Enums.Exit);

    public double CellSizeMeters { get; init; } = 4.0;

    public List<TrafficLightController> TrafficLightControllers { get; } = [];
    
    public void AddNode(TrafficNode node)
    {
        _nodes[node.Id] = node;
        _cellToNode[(node.GridX, node.GridY)] = node;
    }
    
    public void AddLane(Lane lane)
    {
        _lanes[lane.Id] = lane;
    }
    
    public TrafficNode? GetNodeAt(int gridX, int gridY)
    {
        return _cellToNode.GetValueOrDefault((gridX, gridY));
    }

    public void Clear()
    {
        _nodes.Clear();
        _lanes.Clear();
        _cellToNode.Clear();
        TrafficLightControllers.Clear();
    }
    
    public (int nodeCount, int laneCount, int straightLanes, int curvedLanes) GetStats()
    {
        var straightCount = 0;
        var curvedCount = 0;
        foreach (var lane in _lanes.Values)
        {
            switch (lane.Type)
            {
                case LaneType.Straight:
                    straightCount++;
                    break;
                case LaneType.Curved:
                    curvedCount++;
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        return (_nodes.Count, _lanes.Count, straightCount, curvedCount);
    }
}