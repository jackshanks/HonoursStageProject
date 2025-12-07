using TrafficSim.Models;

namespace TrafficSim.Managers;

public class TrafficManager(GridManager gridManager)
{
    private readonly List<Car> _cars = [];
    
    private const double SafeFollowingDistance = 5.0;
    private const double MinFollowingDistance = 2.0;
    private const double ReactionDistance = 15.0;
    
    public int GetCarCount() => _cars.Count;
    
    public void UpdatePhysics(double deltaTime, bool collisionsEnabled)
    {
        if (!gridManager.HasGrid()) return;
        
        for (var i = _cars.Count - 1; i >= 0; i--)
        {
            var car = _cars[i];
            
            UpdateCarDirection(car);
            
            if (collisionsEnabled)
            {
                ApplyTrafficRules(car, deltaTime);
            }
            else
            {
                car.Accelerate(deltaTime);
            }
            
            car.Move(deltaTime);
            
            if (IsOutOfBounds(car))
            {
                _cars.RemoveAt(i);
            }
        }
    }
    
    private void ApplyTrafficRules(Car car, double deltaTime)
    {
        Car? carAhead = null;
        var minDistance = double.MaxValue;
        
        foreach (var otherCar in _cars)
        {
            if (otherCar == car) continue;

            if (!car.IsCarAhead(otherCar)) continue;
            var distance = car.GetDistanceTo(otherCar);
            if (!(distance < minDistance)) continue;
            minDistance = distance;
            carAhead = otherCar;
        }
        
        if (carAhead != null)
        {
            var distance = minDistance - Car.LengthMeters;

            switch (distance)
            {
                case < MinFollowingDistance:
                    car.Decelerate(deltaTime);
                    break;
                case < SafeFollowingDistance:
                {
                    var targetSpeed = Math.Min(carAhead.Speed, car.MaxSpeed);
                    car.SetTargetSpeed(targetSpeed * 0.8, deltaTime);
                    break;
                }
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
    
    private void UpdateCarDirection(Car car)
    {
        var cell = gridManager.GetCellFromWorldCoords(car.X, car.Y);

        if (cell is not { Type: CellType.Road } || cell.Direction == TrafficDirection.None) return;
        if (car.Direction == cell.Direction) return;
        car.Direction = cell.Direction;
        
        var halfCell = gridManager.CellSizeMeters / 2.0;
        car.SetPosition(cell.RealWorldX + halfCell, cell.RealWorldY + halfCell);
    }

    public bool SpawnCarAt(double pixelX, double pixelY)
    {
        var cell = gridManager.GetCellFromPixel(pixelX, pixelY);
        
        // Can only spawn on roads with valid direction
        if (cell is not { Type: CellType.Road } || cell.Direction == TrafficDirection.None) 
            return false;
        
        // Calculate spawn position (center of cell)
        var halfCell = gridManager.CellSizeMeters / 2.0;
        var startX = cell.RealWorldX + halfCell;
        var startY = cell.RealWorldY + halfCell;
        
        // Check if there is a car too close to where it's being spawned
        foreach (var existingCar in _cars)
        {
            var dx = existingCar.X - startX;
            var dy = existingCar.Y - startY;
            var distance = Math.Sqrt(dx * dx + dy * dy);
            
            if (distance < MinFollowingDistance)
            {
                return false;
            }
        }
        
        var random = new Random();
        var speed = 10.0 + random.NextDouble() * 10.0;
        
        var car = new Car(startX, startY, speed, cell.Direction);
        _cars.Add(car);
        
        return true;
    }

    private bool IsOutOfBounds(Car car)
    {
        var widthMeters = gridManager.GetTotalWidthMeters();
        var heightMeters = gridManager.GetTotalHeightMeters();

        const double margin = 10.0;
        return car.X < -margin || car.X > widthMeters + margin || 
               car.Y < -margin || car.Y > heightMeters + margin;
    }
    
    public void ClearTraffic()
    {
        _cars.Clear();
    }
    
    public IReadOnlyList<Car> GetCars() => _cars.AsReadOnly();
}