using TrafficSim.Models;

namespace TrafficSim.Managers;

/// <summary>
/// Builds the network as a predefined "grid" to allow the cars to run through
/// </summary>
public static class NetworkManager
{
    public static LaneNetwork BuildNetwork(Cell[,] grid, int width, int height, double cellSizeMeters)
    {
        var network = new LaneNetwork { CellSizeMeters = cellSizeMeters };
        var nodeMap = new Dictionary<(int, int), TrafficNode>();
        
        for (var x = 0; x < width; x++)
        {
            for (var y = 0; y < height; y++)
            {
                var cell = grid[x, y];
                
                if (cell.Type != CellType.Road || cell.Direction == TrafficDirection.None)
                    continue;
                
                var centerX = cell.RealWorldX + cellSizeMeters / 2.0;
                var centerY = cell.RealWorldY + cellSizeMeters / 2.0;
                
                var node = new TrafficNode(centerX, centerY, x, y);
                network.AddNode(node);
                nodeMap[(x, y)] = node;
            }
        }
        
        for (var x = 0; x < width; x++)
        {
            for (var y = 0; y < height; y++)
            {
                var cell = grid[x, y];
                
                if (cell.Type != CellType.Road || cell.Direction == TrafficDirection.None)
                    continue;
                
                if (!nodeMap.TryGetValue((x, y), out var currentNode))
                    continue;
                
                var (neighborX, neighborY) = GetNeighborCoords(x, y, cell.Direction);
                
                if (neighborX < 0 || neighborX >= width || neighborY < 0 || neighborY >= height)
                    continue;
                
                var neighborCell = grid[neighborX, neighborY];
                
                if (neighborCell.Type != CellType.Road || neighborCell.Direction == TrafficDirection.None)
                    continue;
                
                if (!CanConnect(cell.Direction, neighborCell.Direction))
                    continue;
                
                if (!nodeMap.TryGetValue((neighborX, neighborY), out var neighborNode))
                    continue;
                
                var lane = new Lane(
                    currentNode, 
                    neighborNode, 
                    cell.Direction, 
                    neighborCell.Direction
                );
                
                network.AddLane(lane);
            }
        }
        
        foreach (var node in network.Nodes)
        {
            if (node.IncomingLanes.Count == 0)
                node.Enums = Enums.Spawn;
            else if (node.OutgoingLanes.Count == 0)
                node.Enums = Enums.Exit;
        }

        return network;
    }
    
    private static (int x, int y) GetNeighborCoords(int x, int y, TrafficDirection direction)
    {
        return direction switch
        {
            TrafficDirection.North => (x, y - 1),
            TrafficDirection.South => (x, y + 1),
            TrafficDirection.East => (x + 1, y),
            TrafficDirection.West => (x - 1, y),
            _ => (x, y)
        };
    }
    
    private static bool CanConnect(TrafficDirection fromDirection, TrafficDirection toDirection)
    {
        if (fromDirection == toDirection)
            return true;
        
        // The car cannot make u turns only 90 degree turns, prevents strange loop behaviour
        return fromDirection switch
        {
            TrafficDirection.North => toDirection is TrafficDirection.East or TrafficDirection.West,
            TrafficDirection.South => toDirection is TrafficDirection.East or TrafficDirection.West,
            TrafficDirection.East => toDirection is TrafficDirection.North or TrafficDirection.South,
            TrafficDirection.West => toDirection is TrafficDirection.North or TrafficDirection.South,
            _ => false
        };
    }
    
    public static bool ValidateNetwork(LaneNetwork network)
    {
        var disconnected = network.Nodes.Count(n => 
            n.OutgoingLanes.Count == 0 && n.IncomingLanes.Count == 0);
        
        return disconnected == 0;
    }
}