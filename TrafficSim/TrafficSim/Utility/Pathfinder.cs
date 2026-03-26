using TrafficSim.Models;

namespace TrafficSim.Utility;

public static class Pathfinder
{
    /// <summary>
    /// Finds the shortest path (by lane length) from start to finish using A*
    /// </summary>
    /// <param name="start">The start node</param>
    /// <param name="end">The end node</param>
    /// <returns>Ordered list of lanes from start to end</returns>
    public static List<Lane>? FindPath(TrafficNode start, TrafficNode end)
    {
        // Start and end are the same so no path is calculated
        if (start.Id == end.Id)
        {
            return [];
        }

        // Nodes to explore, ordered by the g cost and heuristic
        var openSet = new PriorityQueue<TrafficNode, double>();
        // Nodes already fully explored
        var closedSet = new HashSet<Guid>();
        // Cheapest known cost to reach each node
        var gCosts = new Dictionary<Guid, double> { [start.Id] = 0.0 };
        // Tracks which node and lane we came from to reach each node
        var cameFrom = new Dictionary<Guid, (TrafficNode parent, Lane viaLane)>();

        openSet.Enqueue(start, Heuristic(start, end));

        while (openSet.Count > 0)
        {
            var current = openSet.Dequeue();

            // The path has to end node has been finished so the path needs to be reconstructed to form a start to finish path
            if (current.Id == end.Id)
            {
                return ReconstructPath(cameFrom, end);
            }

            // Skip if node already explored
            if (!closedSet.Add(current.Id))
            {
                continue;
            }

            var currentG = gCosts[current.Id];

            // Check each outgoing lane as an edge to a neighbouring node
            foreach (var lane in current.OutgoingLanes)
            {
                var neighbor = lane.EndNode;

                if (closedSet.Contains(neighbor.Id))
                {
                    continue;
                }

                var finalG = currentG + lane.Length;

                // Only update if this is a cheaper path to the neighbour by checking the current cost and adding the lane's length
                if (gCosts.TryGetValue(neighbor.Id, out var existingG) && finalG >= existingG)
                {
                    continue;
                }

                gCosts[neighbor.Id] = finalG;
                cameFrom[neighbor.Id] = (current, lane);
                // Add the lowest cost nodes first
                openSet.Enqueue(neighbor, finalG + Heuristic(neighbor, end));
            }
        }

        return null;
    }

    /// <summary>
    /// Find exit nodes that are reachable from spawn nodes
    /// </summary>
    /// <param name="spawnNodes">All spawn nodes in the network</param>
    /// <param name="exitNodes">All exit nodes in the network</param>
    /// <returns>Map spawn nodes to list of reachable exit nodes</returns>
    public static Dictionary<Guid, List<TrafficNode>> CheckReachability( IEnumerable<TrafficNode> spawnNodes, IEnumerable<TrafficNode> exitNodes)
    {
        // Build a set of exit node IDs
        var exitSet = new HashSet<Guid>();
        foreach (var exit in exitNodes)
        {
            exitSet.Add(exit.Id);
        }

        var result = new Dictionary<Guid, List<TrafficNode>>();

        // BFS from each spawn node to find which exits it can reach
        foreach (var spawn in spawnNodes)
        {
            var reachableExits = new List<TrafficNode>();
            var visited = new HashSet<Guid>();
            var queue = new Queue<TrafficNode>();

            queue.Enqueue(spawn);
            visited.Add(spawn.Id);

            while (queue.Count > 0)
            {
                var node = queue.Dequeue();

                if (exitSet.Contains(node.Id))
                {
                    reachableExits.Add(node);  
                }

                foreach (var lane in node.OutgoingLanes)
                {
                    if (visited.Add(lane.EndNode.Id))
                    {
                        queue.Enqueue(lane.EndNode);
                    }
                }
            }

            result[spawn.Id] = reachableExits;
        }

        return result;
    }

    // Straight line distance between two nodes to calculate Heuristic for A*
    private static double Heuristic(TrafficNode from, TrafficNode to)
    {
        var dx = from.X - to.X;
        var dy = from.Y - to.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    // Goes backwards from goal to start and reverses to get the lane sequence
    private static List<Lane> ReconstructPath(Dictionary<Guid, (TrafficNode parent, Lane viaLane)> cameFrom, TrafficNode goal)
    {
        var path = new List<Lane>();
        var current = goal;

        while (cameFrom.TryGetValue(current.Id, out var entry))
        {
            path.Add(entry.viaLane);
            current = entry.parent;
        }

        path.Reverse();
        return path;
    }
}
