namespace TrafficSim.Models;

public class LaneNetwork
{
    private readonly Dictionary<Guid, TrafficNode> _nodes = new();
    private readonly Dictionary<Guid, Lane> _lanes = new();
    
    private readonly Dictionary<(int, int), TrafficNode> _cellToNode = new();
    private readonly Dictionary<(int, int), List<Lane>> _cellToLanes = new();
    
    public IReadOnlyCollection<TrafficNode> Nodes => _nodes.Values;
    public IReadOnlyCollection<Lane> Lanes => _lanes.Values;

    public double CellSizeMeters { get; set; } = 4.0;
    
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
    
    public TrafficNode? GetNearestNode(double worldX, double worldY, double maxDistance = double.MaxValue)
    {
        TrafficNode? nearest = null;
        var minDist = maxDistance;
        
        foreach (var node in _nodes.Values)
        {
            var dx = node.X - worldX;
            var dy = node.Y - worldY;
            var dist = Math.Sqrt(dx * dx + dy * dy);

            if (!(dist < minDist)) continue;
            minDist = dist;
            nearest = node;
        }
        
        return nearest;
    }
    
    public List<Lane> GetLanesInCell(int gridX, int gridY)
    {
        return _cellToLanes.TryGetValue((gridX, gridY), out var lanes) ? lanes : [];
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
        
        foreach (var cell in cellsToAdd)
        {
            if (!_cellToLanes.TryGetValue(cell, out var value))
            {
                value = [];
                _cellToLanes[cell] = value;
            }

            value.Add(lane);
        }
    }
    
    public void Clear()
    {
        _nodes.Clear();
        _lanes.Clear();
        _cellToNode.Clear();
        _cellToLanes.Clear();
    }
    
    public (int nodeCount, int laneCount, int straightLanes, int curvedLanes) GetStats()
    {
        var straightCount = _lanes.Values.Count(l => l.Type == LaneType.Straight);
        var curvedCount = _lanes.Values.Count(l => l.Type == LaneType.Curved);
        
        return (_nodes.Count, _lanes.Count, straightCount, curvedCount);
    }
}