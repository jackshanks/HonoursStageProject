using TrafficSim.Models;

namespace TrafficSim.Managers;

/// <summary>
/// Manages and tracks all vehicles as they go through the network, as well as creating the network
/// </summary>
/// <param name="gridManager"></param>
public class TrafficManager(GridManager gridManager)
{
    private readonly List<Car> _cars = [];
    private readonly Lock _carsLock = new();
    
    private LaneNetwork? _laneNetwork;
    private bool _isNetworkBuilt;
    
    private readonly Dictionary<Guid, List<Car>> _carsPerLane = new();
    
    private const double SafeFollowingDistance = 5.0;
    private const double MinFollowingDistance = 2.0;
    private const double ReactionDistance = 15.0;
    
    public bool BuildNetwork(Cell[,] grid, int width, int height, double cellSizeMeters)
    {
        lock (_carsLock)
        {
            _cars.Clear();
            _carsPerLane.Clear();
            
            _laneNetwork = NetworkManager.BuildNetwork(grid, width, height, cellSizeMeters);
            _isNetworkBuilt = true;
            
            return NetworkManager.ValidateNetwork(_laneNetwork);
        }
    }
    
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
    
    public void UpdatePhysics(double deltaTime, bool collisionsEnabled)
    {
        lock (_carsLock)
        {
            if (!_isNetworkBuilt || _laneNetwork == null) 
                return;
            
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
    }
    
    private void ApplyTrafficRules(Car car, double deltaTime)
    {
        if (car.CurrentLane == null)
            return;
        
        var carAhead = FindCarAhead(car);
        
        if (carAhead != null)
        {
            var distance = car.GetDistanceTo(carAhead);
            distance -= Car.LengthMeters;

            switch (distance)
            {
                // Cars will break sharply at this distance
                case < MinFollowingDistance:
                    car.Decelerate(deltaTime);
                    break;
                case < SafeFollowingDistance:
                {
                    var targetSpeed = Math.Min(carAhead.Speed, car.MaxSpeed);
                    car.SetTargetSpeed(targetSpeed * 0.8, deltaTime);
                    break;
                }
                // Cars will begin slowing down at this distance is there is a slow traffic ahead
                case < ReactionDistance:
                {
                    var targetSpeed = car.MaxSpeed * (distance / ReactionDistance);
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
    /// <param name="car"></param>
    /// <returns></returns>
    private Car? FindCarAhead(Car car)
    {
        if (car.CurrentLane == null)
            return null;
        
        var pathAhead = car.GetCachedPathAhead();
        
        Car? closestCar = null;
        var minDistance = double.MaxValue;
        
        foreach (var (lane, startDistance, endDistance) in pathAhead)
        {
            if (!_carsPerLane.TryGetValue(lane.Id, out var carsOnLane))
                continue;

            foreach (var otherCar in carsOnLane.Where(otherCar => otherCar.Id != car.Id))
            {
                if (lane.Id == car.CurrentLane.Id)
                {
                    if (otherCar.LanePosition <= car.LanePosition)
                        continue;
                }
                
                var distanceAlongLane = otherCar.LanePosition * lane.Length;
                var totalDistance = startDistance + distanceAlongLane;

                if (!(totalDistance > 0) || !(totalDistance < minDistance)) continue;
                minDistance = totalDistance;
                closestCar = otherCar;
            }
        }
        
        return closestCar;
    }

    public bool SpawnCarAt(double pixelX, double pixelY)
    {
        lock (_carsLock)
        {
            if (!_isNetworkBuilt || _laneNetwork == null)
                return false;
            
            var cell = gridManager.GetCellFromPixel(pixelX, pixelY);
            if (cell == null)
                return false;
            
            var node = _laneNetwork.GetNodeAt(cell.X, cell.Y);
            if (node == null || node.OutgoingLanes.Count == 0)
                return false;
            
            var random = new Random();
            var lane = node.OutgoingLanes[random.Next(node.OutgoingLanes.Count)];
            
            // Check if there is a car to close
            if (_carsPerLane.TryGetValue(lane.Id, out var carsOnLane))
            {
                foreach (var existingCar in carsOnLane)
                {
                    var distance = existingCar.LanePosition * lane.Length;
                    if (distance < MinFollowingDistance * 2)
                    {
                        return false;
                    }
                }
            }
            
            var speed = 10.0 + random.NextDouble() * 10.0;
            
            var car = new Car(lane, speed, startPosition: 0.0);
            _cars.Add(car);
        }
        
        return true;
    }
    
    public void ClearTraffic()
    {
        lock (_carsLock)
        {
            _cars.Clear();
            _carsPerLane.Clear();
        }
    }
    
    public void ClearNetwork()
    {
        lock (_carsLock)
        {
            _cars.Clear();
            _carsPerLane.Clear();
            _laneNetwork?.Clear();
            _laneNetwork = null;
            _isNetworkBuilt = false;
        }
    }
    
    public List<CarRenderData> GetRenderData()
    {
        lock (_carsLock)
        {
            return _cars.Select(c => {
                var (dx, dy) = c.GetDirection();
                return new CarRenderData(c.X, c.Y, dx, dy, c.Color);
            }).ToList();
        }
    }
}