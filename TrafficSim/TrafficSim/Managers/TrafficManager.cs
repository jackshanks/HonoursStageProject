using System.Linq;
using TrafficSim.Models;

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

    private readonly Random _random = new();
    
    public bool BuildNetwork(Cell[,] grid, int width, int height, double cellSizeMeters)
    {
        lock (_carsLock)
        {
            _cars.Clear();
            _carsPerLane.Clear();
            
            _laneNetwork = NetworkManager.BuildNetwork(grid, width, height, cellSizeMeters);
            
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
            if (_laneNetwork == null) 
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

        var emptyKeys = _carsPerLane.Where(kvp => kvp.Value.Count == 0).Select(kvp => kvp.Key).ToList();
        foreach (var key in emptyKeys)
        {
            _carsPerLane.Remove(key);
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
                case var d when d < _config.MinFollowingDistance:
                    car.Decelerate(deltaTime);
                    break;
                case var d when d < _config.SafeFollowingDistance:
                {
                    var targetSpeed = Math.Min(carAhead.Speed, car.MaxSpeed);
                    car.SetTargetSpeed(targetSpeed * 0.8, deltaTime);
                    break;
                }
                // Cars will begin slowing down at this distance is there is a slow traffic ahead
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
        
        return closestCar;
    }

    public bool SpawnCarAt(double pixelX, double pixelY)
    {
        lock (_carsLock)
        {
            if (_laneNetwork == null)
                return false;
            
            var cell = gridManager.GetCellFromPixel(pixelX, pixelY);
            if (cell == null)
                return false;
            
            var node = _laneNetwork.GetNodeAt(cell.X, cell.Y);
            if (node == null || node.OutgoingLanes.Count == 0)
                return false;
            
            var lane = node.OutgoingLanes[_random.Next(node.OutgoingLanes.Count)];
            
            // Check if there is a car to close
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

            var car = new Car(lane, speed, color, _config, startPosition: 0.0);
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
            ClearTraffic();
            _laneNetwork?.Clear();
            _laneNetwork = null;
        }
    }
    
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