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
    private readonly Dictionary<Guid, double> _spawnDemand = new();
    private readonly Dictionary<Guid, double> _spawnReleaseCooldown = new();

    private readonly Random _random = new();
    private readonly Dictionary<Guid, double> _spawnIntervals = new();
    private readonly Dictionary<Guid, double> _spawnCycles = new();

    private List<TrafficNode> _exitNodesCache = [];
    private Dictionary<Guid, double> _exitNodeWeights = new();
    // Map of spawn node ID to list of exit nodes reachable from that spawn (ensures disconnected networks don't break)
    private Dictionary<Guid, List<TrafficNode>> _reachableExits = new();

    private TrafficStatistics? _statistics;
    private double _simulationTime;
    private double _statsAccumulator;
    private double _totalLaneLengthKm;
    private const double StatsSnapshotInterval = 1.0;
    
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
            _spawnDemand.Clear();
            _spawnReleaseCooldown.Clear();
            _spawnIntervals.Clear();
            _spawnCycles.Clear();

            _laneNetwork = NetworkManager.BuildNetwork(grid, width, height, cellSizeMeters, _config);

            foreach (var node in _laneNetwork.SpawnNodes)
            {
                _spawnTimers[node.Id] = 0.0;
                _spawnIntervals[node.Id] = SimulationConfig.Default.SpawnInterval;
                _spawnDemand[node.Id] = 0.0;
                _spawnReleaseCooldown[node.Id] = 0.0;
                _spawnCycles[node.Id] = SimulationConfig.Default.SpawnInterval;
            }

            // Cache exit nodes and check reachability
            _exitNodesCache = _laneNetwork.ExitNodes.ToList();
            _exitNodeWeights.Clear();
            foreach (var exit in _exitNodesCache)
            {
                _exitNodeWeights[exit.Id] = 1.0;
            }

            _reachableExits = Pathfinder.CheckReachability( _laneNetwork.SpawnNodes, _exitNodesCache);

            _totalLaneLengthKm = _laneNetwork.Lanes.Sum(l => l.Length) / 1000.0;
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
            {
                return "Network not built";
            }
            
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
            {
                return;
            }
            
            // Used by cars to look up other nearby cars based on lanes
            UpdateSpatialIndex();

            // Tick all traffic light controllers
            foreach (var controller in _laneNetwork.TrafficLightControllers)
            {
                controller.Update(deltaTime);
            }

            for (var i = _cars.Count - 1; i >= 0; i--)
            {
                var car = _cars[i];
                
                var holdAtLine = false;
                if (car.CurrentLane != null)
                {
                    holdAtLine = ApplyTrafficRules(car, deltaTime, collisionsEnabled);
                }
                else
                {
                    car.Accelerate(deltaTime);
                }

                if (!holdAtLine && collisionsEnabled && car.CurrentLane != null && ShouldHoldForLeadVehicleGap(car, deltaTime))
                {
                    holdAtLine = true;
                    car.SetTargetSpeed(0.0, deltaTime);
                }

                var stillOnNetwork = holdAtLine || car.Move(deltaTime);

                if (stillOnNetwork) continue;
                _statistics?.RecordVehicleCompletion();
                _cars.RemoveAt(i);
            }

            // Update simulation time and take statistics snapshots
            _simulationTime += deltaTime;
            _statsAccumulator += deltaTime;
            if (_statsAccumulator >= StatsSnapshotInterval)
            {
                _statsAccumulator -= StatsSnapshotInterval;
                _statistics?.RecordSnapshot(_carsPerLane, _laneNetwork.Lanes);
            }

            // Accumulate waiting cars on entrance nodes
            foreach (var node in _laneNetwork.SpawnNodes)
            {
                if (!_spawnTimers.TryGetValue(node.Id, out var elapsed))
                {
                    continue;
                }
                elapsed += deltaTime;
                var spawnInterval = _spawnIntervals.GetValueOrDefault(node.Id, SimulationConfig.Default.SpawnInterval);
                var cycle = _spawnCycles.GetValueOrDefault(node.Id, spawnInterval);

                var pendingDemand = _spawnDemand.GetValueOrDefault(node.Id, 0.0);

                var safetyCounter = 0;
                while (elapsed >= cycle && safetyCounter < 32)
                {
                    elapsed -= cycle;
                    pendingDemand += 1.0;
                    safetyCounter++;
                    // Commit in cycles to ensure its fixed till the next spawn
                    var jitter = (_random.NextDouble() * 2.0 - 1.0) * 0.25 * spawnInterval;
                    cycle = Math.Max(0.1, spawnInterval + jitter);
                }
                _spawnCycles[node.Id] = cycle;

                _spawnTimers[node.Id] = elapsed;
                _spawnDemand[node.Id] = pendingDemand;
            }

            // Release accumulated cars slowly when there's room.
            foreach (var node in _laneNetwork.SpawnNodes)
            {
                if (!_spawnDemand.TryGetValue(node.Id, out var pendingDemand) || pendingDemand < 1.0)
                {
                    continue;
                }

                var cooldown = _spawnReleaseCooldown.GetValueOrDefault(node.Id, 0.0);
                cooldown = Math.Max(0.0, cooldown - deltaTime);

                var releasesThisTick = 0;
                while (pendingDemand >= 1.0 &&
                       releasesThisTick < _config.SpawnQueueMaxReleasePerTick &&
                       cooldown <= 0.0)
                {
                    if (!TrySpawnAtNode(node))
                    {
                        break;
                    }

                    pendingDemand -= 1.0;
                    releasesThisTick++;
                    cooldown = _config.SpawnMinInterReleaseSeconds;
                }

                _spawnDemand[node.Id] = pendingDemand;
                _spawnReleaseCooldown[node.Id] = cooldown;
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
            if (car.CurrentLane == null)
            {
                continue;
            }

            if (!_carsPerLane.TryGetValue(car.CurrentLane.Id, out var carsOnLane))
            {
                carsOnLane = [];
                _carsPerLane[car.CurrentLane.Id] = carsOnLane;
            }

            carsOnLane.Add(car);
        }

        foreach (var kvp in _carsPerLane.Where(kvp => kvp.Value.Count == 0))
        {
            _emptyLaneKeys.Add(kvp.Key);
        }

        foreach (var key in _emptyLaneKeys)
        {
            _carsPerLane.Remove(key);
        }
        _emptyLaneKeys.Clear();
    }
    
    private readonly record struct TrafficRuleDecision(double SpeedCap, bool ForceStop);

    /// <summary>
    /// Run on each car to apply traffic rules in three steps:
    /// perceive constraints, decide an aggregate speed cap, then execute with car-following.
    /// </summary>
    private bool ApplyTrafficRules(Car car, double deltaTime, bool enableCarFollowing)
    {
        if (car.CurrentLane == null)
        {
            return false;
        }

        var signalObservation = TrafficUtility.FindNearestTrafficSignal(car);
        var decision = DecideTrafficRuleConstraints(car, signalObservation, deltaTime);
        return ExecuteTrafficDecision(car, decision, deltaTime, enableCarFollowing);
    }

    private TrafficRuleDecision DecideTrafficRuleConstraints(Car car, TrafficUtility.TrafficSignalState lightState, double deltaTime)
    {
        var speedCap = car.MaxSpeed;
        var forceStop = false;

        (speedCap, forceStop) = DecideTrafficLightConstraint(car, speedCap, forceStop, lightState, deltaTime);
        speedCap = DecideGiveWayConstraint(car, speedCap);
        speedCap = DecideConnectorConflictConstraint(car, speedCap);
        speedCap = DecidePreLightConstraint(speedCap, lightState);

        return new TrafficRuleDecision(speedCap, forceStop);
    }

    private (double speedCap, bool forceStop) DecideTrafficLightConstraint( Car car, double speedCap, bool forceStop, TrafficUtility.TrafficSignalState signalObservation, double deltaTime)
    {
        if (signalObservation.LightNode == null || signalObservation.Phase == null)
        {
            return (speedCap, forceStop);
        }

        var mustStopForSignal = signalObservation.Phase == TrafficLightPhase.Red || (signalObservation.Phase == TrafficLightPhase.Yellow && signalObservation.DistanceToStopLine > _config.SafeFollowingDistance);

        if (mustStopForSignal && signalObservation.OnApproachLane)
        {
            const double stopBufferMeters = 0.25;
            var clampedDistance = Math.Max(0.0, signalObservation.DistanceToStopLine - stopBufferMeters);

            // Never allow enough speed to cross a red light
            var maxTickSafeSpeed = deltaTime > 0 ? clampedDistance / deltaTime : 0.0;
            speedCap = Math.Min(speedCap, maxTickSafeSpeed);

            if (signalObservation.DistanceToStopLine <= stopBufferMeters ||
                car.Speed * deltaTime >= clampedDistance)
            {
                return (0.0, true);
            }
        }

        switch (signalObservation.Phase)
        {
            case TrafficLightPhase.Red:
            {
                if (signalObservation.DistanceToStopLine <= _config.MinFollowingDistance)
                {
                    return (0.0, true);
                }

                // Slowly reduce speed relative to how close the light is
                var maxSafeSpeed = Math.Sqrt(2.0 * _config.Deceleration * signalObservation.DistanceToStopLine);
                speedCap = Math.Min(speedCap, Math.Max(0.0, maxSafeSpeed));
                break;
            }
            case TrafficLightPhase.Yellow when signalObservation.DistanceToStopLine > _config.SafeFollowingDistance:
            {
                var maxSafeSpeed = Math.Sqrt(2.0 * _config.Deceleration * signalObservation.DistanceToStopLine);
                speedCap = Math.Min(speedCap, Math.Max(0.0, maxSafeSpeed));
                break;
            }
        }

        return (speedCap, forceStop);
    }

    private double DecideGiveWayConstraint(Car car, double speedCap)
    {
        var endNode = car.CurrentLane!.EndNode;
        if (endNode is not { IsGiveWay: true, PriorityNodes.Count: > 0 })
        {
            return speedCap;
        }

        var remainingDist = (1.0 - car.LanePosition) * car.CurrentLane.Length;

        // Approach zone: always cap speed so cars don't arrive at give-way lines at full speed
        if (remainingDist < _config.GiveWayApproachDistance)
        {
            var approachCap = car.MaxSpeed * _config.GiveWayApproachSpeedFactor;
            speedCap = Math.Min(speedCap, approachCap);
        }

        // Stop zone: yield to any conflicting traffic (including cars already inside the junction)
        if (remainingDist < _config.GiveWayCheckDistance && HasConflictingTraffic(endNode))
        {
            var giveWaySpeed = car.MaxSpeed * (remainingDist / _config.GiveWayCheckDistance);
            speedCap = Math.Min(speedCap, Math.Max(0.0, giveWaySpeed));
        }

        return speedCap;
    }

    private double DecideConnectorConflictConstraint(Car car, double speedCap)
    {
        if (car.CurrentLane!.ConflictingLanes.Count > 0)
        {
            // Already on a conflicting connector and giveway to any competing car closer to exit.
            const double epsilon = 0.5;
            var myRemaining = (1.0 - car.LanePosition) * car.CurrentLane.Length;
            foreach (var conflicting in car.CurrentLane.ConflictingLanes)
            {
                if (!_carsPerLane.TryGetValue(conflicting.Id, out var conflictingCars))
                {
                    continue;
                }

                foreach (var other in conflictingCars)
                {
                    var otherRemaining = (1.0 - other.LanePosition) * conflicting.Length;

                    if (otherRemaining > myRemaining + epsilon)
                    {
                        continue;
                    }

                    // Distances roughly equal use a GUID tiebreaker
                    if (Math.Abs(otherRemaining - myRemaining) <= epsilon && car.Id.CompareTo(other.Id) < 0)
                    {
                        continue;
                    }

                    speedCap = 0.0;
                    break;
                }
            }

            return speedCap;
        }

        // On an approach lane — check if the next connector in the route has any cars on a crossing connector
        foreach (var (futureLane, distToStart, _) in car.GetCachedPathAhead())
        {
            if (futureLane.Id == car.CurrentLane.Id)
            {
                continue;
            }

            if (distToStart >= _config.GiveWayCheckDistance)
            {
                break;
            }

            if (futureLane.ConflictingLanes.Count == 0)
            {
                continue;
            }

            var mustYield = false;
            foreach (var conflicting in futureLane.ConflictingLanes)
            {
                // A car is already inside the conflicting connector — stop before entering
                if (_carsPerLane.TryGetValue(conflicting.Id, out var conflictingCars) && conflictingCars.Count > 0)
                {
                    mustYield = true;
                    break;
                }

                // A car on an approach lane is close enough to enter so one must giveway
                if (conflicting.StartNode == null) continue;
                if (conflicting.StartNode.IsGiveWay && conflicting.StartNode.PriorityNodes.Count > 0) continue;
                foreach (var approachLane in conflicting.StartNode.IncomingLanes)
                {
                    if (!_carsPerLane.TryGetValue(approachLane.Id, out var approachCars)) continue;
                    foreach (var other in approachCars)
                    {
                        var otherDistToJunction = (1.0 - other.LanePosition) * approachLane.Length;
                        if (otherDistToJunction < _config.GiveWayCheckDistance && car.Id.CompareTo(other.Id) > 0)
                        {
                            mustYield = true;
                            break;
                        }
                    }
                    if (mustYield) break;
                }
                if (mustYield) break;
            }

            // Anti-blocking: don't enter a connector if the exit road has no space.
            // This prevents cars from becoming stranded inside the junction (queue spillback deadlock).
            if (!mustYield && !IsExitRoadClear(futureLane, car))
            {
                mustYield = true;
            }

            if (mustYield)
            {
                speedCap = 0.0;
            }

            break; // Only check the nearest connector in the path
        }

        return speedCap;
    }

    /// <summary>
    /// Returns true if the road segment after this connector has enough space for one more car.
    /// Prevents cars from entering a connector when the exit is jammed (queue spillback).
    /// </summary>
    private bool IsExitRoadClear(Lane connectorLane, Car car)
    {
        var exitNode = connectorLane.EndNode;

        // Find the exit road lane the car will take after the connector.
        // Prefer the route-based next lane; fall back to any non-connector outgoing lane.
        Lane? exitLane = null;
        var pathAhead = car.GetCachedPathAhead();
        var foundConnector = false;
        foreach (var (lane, _, _) in pathAhead)
        {
            if (foundConnector && lane.ConflictingLanes.Count == 0)
            {
                exitLane = lane;
                break;
            }
            if (lane.Id == connectorLane.Id)
                foundConnector = true;
        }

        // Fall back: first non-connector outgoing lane from the exit node
        exitLane ??= exitNode.OutgoingLanes.FirstOrDefault(l => l.ConflictingLanes.Count == 0);

        if (exitLane == null) return true;

        if (!_carsPerLane.TryGetValue(exitLane.Id, out var carsOnExit) || carsOnExit.Count == 0)
            return true;

        // Space is clear if the rearmost car on the exit lane is far enough from the start
        var rearmostPosition = carsOnExit.Min(c => c.LanePosition);
        var gapFromStart = rearmostPosition * exitLane.Length;
        return gapFromStart > Car.LengthMeters + _config.MinFollowingDistance;
    }

    private double DecidePreLightConstraint(double speedCap, TrafficUtility.TrafficSignalState signalObservation)
    {
        if (signalObservation.LightNode == null || signalObservation.OnApproachLane || signalObservation.Phase == null)
        {
            return speedCap;
        }

        var shouldSlowForSignal = signalObservation.Phase == TrafficLightPhase.Red ||
                                  (signalObservation.Phase == TrafficLightPhase.Yellow &&
                                   signalObservation.DistanceToStopLine > _config.SafeFollowingDistance);

        if (!shouldSlowForSignal)
        {
            return speedCap;
        }

        var maxSafeSpeed = Math.Sqrt(2.0 * _config.Deceleration * signalObservation.DistanceToStopLine);
        return Math.Min(speedCap, Math.Max(0.0, maxSafeSpeed));
    }

    private bool ExecuteTrafficDecision(Car car, TrafficRuleDecision decision, double deltaTime, bool enableCarFollowing)
    {
        if (decision.ForceStop)
        {
            car.SetTargetSpeed(0.0, deltaTime);
            return true;
        }

        if (!enableCarFollowing)
        {
            ApplySpeedCap(car, decision.SpeedCap, deltaTime);
            return false;
        }

        // Car-following always runs, constrained by the aggregate speed cap.
        var (carAhead, distance) = FindCarAhead(car);
        if (carAhead != null)
        {
            distance -= Car.LengthMeters;
            if (distance < _config.MinFollowingDistance)
            {
                car.Decelerate(deltaTime);
                return false;
            }

            if (distance < _config.SafeFollowingDistance)
            {
                var followSpeed = Math.Min(carAhead.Speed, car.MaxSpeed) * 0.8;
                car.SetTargetSpeed(Math.Min(followSpeed, decision.SpeedCap), deltaTime);
                return false;
            }

            if (distance < _config.ReactionDistance)
            {
                var followSpeed = car.MaxSpeed * (distance / _config.ReactionDistance);
                car.SetTargetSpeed(Math.Min(followSpeed, decision.SpeedCap), deltaTime);
                return false;
            }
        }

        ApplySpeedCap(car, decision.SpeedCap, deltaTime);
        return false;
    }

    /// <summary>
    /// Decelerates toward the speed cap if below max speed, otherwise speeds up.
    /// </summary>
    private static void ApplySpeedCap(Car car, double speedCap, double deltaTime)
    {
        if (speedCap < car.MaxSpeed)
        {
            car.SetTargetSpeed(speedCap, deltaTime);
        }
        else
        {
            car.Accelerate(deltaTime);
        }
    }

    /// <summary>
    /// Last-moment safety guard to avoid interpenetration with the lead vehicle.
    /// Uses current frame positions to prevent advancing farther than the available gap allows.
    /// </summary>
    private bool ShouldHoldForLeadVehicleGap(Car car, double deltaTime)
    {
        var (carAhead, distanceToAhead) = FindCarAhead(car);
        if (carAhead == null || deltaTime <= 0.0)
        {
            return false;
        }

        var bumperGap = distanceToAhead - Car.LengthMeters;
        var maxSafeTravel = Math.Max(0.0, bumperGap - _config.MinFollowingDistance);
        var projectedTravel = car.Speed * deltaTime;
        return projectedTravel > maxSafeTravel;
    }
    
    /// <summary>
    /// Traverses through the spatial index to find if there is a car on its lane and how close it is
    /// </summary>
    /// <param name="car">Car to operate on</param>
    /// <returns>the nearest car and its distance from our car</returns>
    private (Car? car, double distance) FindCarAhead(Car car)
    {
        if (car.CurrentLane == null)
        {
            return (null, double.MaxValue);
        }

        // Get cached path for quicker calc
        var pathAhead = car.GetCachedPathAhead();

        Car? closestCar = null;
        var minDistance = double.MaxValue;

        foreach (var (lane, startDistance, _) in pathAhead)
        {
            if (!_carsPerLane.TryGetValue(lane.Id, out var carsOnLane))
            {
                continue;
            }

            foreach (var otherCar in carsOnLane.Where(otherCar => otherCar.Id != car.Id))
            {
                if (lane.Id == car.CurrentLane.Id)
                {
                    if (otherCar.LanePosition <= car.LanePosition)
                    {
                        continue;
                    }
                }

                var startOffset = lane.Id == car.CurrentLane.Id ? car.LanePosition : 0.0;
                var distanceAlongLane = (otherCar.LanePosition - startOffset) * lane.Length;
                var totalDistance = startDistance + distanceAlongLane;

                if (!(totalDistance > 0) || !(totalDistance < minDistance))
                {
                    continue;
                }
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
            // Cars still approaching on priority approach lanes
            foreach (var lane in priorityNode.IncomingLanes)
            {
                if (!_carsPerLane.TryGetValue(lane.Id, out var carsOnLane)) continue;
                foreach (var car in carsOnLane)
                {
                    if ((1.0 - car.LanePosition) * lane.Length < _config.ConflictCheckDistance)
                        return true;
                }
            }

            // Cars already inside the junction on priority connector lanes
            foreach (var lane in priorityNode.OutgoingLanes)
            {
                if (_carsPerLane.TryGetValue(lane.Id, out var carsOnLane) && carsOnLane.Count > 0)
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Gets the current traffic light phase for all traffic-light-controlled nodes
    /// </summary>
    /// <returns>List of node positions and their current phase</returns>
    public List<(int gridX, int gridY, TrafficLightPhase phase)> GetTrafficLightRenderData()
    {
        lock (_carsLock)
        {
            if (_laneNetwork == null)
            {
                return [];
            }

            var result = new List<(int, int, TrafficLightPhase)>();
            foreach (var node in _laneNetwork.Nodes)
            {
                if (node.TrafficLight != null)
                {
                    result.Add((node.GridX, node.GridY, node.TrafficLight.GetPhaseForNode(node)));
                }
            }
            return result;
        }
    }

    /// <summary>
    /// Gets the positions of all give-way nodes
    /// </summary>
    /// <returns>List of all give-way nodes</returns>
    public IEnumerable<(int gridX, int gridY)> GetGiveWayNodePositions()
    {
        lock (_carsLock)
        {
            if (_laneNetwork == null)
            {
                return [];
            }
            return _laneNetwork.Nodes
                .Where(n => n.IsGiveWay)
                .Select(n => (n.GridX, n.GridY))
                .ToList();
        }
    }

    /// <summary>
    /// Sets the spawn rate at a specific spawn node in cars per minute.
    /// </summary>
    public void SetSpawnRate(int gridX, int gridY, double carsPerMinute)
    {
        lock (_carsLock)
        {
            var node = _laneNetwork?.GetNodeAt(gridX, gridY);
            if (node == null || !_spawnIntervals.ContainsKey(node.Id)) return;
            var interval = carsPerMinute > 0 ? 60.0 / carsPerMinute : 60.0;
            _spawnIntervals[node.Id] = Math.Max(0.5, interval);
        }
    }

    /// <summary>
    /// Returns the current spawn rate for a specific spawn node in cars per minute.
    /// </summary>
    public double GetSpawnRate(int gridX, int gridY)
    {
        lock (_carsLock)
        {
            var node = _laneNetwork?.GetNodeAt(gridX, gridY);
            if (node != null && _spawnIntervals.TryGetValue(node.Id, out var interval))
                return interval > 0 ? 60.0 / interval : 0;
            return 60.0 / SimulationConfig.Default.SpawnInterval;
        }
    }

    /// <summary>
    /// Updates the destination weight for a single exit node.
    /// </summary>
    public void SetExitNodeWeight(int gridX, int gridY, double weight)
    {
        lock (_carsLock)
        {
            var node = _laneNetwork?.GetNodeAt(gridX, gridY);
            if (node != null && _exitNodeWeights.ContainsKey(node.Id))
                _exitNodeWeights[node.Id] = Math.Max(0.0, weight);
        }
    }

    /// <summary>
    /// Returns the current destination weight for a single exit node.
    /// </summary>
    public double GetExitNodeWeight(int gridX, int gridY)
    {
        lock (_carsLock)
        {
            var node = _laneNetwork?.GetNodeAt(gridX, gridY);
            if (node != null && _exitNodeWeights.TryGetValue(node.Id, out var weight))
                return weight;
            return 1.0;
        }
    }

    /// <summary>
    /// Updates green/yellow/red durations on the traffic light controller at the given node.
    /// </summary>
    public void SetTrafficLightTimings(int gridX, int gridY, double green, double yellow, double allRed)
    {
        lock (_carsLock)
        {
            _laneNetwork?.GetNodeAt(gridX, gridY)?.TrafficLight?.SetTimings(green, yellow, allRed);
        }
    }

    /// <summary>
    /// Returns the current green/yellow/red durations for the controller at the given node.
    /// </summary>
    public (double green, double yellow, double allRed)? GetTrafficLightTimings(int gridX, int gridY)
    {
        lock (_carsLock)
        {
            var node = _laneNetwork?.GetNodeAt(gridX, gridY);
            return node?.TrafficLight?.GetTimings();
        }
    }

    /// <summary>
    /// Returns grid coordinates and ID for every spawn node in the current network.
    /// </summary>
    public IReadOnlyList<(int gridX, int gridY, Guid id)> GetSpawnNodeInfos()
    {
        lock (_carsLock)
        {
            if (_laneNetwork == null) return [];
            return _laneNetwork.SpawnNodes.Select(n => (n.GridX, n.GridY, n.Id)).ToList();
        }
    }

    /// <summary>
    /// Returns the current buffered spawn backlog for each spawn node.
    /// </summary>
    public List<(int gridX, int gridY, double backlog)> GetSpawnBacklogRenderData()
    {
        lock (_carsLock)
        {
            if (_laneNetwork == null)
            {
                return [];
            }

            var result = new List<(int gridX, int gridY, double backlog)>();
            foreach (var node in _laneNetwork.SpawnNodes)
            {
                var backlog = _spawnDemand.GetValueOrDefault(node.Id, 0.0);
                result.Add((node.GridX, node.GridY, backlog));
            }

            return result;
        }
    }

    /// <summary>
    /// Returns grid coordinates and ID for every exit node in the current network.
    /// </summary>
    public IReadOnlyList<(int gridX, int gridY, Guid id)> GetExitNodeInfos()
    {
        lock (_carsLock)
        {
            return _exitNodesCache.Select(n => (n.GridX, n.GridY, n.Id)).ToList();
        }
    }

    /// <summary>
    /// Returns what kind of configurable sim node (if any) is at the given pixel position.
    /// </summary>
    public (NodeKind kind, int gridX, int gridY)? GetSimNodeAt(double pixelX, double pixelY)
    {
        lock (_carsLock)
        {
            if (_laneNetwork == null) return null;
            var cell = gridManager.GetCellFromPixel(pixelX, pixelY);
            if (cell == null) return null;
            var node = _laneNetwork.GetNodeAt(cell.X, cell.Y);
            if (node == null) return null;
            if (node.Enums == Enums.Spawn) return (NodeKind.Spawn, cell.X, cell.Y);
            if (node.Enums == Enums.Exit) return (NodeKind.Exit, cell.X, cell.Y);
            if (node.TrafficLight != null) return (NodeKind.TrafficLight, cell.X, cell.Y);
            return null;
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
            {
                return exit;
            }
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

        // Check the nearest traffic ahead across the first couple of lanes of the route.
        var distToLaneStart = 0.0;
        var minDistAhead = double.MaxValue;
        foreach (var checkLane in route.Take(2))
        {
            foreach (var other in _cars)
            {
                if (other.CurrentLane?.Id != checkLane.Id)
                {
                    continue;
                }
                var distFromSpawn = distToLaneStart + other.LanePosition * checkLane.Length;
                if (distFromSpawn < minDistAhead)
                {
                    minDistAhead = distFromSpawn;
                }
            }
            distToLaneStart += checkLane.Length;
        }

        const double fiveMphInMps = 5.0 * 0.44704;
        var speedOffset = (_random.NextDouble() * 2.0 - 1.0) * fiveMphInMps;
        var colors = Enum.GetValues<CarColor>();
        var color = colors[_random.Next(colors.Length)];

        var hardMinimumGap = Car.LengthMeters + _config.MinFollowingDistance;
        if (minDistAhead < hardMinimumGap)
        {
            return false;
        }

        // Scale initial speed to the gap ahead and a speed-aware stopping envelope.
        var maxSpawnSpeed = Math.Max(lane.SpeedLimitMps + speedOffset, 0.0);
        var reactionScaledSpeed = maxSpawnSpeed;
        var stoppingEnvelopeSpeed = maxSpawnSpeed;
        if (minDistAhead < double.MaxValue)
        {
            var balancedGapDistance = Math.Max(_config.ReactionDistance * _config.SpawnBalancedGapFactor, hardMinimumGap);
            reactionScaledSpeed = maxSpawnSpeed *
                                  Math.Clamp((minDistAhead - hardMinimumGap) / Math.Max(0.1, balancedGapDistance - hardMinimumGap), 0.0, 1.0);

            var usableGapForStopping = Math.Max(0.0, minDistAhead - hardMinimumGap);
            stoppingEnvelopeSpeed = Math.Sqrt(2.0 * Math.Max(0.1, _config.Deceleration) * usableGapForStopping);
        }

        var initialSpeed = Math.Min(maxSpawnSpeed, Math.Min(reactionScaledSpeed, stoppingEnvelopeSpeed));

        var car = new Car(lane, speedOffset, color, _config, 0.0, route, destination, initialSpeed);
        _cars.Add(car);
        return true;
    }
    
    /// <summary>
    /// Resets the statistics accumulator ready for a new simulation run.
    /// </summary>
    public void StartStatistics()
    {
        lock (_carsLock)
        {
            _statistics = new TrafficStatistics();
            _simulationTime = 0.0;
            _statsAccumulator = 0.0;
        }
    }

    /// <summary>
    /// Finalizes accumulated statistics and returns them for display.
    /// Returns null if statistics were never started.
    /// </summary>
    public TrafficStatistics? GetFinalStatistics()
    {
        lock (_carsLock)
        {
            _statistics?.Finalise(_simulationTime, _totalLaneLengthKm);
            return _statistics;
        }
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
            _spawnDemand.Clear();
            _spawnReleaseCooldown.Clear();
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
            _spawnTimers.Clear();
            _spawnDemand.Clear();
            _spawnReleaseCooldown.Clear();
            _spawnIntervals.Clear();
            _spawnCycles.Clear();
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