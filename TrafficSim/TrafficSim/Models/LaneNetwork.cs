namespace TrafficSim.Models;

public class LaneNetwork
{
    private readonly Dictionary<Guid, TrafficNode> _nodes = new();
    private readonly Dictionary<Guid, Lane> _lanes = new();
    
    private readonly Dictionary<(int, int), TrafficNode> _cellToNode = new();
    
    public IReadOnlyCollection<TrafficNode> Nodes => _nodes.Values;

    public double CellSizeMeters { get; init; } = 4.0;
    
    public void AddNode(TrafficNode node)
    {
        _nodes[node.Id] = node;
        _cellToNode[(node.GridX, node.GridY)] = node;
    }
    
    public void AddLane(Lane lane)
    {
        _lanes[lane.Id] = lane;
        AddLaneToSpatialIndex(lane);
    }
    
    public TrafficNode? GetNodeAt(int gridX, int gridY)
    {
        return _cellToNode.GetValueOrDefault((gridX, gridY));
    }

    private void AddLaneToSpatialIndex(Lane lane)
    {
        var cellsToAdd = new HashSet<(int, int)>
        {
            (lane.StartNode.GridX, lane.StartNode.GridY),
            (lane.EndNode.GridX, lane.EndNode.GridY)
        };
        
        if (lane.Type == LaneType.Curved)
        {
            const int samples = 10;
            for (var i = 1; i < samples; i++)
            {
                var t = (double)i / samples;
                var pos = lane.GetPositionAt(t);
                
                var gridX = (int)Math.Floor(pos.X / CellSizeMeters);
                var gridY = (int)Math.Floor(pos.Y / CellSizeMeters);
                
                cellsToAdd.Add((gridX, gridY));
            }
        }
    }
    
    public void Clear()
    {
        _nodes.Clear();
        _lanes.Clear();
        _cellToNode.Clear();
    }
    
    public (int nodeCount, int laneCount, int straightLanes, int curvedLanes) GetStats()
    {
        var straightCount = 0;
        var curvedCount = 0;
        foreach (var lane in _lanes.Values)
        {
            if (lane.Type == LaneType.Straight) straightCount++;
            else if (lane.Type == LaneType.Curved) curvedCount++;
        }

        return (_nodes.Count, _lanes.Count, straightCount, curvedCount);
    }
}