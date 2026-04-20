using TrafficSim.Models;
using static TrafficSim.Utility.TrafficUtility;

namespace TrafficSim.Managers;

/// <summary>
/// Converts the visual UI grid into the mathematical network of nodes and lanes for the cars
/// </summary>
public static class NetworkManager
{
    private const double MphToMps = 0.44704;
    
    /// <summary>
    /// Builds the physical network from the visual grid
    /// </summary>
    public static LaneNetwork BuildNetwork(Cell[,] grid, int width, int height, double cellSizeMeters, SimulationConfig? config = null)
    {
        var network = new LaneNetwork { CellSizeMeters = cellSizeMeters };
        var nodeMap = new Dictionary<(int, int), TrafficNode>();
        
        for (var x = 0; x < width; x++)
        {
            for (var y = 0; y < height; y++)
            {
                var cell = grid[x, y];
                
                if (cell.Type != CellType.Road || cell.Direction == TrafficDirection.None)
                {
                    continue;
                }
                
                // Only create nodes at segment boundaries (starts, ends, corners)
                if (!IsSegmentBoundary(grid, width, height, x, y, cell.Direction))
                {
                    continue;
                }
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
                {
                    continue;
                }
                if (!nodeMap.TryGetValue((x, y), out var currentNode))
                {
                    continue;
                }

                var (px, py) = GetNeighborCoords(x, y, GetOpposite(cell.Direction));
                var isSegmentStart = px < 0 || px >= width || py < 0 || py >= height
                                     || grid[px, py].Type != CellType.Road
                                     || grid[px, py].Direction != cell.Direction;

                // Trace out straight lanes
                if (isSegmentStart)
                {
                    var (cx, cy) = (x, y);
                    var (nx, ny) = GetNeighborCoords(cx, cy, cell.Direction);
                    while (nx >= 0 && nx < width && ny >= 0 && ny < height
                           && grid[nx, ny].Type == CellType.Road
                           && grid[nx, ny].Direction == cell.Direction)
                    {
                        (cx, cy) = (nx, ny);
                        (nx, ny) = GetNeighborCoords(cx, cy, cell.Direction);
                    }

                    if ((cx, cy) != (x, y) && nodeMap.TryGetValue((cx, cy), out var segEndNode))
                    {
                        var straightLane = new Lane(currentNode, segEndNode, cell.Direction, cell.Direction, cell.SpeedLimitMph * MphToMps);
                        network.AddLane(straightLane);

                        // Map intermediate cells for spatial lookup
                        var (mx, my) = GetNeighborCoords(x, y, cell.Direction);
                        while ((mx, my) != (cx, cy))
                        {
                            network.AddCellLaneMapping(mx, my, straightLane);
                            (mx, my) = GetNeighborCoords(mx, my, cell.Direction);
                        }
                    }
                }
                
                var (sx, sy) = GetNeighborCoords(x, y, cell.Direction);
                var isSegmentEnd = sx < 0 || sx >= width || sy < 0 || sy >= height
                                   || grid[sx, sy].Type != CellType.Road
                                   || grid[sx, sy].Direction != cell.Direction;

                if (!isSegmentEnd
                    || sx < 0 || sx >= width || sy < 0 || sy >= height
                    || grid[sx, sy].Type != CellType.Road
                    || grid[sx, sy].Direction == TrafficDirection.None
                    || !CanConnect(cell.Direction, grid[sx, sy].Direction)
                    || !nodeMap.TryGetValue((sx, sy), out var turnNode)) continue;
                
                var curvedLane = new Lane(currentNode, turnNode, cell.Direction, grid[sx, sy].Direction, cell.SpeedLimitMph * MphToMps);
                network.AddLane(curvedLane);
            }
        }
        
        BuildJunctionLanes(grid, width, height, network, config);

        foreach (var node in network.Nodes)
        {
            if (node.IncomingLanes.Count == 0)
            {
                node.NodeType = NodeType.Spawn;
            }
            else if (node.OutgoingLanes.Count == 0)
            {
                node.NodeType = NodeType.Exit;
            }
        }

        return network;
    }
    
    /// <summary>
    /// Checks if a cell acts as a boundary requiring a node
    /// </summary>
    private static bool IsSegmentBoundary(Cell[,] grid, int width, int height, int x, int y, TrafficDirection direction)
    {
        var (px, py) = GetNeighborCoords(x, y, GetOpposite(direction));
        var noPredecessor = px < 0 || px >= width || py < 0 || py >= height || grid[px, py].Type != CellType.Road || grid[px, py].Direction != direction;

        var (sx, sy) = GetNeighborCoords(x, y, direction);
        var noSuccessor = sx < 0 || sx >= width || sy < 0 || sy >= height || grid[sx, sy].Type != CellType.Road || grid[sx, sy].Direction != direction;

        return noPredecessor || noSuccessor;
    }

    /// <summary>
    /// Translates direction to grid offsets
    /// </summary>
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
    /// Prevents U-Turns when mapping junctions
    /// </summary>
    private static bool CanConnect(TrafficDirection fromDirection, TrafficDirection toDirection)
    {
        if (fromDirection == toDirection)
        {
            return true;
        }
        
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
    /// Ensures all nodes in the network are cleanly connected
    /// </summary>
    public static bool ValidateNetwork(LaneNetwork network)
    {
        var disconnected = network.Nodes.Count(n => n.OutgoingLanes.Count == 0 && n.IncomingLanes.Count == 0);
        return disconnected == 0;
    }
    
    /// <summary>
    /// Generates connector lanes across intersection zones
    /// </summary>
    private static void BuildJunctionLanes(Cell[,] grid, int width, int height, LaneNetwork network, SimulationConfig? config = null)
    {
        var junctionGroups = FindJunctionGroups(grid, width, height);

        foreach (var group in junctionGroups)
        {
            var (firstX, firstY) = group.First();
            var junctionType = grid[firstX, firstY].JunctionType;

            var approachNodes = new List<(TrafficNode node, TrafficDirection dir)>();
            var exitNodes = new List<(TrafficNode node, TrafficDirection dir)>();

            foreach (var node in network.Nodes)
            {
                var direction = grid[node.GridX, node.GridY].Direction;

                var (nx, ny) = GetNeighborCoords(node.GridX, node.GridY, direction);
                if (group.Contains((nx, ny)))
                {
                    approachNodes.Add((node, direction));
                }

                var (px, py) = GetNeighborCoords(node.GridX, node.GridY, GetOpposite(direction));
                if (group.Contains((px, py)))
                {
                    exitNodes.Add((node, direction));
                }
            }
            
            var groupBlockedTurns = new HashSet<(TrafficDirection, TrafficDirection)>();
            foreach (var (cx, cy) in group)
            {
                foreach (var bt in grid[cx, cy].BlockedTurns)
                {
                    groupBlockedTurns.Add(bt);
                }
            }

            var junctionConnectors = new List<Lane>();
            foreach (var (approachNode, approachDir) in approachNodes)
            {
                foreach (var (exitNode, exitDir) in exitNodes)
                {
                    if (approachNode == exitNode) continue;
                    if (!CanConnect(approachDir, exitDir)) continue;
                    if (groupBlockedTurns.Contains((approachDir, exitDir))) continue;

                    var approachSpeedMps = grid[approachNode.GridX, approachNode.GridY].SpeedLimitMph * MphToMps;
                    var lane = new Lane(approachNode, exitNode, approachDir, exitDir, approachSpeedMps) { IsJunctionConnector = true };
                    network.AddLane(lane);
                    junctionConnectors.Add(lane);
                }
            }

            ComputeConflictingLanes(junctionConnectors);

            switch (junctionType)
            {
                case JunctionType.TrafficLight:
                {
                    var repCell = grid[firstX, firstY];
                    BuildTrafficLightJunction(approachNodes, junctionConnectors, network, config ?? SimulationConfig.Default, repCell.GreenDuration, repCell.YellowDuration, repCell.AllRedDuration);
                    break;
                }
                case JunctionType.GiveWay:
                default:
                {
                    BuildGiveWayJunction(grid, group, approachNodes);
                    break;
                }
            }
        }
    }

    /// <summary>
    /// Flags crossing paths for give-way mechanics inside a junction
    /// </summary>
    private static void ComputeConflictingLanes(List<Lane> connectors)
    {
        const int samples = 24;
        const double threshold = Car.WidthMeters;

        for (var i = 0; i < connectors.Count; i++)
        {
            for (var j = i + 1; j < connectors.Count; j++)
            {
                var a = connectors[i];
                var b = connectors[j];
                
                if (a.StartNode == b.StartNode)
                {
                    continue;
                }

                if (a.Type == LaneType.Straight && b.Type == LaneType.Straight &&
                    GetOpposite(a.StartDirection) == b.StartDirection &&
                    GetOpposite(a.EndDirection) == b.EndDirection)
                {
                    continue;
                }

                var minAa = 0.0;
                var minBb = 0.0;
                var minDistSq = double.MaxValue;
                for (var si = 0; si <= samples; si++)
                {
                    var pa = a.GetPositionAt((double)si / samples);
                    for (var sj = 0; sj <= samples; sj++)
                    {
                        var pb = b.GetPositionAt((double)sj / samples);
                        var dx = pa.X - pb.X;
                        var dy = pa.Y - pb.Y;
                        var distSq = dx * dx + dy * dy;
                        if (distSq < minDistSq) 
                        {
                            minDistSq = distSq;
                            minAa = (double)si / samples;
                            minBb = (double)sj / samples;
                        }
                    }
                }

                if (!(minDistSq < threshold * threshold)) continue;
                a.ConflictingLanes.Add(b);
                a.Conflicts.Add(new LaneConflict { ConflictingLane = b, MyFraction = minAa, TheirFraction = minBb });
                b.ConflictingLanes.Add(a);
                b.Conflicts.Add(new LaneConflict { ConflictingLane = a, MyFraction = minBb, TheirFraction = minAa });
            }
        }
    }

    /// <summary>
    /// Establishes give-way logic at stopline nodes
    /// </summary>
    private static void BuildGiveWayJunction(Cell[,] grid, HashSet<(int, int)> group,
        List<(TrafficNode node, TrafficDirection dir)> approachNodes)
    {
        var groupGiveWayDirs = new HashSet<TrafficDirection>();
        foreach (var (cx, cy) in group)
        {
            foreach (var dir in grid[cx, cy].GiveWayDirections)
            {
                groupGiveWayDirs.Add(dir);
            }
        }

        var priorityNodes = approachNodes.Where(a => !groupGiveWayDirs.Contains(a.dir)).Select(a => a.node).ToList();

        foreach (var (node, dir) in approachNodes)
        {
            if (!groupGiveWayDirs.Contains(dir))
            {
                continue;
            }
            node.IsGiveWay = true;
            node.PriorityNodes.AddRange(priorityNodes);
        }
    }

    /// <summary>
    /// Creates a phase-based state machine for lights
    /// </summary>
    private static void BuildTrafficLightJunction(List<(TrafficNode node, TrafficDirection dir)> approachNodes, List<Lane> junctionConnectors, LaneNetwork network, SimulationConfig config, double greenDuration = 20.0, double yellowDuration = 3.0, double allRedDuration = 1.0)
    {
        var directionConflicts = new Dictionary<TrafficDirection, HashSet<TrafficDirection>>();
        foreach (var (_, dir) in approachNodes)
        {
            directionConflicts.TryAdd(dir, []);
        }
        foreach (var connector in junctionConnectors)
        {
            foreach (var conflicting in connector.ConflictingLanes)
            {
                directionConflicts[connector.StartDirection].Add(conflicting.StartDirection);
            }
        }

        var colourAssignment = new Dictionary<TrafficDirection, int>();
        foreach (var dir in directionConflicts.Keys)
        {
            var usedColours = directionConflicts[dir]
                .Where(d => colourAssignment.ContainsKey(d))
                .Select(d => colourAssignment[d])
                .ToHashSet();

            var colour = 0;
            while (usedColours.Contains(colour)) colour++;
            colourAssignment[dir] = colour;
        }

        var phaseGroupsDict = new Dictionary<int, HashSet<TrafficDirection>>();
        foreach (var (dir, colour) in colourAssignment)
        {
            if (!phaseGroupsDict.TryGetValue(colour, out var phaseGroup))
            {
                phaseGroup = [];
                phaseGroupsDict[colour] = phaseGroup;
            }
            phaseGroup.Add(dir);
        }

        var phaseGroups = phaseGroupsDict.Values.ToList();
        if (phaseGroups.Count == 0) return;

        var approachNodesByDirection = new Dictionary<TrafficDirection, List<TrafficNode>>();
        foreach (var (node, dir) in approachNodes)
        {
            node.ApproachDirection = dir;

            if (!approachNodesByDirection.TryGetValue(dir, out var nodes))
            {
                nodes = [];
                approachNodesByDirection[dir] = nodes;
            }
            nodes.Add(node);
        }

        var controller = new TrafficLightController(phaseGroups, config);
        controller.SetTimings(greenDuration, yellowDuration, allRedDuration);

        foreach (var (node, _) in approachNodes)
        {
            node.TrafficLight = controller;
        }

        network.TrafficLightControllers.Add(controller);
    }
    
    /// <summary>
    /// Groups connecting cells into logic junctions
    /// </summary>
    private static List<HashSet<(int, int)>> FindJunctionGroups(Cell[,] grid, int width, int height)
    {
        var visited = new HashSet<(int, int)>();
        var groups  = new List<HashSet<(int, int)>>();

        for (var x = 0; x < width; x++)
        {
            for (var y = 0; y < height; y++)
            {
                if (grid[x, y].Type != CellType.Intersection)
                {
                    continue;
                }
                if (visited.Contains((x, y)))
                {
                    continue;
                }

                var group = new HashSet<(int, int)>();
                var queue = new Queue<(int, int)>();
                queue.Enqueue((x, y));
                visited.Add((x, y));

                while (queue.Count > 0)
                {
                    var (cx, cy) = queue.Dequeue();
                    group.Add((cx, cy));

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