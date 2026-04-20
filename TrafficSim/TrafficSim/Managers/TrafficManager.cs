using TrafficSim.Models;
using TrafficSim.Utility;

namespace TrafficSim.Managers;

/// <summary>
/// Manages vehicle physics, traffic rules, and simulation state
/// </summary>
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
    private readonly Dictionary<Guid, double> _carWaitTimers = new();

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
    /// Constructs the physics/routing network and initializes spawn states
    /// </summary>
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
            _carWaitTimers.Clear();

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
    /// Generates a diagnostic string about the current network scale
    /// </summary>
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
    /// Master tick function: advances traffic lights, cars, and spawners
    /// </summary>
    public void UpdatePhysics(double deltaTime, bool collisionsEnabled)
    {
        lock (_carsLock)
        {
            if (_laneNetwork == null) 
            {
                return;
            }
            
            UpdateSpatialIndex();

            foreach (var controller in _laneNetwork.TrafficLightControllers)
            {
                controller.Update(deltaTime);
            }

            for (var i = _cars.Count - 1; i >= 0; i--)
            {
                var car = _cars[i];
                
                // Count very slow movement as 'waiting' for deadlock overrides
                if (car.Speed < 0.5)
                {
                    _carWaitTimers[car.Id] = _carWaitTimers.GetValueOrDefault(car.Id) + deltaTime;
                }
                else
                {
                    _carWaitTimers[car.Id] = 0.0;
                }

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

                var oldLane = car.CurrentLane;
                var stillOnNetwork = holdAtLine || car.Move(deltaTime);
                var newLane = car.CurrentLane;

                // Keep spatial index synced so cars evaluate each other correctly this frame
                if (oldLane != null && newLane != null && oldLane.Id != newLane.Id)
                {
                    if (_carsPerLane.TryGetValue(oldLane.Id, out var oldBucket))
                    {
                        oldBucket.Remove(car);
                    }
                    if (!_carsPerLane.TryGetValue(newLane.Id, out var newBucket))
                    {
                        newBucket = [];
                        _carsPerLane[newLane.Id] = newBucket;
                    }
                    newBucket.Add(car);
                }

                if (stillOnNetwork) continue;
                
                _carWaitTimers.Remove(car.Id);
                _statistics?.RecordVehicleCompletion();
                _cars.RemoveAt(i);
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

            // Periodically record simulation stats
            _simulationTime += deltaTime;
            _statsAccumulator += deltaTime;
            if (!(_statsAccumulator >= StatsSnapshotInterval)) return;
            _statsAccumulator -= StatsSnapshotInterval;
            _statistics?.RecordSnapshot(_carsPerLane, _laneNetwork.Lanes);
        }
    }
    
    /// <summary>
    /// Groups cars by lane to optimize distance checks
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
    /// Determines the safest max speed based on lights, junctions, and traffic ahead
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

        // Cap speed near give-way lines to allow reaction time
        if (!(remainingDist < _config.GiveWayApproachDistance)) return speedCap;
        var approachCap = car.MaxSpeed * _config.GiveWayApproachSpeedFactor;
        speedCap = Math.Min(speedCap, approachCap);

        return speedCap;
    }

    private double DecideConnectorConflictConstraint(Car car, double speedCap)
    {
        // Inside the junction
        if (car.CurrentLane!.Conflicts.Count > 0)
        {
            var myProgress = car.LanePosition;
            var carLengthFractionMine = Car.LengthMeters / car.CurrentLane.Length;
            
            foreach (var conflict in car.CurrentLane.Conflicts)
            {
                var conflicting = conflict.ConflictingLane;
                if (!_carsPerLane.TryGetValue(conflicting.Id, out var conflictingCars)) continue;

                var carLengthFractionTheirs = Car.LengthMeters / conflicting.Length;

                foreach (var other in conflictingCars)
                {
                    // Have we already passed the conflict point?
                    if (myProgress > conflict.MyFraction + carLengthFractionMine) continue;
                    // Have they already cleared the conflict point?
                    if (other.LanePosition > conflict.TheirFraction + carLengthFractionTheirs) continue;

                    var myDistToConflict = (conflict.MyFraction - myProgress) * car.CurrentLane.Length;
                    var theirDistToConflict = (conflict.TheirFraction - other.LanePosition) * conflicting.Length;

                    var myWait = _carWaitTimers.GetValueOrDefault(car.Id);
                    var theirWait = _carWaitTimers.GetValueOrDefault(other.Id);
                    
                    var canOverride = false;
                    if (myWait > 1.0 && theirWait > 1.0)
                    {
                        if (car.Id.CompareTo(other.Id) > 0 || myWait > 2.0)
                        {
                            canOverride = true;
                        }
                    }

                    if (canOverride) continue;

                    // If they are closer to the conflict point
                    if (theirDistToConflict < myDistToConflict - 1.0)
                    {
                        return 0.0;
                    }
                    // If tied, use ID
                    else if (theirDistToConflict < myDistToConflict + 1.0)
                    {
                        if (car.Id.CompareTo(other.Id) < 0) return 0.0;
                    }
                }
            }
            return speedCap; 
        }

        // Approaching the junction
        foreach (var (futureLane, distToStart, _) in car.GetCachedPathAhead())
        {
            if (futureLane.Id == car.CurrentLane.Id) continue;
            if (distToStart >= _config.GiveWayCheckDistance) break;
            if (futureLane.Conflicts.Count == 0) continue;

            var hasUnbreakableGiveWay = false;
            
            foreach (var conflict in futureLane.Conflicts)
            {
                var conflicting = conflict.ConflictingLane;
                var carLengthFractionTheirs = Car.LengthMeters / conflicting.Length;
                var myDistToConflict = distToStart + conflict.MyFraction * futureLane.Length;

                var thisConflictMustGiveWay = false;
                Car? givingWayToInThisConflict = null;

                // Yield to anyone already inside the junction before their conflict point
                if (_carsPerLane.TryGetValue(conflicting.Id, out var conflictingCars) && conflictingCars.Count > 0)
                {
                    foreach (var other in conflictingCars)
                    {
                        var theirDistToConflict = (conflict.TheirFraction - other.LanePosition) * conflicting.Length;
                        if (theirDistToConflict < -Car.LengthMeters) continue;

                        if (other.Speed < 0.5 && theirDistToConflict > myDistToConflict + Car.LengthMeters) continue;

                        thisConflictMustGiveWay = true;
                        givingWayToInThisConflict = other;
                        break;
                    }
                }

                // Standard approach logic for uncontrolled and give-way junctions
                if (!thisConflictMustGiveWay && conflicting.StartNode is { TrafficLight: null })
                {
                    var ourStartNode = futureLane.StartNode;
                    var otherHasPriority = ourStartNode.IsGiveWay && ourStartNode.PriorityNodes.Contains(conflicting.StartNode);
                    var weHavePriority = conflicting.StartNode.IsGiveWay && conflicting.StartNode.PriorityNodes.Contains(ourStartNode);

                    foreach (var approachLane in conflicting.StartNode.IncomingLanes)
                    {
                        if (!_carsPerLane.TryGetValue(approachLane.Id, out var approachCars)) continue;
                        foreach (var other in approachCars)
                        {
                            var otherDistToStart = (1.0 - other.LanePosition) * approachLane.Length;
                            var theirDistToConflict = otherDistToStart + conflict.TheirFraction * conflicting.Length;
                            var checkDist = otherHasPriority ? _config.ConflictCheckDistance : _config.GiveWayCheckDistance;

                            if (otherDistToStart >= checkDist) continue;
                            if (!other.GetCachedPathAhead().Any(p => p.lane.Id == conflicting.Id)) continue;
                            
                            if (weHavePriority) continue;

                            if (other.Speed < 0.5 && theirDistToConflict > myDistToConflict + Car.LengthMeters) continue;
                            
                            // Bypass give-way if the other car is blocked by traffic ahead anyway
                            if (other.Speed < 0.5 && !IsExitRoadClear(conflicting, other)) continue;

                            if (!otherHasPriority) 
                            {
                                if (myDistToConflict < theirDistToConflict - 2.0) continue;
                                if (myDistToConflict < theirDistToConflict + 2.0 && car.Id.CompareTo(other.Id) > 0) continue;
                            }

                            thisConflictMustGiveWay = true;
                            givingWayToInThisConflict = other;
                            break;
                        }
                        if (thisConflictMustGiveWay) break;
                    }
                }

                if (!thisConflictMustGiveWay) continue;
                var myWait = _carWaitTimers.GetValueOrDefault(car.Id);
                var theirWait = _carWaitTimers.GetValueOrDefault(givingWayToInThisConflict!.Id);

                var canOverride = false;
                if (myWait > 1.0 && theirWait > 1.0)
                {
                    if (car.Id.CompareTo(givingWayToInThisConflict.Id) > 0 || myWait > 2.0)
                    {
                        canOverride = true; 
                    }
                }

                if (canOverride) continue;
                hasUnbreakableGiveWay = true;
                break;
            }

            if (hasUnbreakableGiveWay || !IsExitRoadClear(futureLane, car))
            {
                const double stopBuffer = 0.25;
                var distanceToStop = Math.Max(0.0, distToStart - stopBuffer);
                var maxSafeSpeed = Math.Sqrt(2.0 * _config.Deceleration * distanceToStop);
                return Math.Min(speedCap, Math.Max(0.0, maxSafeSpeed));
            }
            break; 
        }
        return speedCap;
    }

    /// <summary>
    /// Prevents cars entering a junction if their exit lane is blocked by traffic
    /// </summary>
    private bool IsExitRoadClear(Lane connectorLane, Car car)
    {
        var pathAhead = car.GetCachedPathAhead();
        var foundConnector = false;
        var requiredGap = Car.LengthMeters * 1.5 + _config.MinFollowingDistance;
        var availableGap = 0.0;

        foreach (var (lane, _, _) in pathAhead)
        {
            if (!foundConnector)
            {
                if (lane.Id == connectorLane.Id) foundConnector = true;
                continue;
            }

            // Start accumulating clear gap sequence AFTER the connector
            if (_carsPerLane.TryGetValue(lane.Id, out var carsOnExit) && carsOnExit.Count > 0)
            {
                var rearmostPosition = carsOnExit.Min(c => c.LanePosition);
                availableGap += rearmostPosition * lane.Length;
                return availableGap > requiredGap;
            }

            availableGap += lane.Length;
            if (availableGap > requiredGap) return true;
        }

        // If path ahead does not run into any cars, space is cleared
        return true;
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
    
        if (enableCarFollowing)
        {
            var (carAhead, distance) = FindCarAhead(car);
            if (carAhead != null)
            {
                var bumperGap = distance - Car.LengthMeters;
                
                if (bumperGap <= _config.MinFollowingDistance)
                {
                    car.SetTargetSpeed(0.0, deltaTime);
                    return true; 
                }
                
                var safeBrakingDist = Math.Max(0.0, bumperGap - _config.MinFollowingDistance);
                var approachSpeed = Math.Sqrt(2.0 * _config.Deceleration * safeBrakingDist);

                var followSpeedCap = approachSpeed + carAhead.Speed * 0.95; 
            
                decision = decision with { SpeedCap = Math.Min(followSpeedCap, decision.SpeedCap) };
            }
        }

        ApplySpeedCap(car, decision.SpeedCap, deltaTime);
        return decision.SpeedCap <= 0.1 && car.Speed < 0.5;
    }

    /// <summary>
    /// Applies target speed limits
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
    /// Final physical bounds check to absolutely prevent crashing into the car ahead
    /// </summary>
    private bool ShouldHoldForLeadVehicleGap(Car car, double deltaTime)
    {
        var (carAhead, distanceToAhead) = FindCarAhead(car);
    
        // Prevent collisions on straight paths (junctions are handled by ConflictConstraint)
        if (carAhead == null || deltaTime <= 0.0) return false;

        var bumperGap = distanceToAhead - Car.LengthMeters;
        
        if (bumperGap <= _config.MinFollowingDistance + 0.01)
        {
            car.SetTargetSpeed(0.0, deltaTime);
            return true; 
        }

        var maxSafeTravel = Math.Max(0.0, bumperGap - _config.MinFollowingDistance);
        var projectedTravel = car.Speed * deltaTime;
    
        return projectedTravel > maxSafeTravel;
    }
    
    /// <summary>
    /// Locates the nearest leading car along the immediate designated route
    /// </summary>
    private (Car? car, double distance) FindCarAhead(Car car)
    {
        if (car.CurrentLane == null) return (null, double.MaxValue);

        var pathAhead = car.GetCachedPathAhead();
        Car? closestCar = null;
        var minDistance = double.MaxValue;

        foreach (var (lane, startDistance, _) in pathAhead)
        {
            // Check cars on our designated path
            if (_carsPerLane.TryGetValue(lane.Id, out var carsOnLane))
            {
                foreach (var otherCar in carsOnLane.Where(c => c.Id != car.Id))
                {
                    double totalDistance;
                    if (lane.Id == car.CurrentLane.Id)
                    {
                        // FIX: Changed <= to < so cars at the exact same position don't turn invisible
                        if (otherCar.LanePosition < car.LanePosition) continue;
                        totalDistance = (otherCar.LanePosition - car.LanePosition) * lane.Length;
                    }
                    else
                    {
                        totalDistance = startDistance + (otherCar.LanePosition * lane.Length);
                    }

                    // FIX: Allow totalDistance of 0 to be caught to trigger emergency stops
                    if (totalDistance < 0 || totalDistance >= minDistance) continue;
                    minDistance = totalDistance;
                    closestCar = otherCar;
                }
            }


        }

        return (closestCar, minDistance);
    }
    


    /// <summary>
    /// Gets the current traffic light phase for all traffic-light-controlled nodes
    /// </summary>
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
    public void ApplyJunctionTimingsFromCell(IEnumerable<Cell> cells)
    {
        foreach (var cell in cells)
        {
            SetTrafficLightTimings(cell.X, cell.Y, cell.GreenDuration, cell.YellowDuration, cell.AllRedDuration);
        }
    }

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
            if (node.NodeType == NodeType.Spawn) return (NodeKind.Spawn, cell.X, cell.Y);
            if (node.NodeType == NodeType.Exit) return (NodeKind.Exit, cell.X, cell.Y);
            if (node.TrafficLight != null) return (NodeKind.TrafficLight, cell.X, cell.Y);
            return null;
        }
    }

    /// <summary>
    /// Weighted-randomly selects a valid exit node
    /// </summary>
    private TrafficNode? SelectDestination(TrafficNode spawnNode)
    {
        if (!_reachableExits.TryGetValue(spawnNode.Id, out var reachable) || reachable.Count == 0)
        {
            return null;
        }
        
        var totalWeight = 0.0;
        foreach (var exit in reachable)
        {
            totalWeight += _exitNodeWeights.GetValueOrDefault(exit.Id, 1.0); // Default to 1 if no exit weight is actually found
        }

        var number = _random.NextDouble() * totalWeight;
        var cumulative = 0.0;
        foreach (var exit in reachable)
        {
            cumulative += _exitNodeWeights.GetValueOrDefault(exit.Id, 1.0);
            if (number < cumulative)
            {
                return exit;
            }
        }

        return reachable[^1];
    }

    /// <summary>
    /// Spawns a car at the given node if sufficient physical space exists
    /// </summary>
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
        var colours = Enum.GetValues<CarColour>();
        var colour = colours[_random.Next(colours.Length)];

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
            reactionScaledSpeed = maxSpawnSpeed * Math.Clamp((minDistAhead - hardMinimumGap) / Math.Max(0.1, balancedGapDistance - hardMinimumGap), 0.0, 1.0);

            var usableGapForStopping = Math.Max(0.0, minDistAhead - hardMinimumGap);
            stoppingEnvelopeSpeed = Math.Sqrt(2.0 * Math.Max(0.1, _config.Deceleration) * usableGapForStopping);
        }

        var initialSpeed = Math.Min(maxSpawnSpeed, Math.Min(reactionScaledSpeed, stoppingEnvelopeSpeed));

        var car = new Car(lane, speedOffset, colour, _config, 0.0, route, destination, initialSpeed);
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
    /// Extracts simplified vehicle poses for UI rendering
    /// </summary>
    public void GetRenderData(List<CarRenderData> output)
    {
        output.Clear();
        lock (_carsLock)
        {
            foreach (var c in _cars)
            {
                var (dx, dy) = c.GetDirection();
                output.Add(new CarRenderData(c.X, c.Y, dx, dy, c.Colour));
            }
        }
    }
}