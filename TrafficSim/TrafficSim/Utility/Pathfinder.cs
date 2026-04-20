using TrafficSim.Models;

namespace TrafficSim.Utility;

public static class Pathfinder
{
    /// <summary>
    /// Finds the shortest path (by lane length) from start to finish using the A* algorithm
    /// </summary>
    public static List<Lane>? FindPath(TrafficNode start, TrafficNode end)
    {
        // Exit early if start and end are identical
        if (start.Id == end.Id)
        {
            return [];
        }

        var openSet = new PriorityQueue<TrafficNode, double>();
        var closedSet = new HashSet<Guid>();
        var gCosts = new Dictionary<Guid, double> { [start.Id] = 0.0 };
        var cameFrom = new Dictionary<Guid, (TrafficNode parent, Lane viaLane)>();

        openSet.Enqueue(start, Heuristic(start, end));

        while (openSet.Count > 0)
        {
            var current = openSet.Dequeue();

            // Goal reached, build final route
            if (current.Id == end.Id)
            {
                return ReconstructPath(cameFrom, end);
            }

            if (!closedSet.Add(current.Id))
            {
                continue;
            }

            var currentG = gCosts[current.Id];

            // Evaluate all connected lanes as edges
            foreach (var lane in current.OutgoingLanes)
            {
                var neighbour = lane.EndNode;

                if (closedSet.Contains(neighbour.Id))
                {
                    continue;
                }

                var finalG = currentG + lane.Length;

                // Only proceed if this forms a strictly cheaper path to the neighbour
                if (gCosts.TryGetValue(neighbour.Id, out var existingG) && finalG >= existingG)
                {
                    continue;
                }

                gCosts[neighbour.Id] = finalG;
                cameFrom[neighbour.Id] = (current, lane);
                openSet.Enqueue(neighbour, finalG + Heuristic(neighbour, end));
            }
        }

        return null; // No path found
    }

    /// <summary>
    /// Finds all valid exit nodes reachable from a given spawn node using BFS
    /// </summary>
    public static Dictionary<Guid, List<TrafficNode>> CheckReachability(IEnumerable<TrafficNode> spawnNodes, IEnumerable<TrafficNode> exitNodes)
    {
        var exitSet = new HashSet<Guid>();
        foreach (var exit in exitNodes)
        {
            exitSet.Add(exit.Id);
        }

        var result = new Dictionary<Guid, List<TrafficNode>>();

        // Run BFS from each spawn node to build reachability map
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

    /// <summary>
    /// Calculates the straight-line distance heuristic for A* pathfinding
    /// </summary>
    private static double Heuristic(TrafficNode from, TrafficNode to)
    {
        var dx = from.X - to.X;
        var dy = from.Y - to.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    /// <summary>
    /// Reconstructs the final lane sequence by navigating backwards from the goal
    /// </summary>
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
