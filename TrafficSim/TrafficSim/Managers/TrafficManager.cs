using TrafficSim.Models;

namespace TrafficSim.Managers;

public class TrafficManager(GridManager gridManager)
{
    private readonly List<Car> _cars = [];
    private readonly Lock _carsLock = new();
    
    private readonly Dictionary<(int, int), List<Car>> _spatialGrid = new();
    
    private const double SafeFollowingDistance = 5.0;
    private const double MinFollowingDistance = 2.0;
    private const double ReactionDistance = 15.0;
    
    public int GetCarCount()
    {
        lock (_carsLock)
        {
            return _cars.Count;
        }
    }
    
    public void UpdatePhysics(double deltaTime, bool collisionsEnabled)
    {
        if (!gridManager.HasGrid()) return;
        
        lock (_carsLock)
        {
            if (collisionsEnabled)
            {
                UpdateSpatialGrid();
            }
            
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
    }
    
    // Spatial grid used to map car locations onto the grid logically for car position lookup
    private void UpdateSpatialGrid()
    {
        foreach (var list in _spatialGrid.Values)
        {
            list.Clear();
        }
        
        foreach (var car in _cars)
        {
            var cell = gridManager.GetCellFromWorldCoords(car.X, car.Y);
            if (cell == null) continue;
            
            var key = (cell.X, cell.Y);
            if (!_spatialGrid.TryGetValue(key, out var list))
            {
                list = [];
                _spatialGrid[key] = list;
            }
            list.Add(car);
        }
    }
    
    private void ApplyTrafficRules(Car car, double deltaTime)
    {
        var currentCell = gridManager.GetCellFromWorldCoords(car.X, car.Y);
        if (currentCell == null) return;
        
        // Calculate how many cells ahead is needed to check in relation to how long the car needs to react
        var cellsAheadCount = (int)Math.Ceiling(ReactionDistance / gridManager.CellSizeMeters) + 1;
        
        var cellsToCheck = GetCellsAhead(currentCell, car.Direction, cellsAheadCount);

        Car? carAhead = null;
        var minDistance = double.MaxValue;
        
        // Check for cars ahead in the same direction
        foreach (var cellCoord in cellsToCheck)
        {
            if (!_spatialGrid.TryGetValue(cellCoord, out var carsInCell))
                continue;
            
            foreach (var otherCar in carsInCell)
            {
                if (otherCar == car) continue;
                if (!car.IsCarAhead(otherCar)) continue;
                
                var distance = car.GetDistanceTo(otherCar);
                if (!(distance < minDistance)) continue;
                minDistance = distance;
                carAhead = otherCar;
            }
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
    
    private List<(int, int)> GetCellsAhead(Cell currentCell, TrafficDirection direction, int count)
    {
        var cells = new List<(int, int)> { (currentCell.X, currentCell.Y) };
        
        var (dx, dy) = direction switch
        {
            TrafficDirection.North => (0, -1),
            TrafficDirection.South => (0, 1),
            TrafficDirection.East => (1, 0),
            TrafficDirection.West => (-1, 0),
            _ => (0, 0)
        };
        
        var x = currentCell.X;
        var y = currentCell.Y;
        
        for (var i = 0; i < count; i++)
        {
            x += dx;
            y += dy;
            
            var cell = gridManager.GetCellFromGridCoords(x, y);
            if (cell == null) break;
            
            cells.Add((x, y));
            
            // Check new direction if there is one within cells to be checked distance
            if (cell.Type != CellType.Road || cell.Direction == TrafficDirection.None || cell.Direction == direction) continue;
            var newDirection = cell.Direction;
            var (newDx, newDy) = newDirection switch
            {
                TrafficDirection.North => (0, -1),
                TrafficDirection.South => (0, 1),
                TrafficDirection.East => (1, 0),
                TrafficDirection.West => (-1, 0),
                _ => (0, 0)
            };
                
            var newX = x;
            var newY = y;
                
            for (var j = 0; j < 3; j++)
            {
                newX += newDx;
                newY += newDy;
                    
                var nextCell = gridManager.GetCellFromGridCoords(newX, newY);
                if (nextCell == null) break;
                    
                cells.Add((newX, newY));
            }
        }
        
        return cells;
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
        
        lock (_carsLock)
        {
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
        }
        
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
        lock (_carsLock)
        {
            _cars.Clear();
            _spatialGrid.Clear();
        }
    }
    
    public IReadOnlyList<Car> GetCars()
    {
        lock (_carsLock)
        {
            return _cars.ToList();
        }
    }
}