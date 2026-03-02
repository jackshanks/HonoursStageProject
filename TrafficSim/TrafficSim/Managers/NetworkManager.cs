using TrafficSim.Models;
using static TrafficSim.Utility.TrafficDirectionUtility;

namespace TrafficSim.Managers;

/// <summary>
/// Builds the network as a predefined "grid" to allow the cars to run through
/// </summary>
public static class NetworkManager
{
    /// <summary>
    /// Builds the network from the grid of cells
    /// </summary>
    /// <param name="grid">The list of all cells</param>
    /// <param name="width">the width of the grid in cells</param>
    /// <param name="height">the height of the grid in cells</param>
    /// <param name="cellSizeMeters">The size of the cells in meters</param>
    /// <returns>The built network (considered immutable)</returns>
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
                
                // Creates a new traffic node at the centre of all road cells
                var node = new TrafficNode(centerX, centerY, x, y);
                network.AddNode(node);
                nodeMap[(x, y)] = node;
            }
        }
        
        for (var x = 0; x < width; x++)
        {
            for (var y = 0; y < height; y++)
            {
                // Connects adjacent nodes into a "lane"
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
        
        BuildJunctionLanes(grid, width, height, network);

        foreach (var node in network.Nodes)
        {
            // Sets spawn and exit nodes for spawning and exiting cars
            if (node.IncomingLanes.Count == 0)
                node.Enums = Enums.Spawn;
            else if (node.OutgoingLanes.Count == 0)
                node.Enums = Enums.Exit;
        }

        return network;
    }
    
    /// <summary>
    /// Gets the coordinates of the next cell in a direction
    /// </summary>
    /// <param name="x">x coord of the cell</param>
    /// <param name="y">y coord of the cell</param>
    /// <param name="direction">Direction to look</param>
    /// <returns>the coords of the next cell</returns>
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
    
    /// <summary>
    /// Checks if a lane can connect to another lane (Prevents U Turns)
    /// </summary>
    /// <param name="fromDirection">Enter direction</param>
    /// <param name="toDirection">Leave direction</param>
    /// <returns></returns>
    private static bool CanConnect(TrafficDirection fromDirection, TrafficDirection toDirection)
    {
        if (fromDirection == toDirection)
            return true;
        
        return fromDirection switch
        {
            TrafficDirection.North => toDirection is TrafficDirection.East or TrafficDirection.West,
            TrafficDirection.South => toDirection is TrafficDirection.East or TrafficDirection.West,
            TrafficDirection.East => toDirection is TrafficDirection.North or TrafficDirection.South,
            TrafficDirection.West => toDirection is TrafficDirection.North or TrafficDirection.South,
            _ => false
        };
    }
    
    /// <summary>
    /// Checks that there are no invalid nodes
    /// </summary>
    /// <param name="network">The built network</param>
    /// <returns>True if validated, false if not</returns>
    public static bool ValidateNetwork(LaneNetwork network)
    {
        var disconnected = network.Nodes.Count(n => n.OutgoingLanes.Count == 0 && n.IncomingLanes.Count == 0);

        return disconnected == 0;
    }
    
    /// <summary>
    /// Builds the lanes for cars to follow inside junctions
    /// </summary>
    /// <param name="grid">List of cells</param>
    /// <param name="width">Width of grid in cells</param>
    /// <param name="height">Height of grid in cells</param>
    /// <param name="network">The built network</param>
    private static void BuildJunctionLanes(Cell[,] grid, int width, int height, LaneNetwork network)
    {
        var junctionGroups = FindJunctionGroups(grid, width, height);

        foreach (var group in junctionGroups)
        {
            // Get all give-way directions in a junction group
            var groupGiveWayDirs = new HashSet<TrafficDirection>();
            foreach (var (cx, cy) in group)
            {
                foreach (var dir in grid[cx, cy].GiveWayDirections)
                {
                    groupGiveWayDirs.Add(dir);
                }
            }

            // Find all approach and exit nodes in a junction group
            var approachNodes = new List<(TrafficNode node, TrafficDirection dir)>();
            var exitNodes = new List<(TrafficNode node, TrafficDirection dir)>();

            foreach (var node in network.Nodes)
            {
                var direction = grid[node.GridX, node.GridY].Direction;

                // the next cell in the direction of travel is inside this junction box
                var (nx, ny) = GetNeighborCoords(node.GridX, node.GridY, direction);
                if (group.Contains((nx, ny)))
                {
                    approachNodes.Add((node, direction));
                }

                // the cell directly behind this node is inside the box
                var (px, py) = GetNeighborCoords(node.GridX, node.GridY, GetOpposite(direction));
                if (group.Contains((px, py)))
                {
                    exitNodes.Add((node, direction));
                }
            }

            // Mark give-way nodes and assign their priority nodes
            var priorityNodes = approachNodes .Where(a => !groupGiveWayDirs.Contains(a.dir)) .Select(a => a.node) .ToList();
            
            foreach (var (node, dir) in approachNodes)
            {
                if (!groupGiveWayDirs.Contains(dir))
                {
                    continue;
                }
                node.IsGiveWay = true;
                node.PriorityNodes.AddRange(priorityNodes);
            }

            foreach (var (approachNode, approachDir) in approachNodes)
            {
                foreach (var (exitNode, exitDir) in exitNodes)
                {
                    if (approachNode == exitNode)
                    {
                        continue;
                    }
                    if (!CanConnect(approachDir, exitDir))
                    {
                        continue;
                    }

                    // Adds a lane between each possible connection of approach and exit nodes in a junction
                    var lane = new Lane(approachNode, exitNode, approachDir, exitDir);
                    network.AddLane(lane);
                }
            }
        }
    }
    
    /// <summary>
    /// Finds all grouped junction nodes in a grid
    /// </summary>
    /// <param name="grid">The list of cells</param>
    /// <param name="width">The width of the grid in cells</param>
    /// <param name="height">The height of the grid in cells</param>
    /// <returns>A list of junction groups</returns>
    private static List<HashSet<(int, int)>> FindJunctionGroups(Cell[,] grid, int width, int height)
    {
        var visited = new HashSet<(int, int)>();
        var groups  = new List<HashSet<(int, int)>>();

        for (var x = 0; x < width; x++)
        {
            for (var y = 0; y < height; y++)
            {
                if (grid[x, y].Type != CellType.Intersection) continue;
                if (visited.Contains((x, y))) continue;

                var group = new HashSet<(int, int)>();
                var queue = new Queue<(int, int)>();
                queue.Enqueue((x, y));
                visited.Add((x, y));

                while (queue.Count > 0)
                {
                    var (cx, cy) = queue.Dequeue();
                    group.Add((cx, cy));

                    // Go through each cell around a junction node and check if it's also a junction node
                    foreach (var (nx, ny) in new[] { (cx, cy - 1), (cx, cy + 1), (cx - 1, cy), (cx + 1, cy) })
                    {
                        if (nx < 0 || nx >= width || ny < 0 || ny >= height)
                        {
                            continue;
                        }

                        if (visited.Contains((nx, ny)))
                        {
                            continue;
                        }

                        if (grid[nx, ny].Type != CellType.Intersection)
                        {
                            continue;
                        }

                        visited.Add((nx, ny));
                        queue.Enqueue((nx, ny));
                    }
                }

                groups.Add(group);
            }
        }

        return groups;
    }
}