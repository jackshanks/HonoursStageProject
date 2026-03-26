using TrafficSim.Models;
using TrafficSim.Utility;

namespace TrafficSim.Managers;

/// <summary>
/// Manages and tracks all vehicles as they go through the network, as well as creating the network
/// </summary>
/// <param name="gridManager"></param>
/// <param name="config">Physics and behaviour constants</param>
public class TrafficManager(GridManager gridManager, SimulationConfig? config = null)
{
    private readonly SimulationConfig _config = config ?? SimulationConfig.Default;
    private readonly List<Car> _cars = [];
    private readonly Lock _carsLock = new();

    private LaneNetwork? _laneNetwork;

    private readonly Dictionary<Guid, List<Car>> _carsPerLane = new();
    private readonly List<Guid> _emptyLaneKeys = [];
    private readonly Dictionary<Guid, double> _spawnTimers = new();

    private readonly Random _random = new();
    
    private List<TrafficNode> _exitNodesCache = [];
    private Dictionary<Guid, double> _exitNodeWeights = new();
    // Map of spawn node ID to list of exit nodes reachable from that spawn (ensures disconnected networks don't break)
    private Dictionary<Guid, List<TrafficNode>> _reachableExits = new();
    
    /// <summary>
    /// Builds the network from the grid
    /// </summary>
    /// <param name="grid">List of all cells</param>
    /// <param name="width">Width of the grid in cells</param>
    /// <param name="height">Height of the grid in cells</param>
    /// <param name="cellSizeMeters">Size of each cell in meters</param>
    /// <returns>If the network was successfully built</returns>
    public bool BuildNetwork(Cell[,] grid, int width, int height, double cellSizeMeters)
    {
        lock (_carsLock)
        {
            _cars.Clear();
            _carsPerLane.Clear();
            _spawnTimers.Clear();

            _laneNetwork = NetworkManager.BuildNetwork(grid, width, height, cellSizeMeters);

            foreach (var node in _laneNetwork.SpawnNodes)
            {
                _spawnTimers[node.Id] = 0.0; 
            }

            // Cache exit nodes and check reachability
            _exitNodesCache = _laneNetwork.ExitNodes.ToList();
            _exitNodeWeights.Clear();
            foreach (var exit in _exitNodesCache)
            {
                _exitNodeWeights[exit.Id] = 1.0;
            }

            _reachableExits = Pathfinder.CheckReachability( _laneNetwork.SpawnNodes, _exitNodesCache);

            return NetworkManager.ValidateNetwork(_laneNetwork);
        }
    }
    
    /// <summary>
    /// Get all information about a network to be returned
    /// </summary>
    /// <returns>string of information</returns>
    public string GetNetworkInfo()
    {
        lock (_carsLock)
        {
            if (_laneNetwork == null)
                return "Network not built";
            
            var stats = _laneNetwork.GetStats();
            return $"Nodes: {stats.nodeCount} | Lanes: {stats.laneCount} (Straight: {stats.straightLanes}, Curved: {stats.curvedLanes})";
        }
    }
    
    /// <summary>
    /// Updates the physics of all cars in the network
    /// </summary>
    /// <param name="deltaTime">Time passed</param>
    /// <param name="collisionsEnabled">If cars should collide or not</param>
    public void UpdatePhysics(double deltaTime, bool collisionsEnabled)
    {
        lock (_carsLock)
        {
            if (_laneNetwork == null) 
                return;
            
            // Used by cars to look up other near-by cars based on lanes
            UpdateSpatialIndex();
            
            for (var i = _cars.Count - 1; i >= 0; i--)
            {
                var car = _cars[i];
                
                if (collisionsEnabled && car.CurrentLane != null)
                {
                    ApplyTrafficRules(car, deltaTime);
                }
                else
                {
                    car.Accelerate(deltaTime);
                }
                
                var stillOnNetwork = car.Move(deltaTime);

                if (!stillOnNetwork)
                {
                    _cars.RemoveAt(i);
                }
            }

            // Calculate if a new car should be spawned on the spawn nodes
            foreach (var node in _laneNetwork.SpawnNodes)
            {
                if (!_spawnTimers.TryGetValue(node.Id, out var elapsed)) continue;
                elapsed += deltaTime;
                if (elapsed >= _config.SpawnInterval)
                {
                    TrySpawnAtNode(node);
                    elapsed = 0.0;
                }
                _spawnTimers[node.Id] = elapsed;
            }
        }
    }
    
    /// <summary>
    /// Organises cars into groups based on the lane they are on, which can be used by other cars to look up nearby cars.
    /// </summary>
    private void UpdateSpatialIndex()
    {
        foreach (var list in _carsPerLane.Values)
        {
            list.Clear();
        }

        foreach (var car in _cars)
        {
            if (car.CurrentLane == null) continue;

            if (!_carsPerLane.TryGetValue(car.CurrentLane.Id, out var carsOnLane))
            {
                carsOnLane = [];
                _carsPerLane[car.CurrentLane.Id] = carsOnLane;
            }

            carsOnLane.Add(car);
        }

        foreach (var kvp in _carsPerLane)
        {
            // If a lane as been cleared of cars note it down so it can be removed from being checked until a new car enters
            if (kvp.Value.Count == 0)
            {
                _emptyLaneKeys.Add(kvp.Key);
            }
        }

        foreach (var key in _emptyLaneKeys)
        {
            _carsPerLane.Remove(key);
            _emptyLaneKeys.Clear();
        }
    }
    
    /// <summary>
    /// Run on each car to apply traffic rules
    /// </summary>
    /// <param name="car">The car to apply traffic rules to</param>
    /// <param name="deltaTime">Time passed</param>
    private void ApplyTrafficRules(Car car, double deltaTime)
    {
        if (car.CurrentLane == null)
        {
            return;
        }

        // Give way check
        var endNode = car.CurrentLane.EndNode;
        if (endNode.IsGiveWay && endNode.PriorityNodes.Count > 0)
        {
            var remainingDist = (1.0 - car.LanePosition) * car.CurrentLane.Length;
            if (remainingDist < _config.GiveWayCheckDistance && HasConflictingTraffic(endNode))
            {
                var targetSpeed = car.MaxSpeed * (remainingDist / _config.GiveWayCheckDistance);
                car.SetTargetSpeed(Math.Max(0, targetSpeed), deltaTime);
                return;
            }
        }

        var (carAhead, distance) = FindCarAhead(car);

        if (carAhead != null)
        {
            distance -= Car.LengthMeters;

            switch (distance)
            {
                // Cars will break sharply at this distance
                case var d when d < _config.MinFollowingDistance:
                    car.Decelerate(deltaTime);
                    break;
                case var d when d < _config.SafeFollowingDistance:
                {
                    var targetSpeed = Math.Min(carAhead.Speed, car.MaxSpeed);
                    car.SetTargetSpeed(targetSpeed * 0.8, deltaTime);
                    break;
                }
                // Cars will begin slowing down at this distance if there is slow traffic ahead
                case var d when d < _config.ReactionDistance:
                {
                    var targetSpeed = car.MaxSpeed * (distance / _config.ReactionDistance);
                    car.SetTargetSpeed(targetSpeed, deltaTime);
                    break;
                }
                default:
                    car.Accelerate(deltaTime);
                    break;
            }
        }
        else
        {
            car.Accelerate(deltaTime);
        }
    }
    
    /// <summary>
    /// Traverses through the spatial index to find if there is a car on its lane and how close it is
    /// </summary>
    /// <param name="car">Car to operate on</param>
    /// <returns>the nearest car and its distance from our car</returns>
    private (Car? car, double distance) FindCarAhead(Car car)
    {
        if (car.CurrentLane == null)
            return (null, double.MaxValue);

        // Get cached path for quicker calc
        var pathAhead = car.GetCachedPathAhead();

        Car? closestCar = null;
        var minDistance = double.MaxValue;

        foreach (var (lane, startDistance, _) in pathAhead)
        {
            if (!_carsPerLane.TryGetValue(lane.Id, out var carsOnLane))
                continue;

            foreach (var otherCar in carsOnLane)
            {
                if (otherCar.Id == car.Id) continue;
                if (lane.Id == car.CurrentLane.Id)
                {
                    if (otherCar.LanePosition <= car.LanePosition)
                        continue;
                }

                var startOffset = lane.Id == car.CurrentLane.Id ? car.LanePosition : 0.0;
                var distanceAlongLane = (otherCar.LanePosition - startOffset) * lane.Length;
                var totalDistance = startDistance + distanceAlongLane;

                if (!(totalDistance > 0) || !(totalDistance < minDistance)) continue;
                minDistance = totalDistance;
                closestCar = otherCar;
            }
        }

        return (closestCar, minDistance);
    }
    
    /// <summary>
    /// Checks if there is any traffic to give way to
    /// </summary>
    /// <param name="giveWayNode">TrafficNode object for the give-way node</param>
    /// <returns></returns>
    private bool HasConflictingTraffic(TrafficNode giveWayNode)
    {
        foreach (var priorityNode in giveWayNode.PriorityNodes)
        {
            foreach (var lane in priorityNode.IncomingLanes)
            {
                if (!_carsPerLane.TryGetValue(lane.Id, out var carsOnLane)) continue;
                foreach (var car in carsOnLane)
                {
                    var distToNode = (1.0 - car.LanePosition) * lane.Length;
                    if (distToNode < _config.ConflictCheckDistance)
                        return true;
                }
            }
        }
        return false;
    }

    /// <summary>
    /// Gets the positions of all give-way nodes
    /// </summary>
    /// <returns>List of all give-way nodes</returns>
    public IEnumerable<(int gridX, int gridY)> GetGiveWayNodePositions()
    {
        lock (_carsLock)
        {
            if (_laneNetwork == null) return [];
            return _laneNetwork.Nodes
                .Where(n => n.IsGiveWay)
                .Select(n => (n.GridX, n.GridY))
                .ToList();
        }
    }

    /// <summary>
    /// Spawns a car at the given pixel position.
    /// </summary>
    /// <param name="pixelX"></param>
    /// <param name="pixelY"></param>
    /// <returns>if car was spawned</returns>
    public bool SpawnCarAt(double pixelX, double pixelY)
    {
        lock (_carsLock)
        {
            if (_laneNetwork == null)
            {
                return false; 
            }

            var cell = gridManager.GetCellFromPixel(pixelX, pixelY);
            if (cell == null)
            {
                return false;
            }

            var node = _laneNetwork.GetNodeAt(cell.X, cell.Y);
            return node != null && TrySpawnAtNode(node);
        }
    }

    /// <summary>
    /// Selects a weighted-random destination from the reachable exit nodes for a given spawn node
    /// </summary>
    /// <param name="spawnNode">The node the car is spawning at</param>
    /// <returns>A reachable exit node</returns>
    private TrafficNode? SelectDestination(TrafficNode spawnNode)
    {
        if (!_reachableExits.TryGetValue(spawnNode.Id, out var reachable) || reachable.Count == 0)
        {
            return null;
        }
        
        // Sum all weights for the reachable exits
        var totalWeight = 0.0;
        foreach (var exit in reachable)
        {
            totalWeight += _exitNodeWeights.GetValueOrDefault(exit.Id, 1.0); // Default to 1 if no exit weight is actually found
        }

        // Returns a random number between 0 and 1 and multiplies by the total weight, effectively returning a value between 0 and the total weight
        var number = _random.NextDouble() * totalWeight;
        var cumulative = 0.0;
        foreach (var exit in reachable)
        {
            // Each weight in the list is added together one by one until the random number is reached and that lane is selected
            cumulative += _exitNodeWeights.GetValueOrDefault(exit.Id, 1.0);
            if (number < cumulative)
                return exit;
        }

        return reachable[^1]; // returns last item to prevent any errors
    }

    /// <summary>
    /// Attempts to spawn a car at the given node with a destination
    /// </summary>
    /// <param name="node">The node to spawn the car at</param>
    /// <returns>If the car was successfully spawned</returns>
    private bool TrySpawnAtNode(TrafficNode node)
    {
        if (node.OutgoingLanes.Count == 0)
        {
            return false;
        }

        // Pick a weighted-random destination and calculate the route to it
        var destination = SelectDestination(node);
        if (destination == null)
        {
            return false;
        }

        var route = Pathfinder.FindPath(node, destination);
        if (route == null || route.Count == 0)
        {
            return false;
        }
        
        var lane = route[0];

        // Check if there is a car too close
        if (_carsPerLane.TryGetValue(lane.Id, out var carsOnLane))
        {
            foreach (var existingCar in carsOnLane)
            {
                var distance = existingCar.LanePosition * lane.Length;
                if (distance < _config.MinFollowingDistance * 2)
                { 
                    return false; 
                }
            }
        }

        var speed = 10.0 + _random.NextDouble() * 10.0;
        var colors = Enum.GetValues<CarColor>();
        var color = colors[_random.Next(colors.Length)];

        var car = new Car(lane, speed, color, _config, 0.0, route, destination);
        _cars.Add(car);
        return true;
    }
    
    /// <summary>
    /// Clears all cars and cached data
    /// </summary>
    public void ClearTraffic()
    {
        lock (_carsLock)
        {
            _cars.Clear();
            _carsPerLane.Clear();
        }
    }
    
    /// <summary>
    /// Clears all cars, cashed data, and the network
    /// </summary>
    public void ClearNetwork()
    {
        lock (_carsLock)
        {
            ClearTraffic();
            _laneNetwork?.Clear();
            _laneNetwork = null;
            _exitNodesCache.Clear();
            _exitNodeWeights.Clear();
            _reachableExits.Clear();
        }
    }
    
    /// <summary>
    /// Get render data for the cars
    /// </summary>
    /// <param name="output"></param>
    public void GetRenderData(List<CarRenderData> output)
    {
        output.Clear();
        lock (_carsLock)
        {
            foreach (var c in _cars)
            {
                var (dx, dy) = c.GetDirection();
                output.Add(new CarRenderData(c.X, c.Y, dx, dy, c.Color));
            }
        }
    }
}