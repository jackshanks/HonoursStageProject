namespace TrafficSim.Models;

/// <summary>
/// A collection of all nodes and lanes that make up the drivable road network
/// </summary>
public class LaneNetwork
{
    private readonly Dictionary<Guid, TrafficNode> _nodes = new();
    private readonly Dictionary<Guid, Lane> _lanes = new();
    
    // Quick lookup maps to find what is at a specific grid coordinate
    private readonly Dictionary<(int, int), TrafficNode> _cellToNode = new();
    private readonly Dictionary<(int, int), Lane> _cellToLane = new();
    
    public IReadOnlyCollection<TrafficNode> Nodes => _nodes.Values;
    public IReadOnlyCollection<Lane> Lanes => _lanes.Values;

    public IEnumerable<TrafficNode> SpawnNodes => _nodes.Values.Where(n => n.NodeType == NodeType.Spawn);
    public IEnumerable<TrafficNode> ExitNodes => _nodes.Values.Where(n => n.NodeType == NodeType.Exit);

    // Scaling factor from grid to real world
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

    public void AddCellLaneMapping(int gridX, int gridY, Lane lane)
    {
        _cellToLane[(gridX, gridY)] = lane;
    }

    public void Clear()
    {
        // Wipe everything clean for a new simulation run
        _nodes.Clear();
        _lanes.Clear();
        _cellToNode.Clear();
        _cellToLane.Clear();
        TrafficLightControllers.Clear();
    }
    
    // Used mainly for debug and rendering stats
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