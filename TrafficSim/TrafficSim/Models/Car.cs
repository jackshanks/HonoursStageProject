namespace TrafficSim.Models;

public class Car
{
    public Guid Id { get; } = Guid.NewGuid();
    
    public double X { get; private set; }
    public double Y { get; private set; }
    public double Speed { get; private set; }
    public double MaxSpeed { get; }
    private const double Deceleration = 25.0;
    private const double Acceleration = 5.0;
    
    public CarColor Color { get; }
    public const double WidthMeters = 2.0;
    public const double LengthMeters = 3.5;
    
    public Lane? CurrentLane { get; private set; }
    public double LanePosition { get; private set; }
    
    private double _directionX;
    private double _directionY;
    
    private const double LookaheadDistance = 30.0;
    
    private Lane? _cachedPathLane;
    private List<Lane> _cachedLaneSequence;
    
    
    public Car(Lane startLane, double speed, double startPosition = 0.0)
    {
        CurrentLane = startLane;
        LanePosition = Math.Clamp(startPosition, 0.0, 1.0);
        Speed = speed;
        MaxSpeed = speed;
        
        var pos = startLane.GetPositionAt(LanePosition);
        X = pos.X;
        Y = pos.Y;
        
        (_directionX, _directionY) = startLane.GetDirectionAt(LanePosition);
        
        var random = new Random(Guid.NewGuid().GetHashCode());
        var colors = Enum.GetValues<CarColor>();
        Color = colors[random.Next(colors.Length)];
    }

    public bool Move(double deltaTime)
    {
        if (CurrentLane == null)
            return false;
        
        var distanceMeters = Speed * deltaTime;
        
        // Convert distance into length through lane
        var progression = distanceMeters / CurrentLane.Length;
        LanePosition += progression;
        
        if (LanePosition >= 1.0)
        {
            // If fully out of lane continue to next lane
            var nextLane = CurrentLane.EndNode.GetNextLane();
            
            if (nextLane == null)
            {
                return false;
            }
            
            var overflow = LanePosition - 1.0;
            var oldLaneLength = CurrentLane.Length;
            CurrentLane = nextLane;
            LanePosition = 0.0;

            if (overflow > 0)
            {
                var overflowMeters = overflow * oldLaneLength;
                LanePosition = Math.Min(overflowMeters / CurrentLane.Length, 1.0);
            }
        }
        
        var pos = CurrentLane.GetPositionAt(LanePosition);
        X = pos.X;
        Y = pos.Y;
        
        (_directionX, _directionY) = CurrentLane.GetDirectionAt(LanePosition);
        
        return true;
    }
    
    public (double dx, double dy) GetDirection()
    {
        return (_directionX, _directionY);
    }
    
    public void Accelerate(double deltaTime)
    {
        Speed = Math.Min(Speed + Acceleration * deltaTime, MaxSpeed);
    }
    
    public void Decelerate(double deltaTime)
    {
        Speed = Math.Max(Speed - Deceleration * deltaTime, 0);
    }
    
    public void SetTargetSpeed(double targetSpeed, double deltaTime)
    {
        if (Speed < targetSpeed)
        {
            Speed = Math.Min(Speed + Acceleration * deltaTime, targetSpeed);
        }
        else if (Speed > targetSpeed)
        {
            Speed = Math.Max(Speed - Deceleration * deltaTime, targetSpeed);
        }
    }
    
    public List<(Lane lane, double distanceToStart, double distanceToEnd)> GetCachedPathAhead()
    {
        // Rebuild only if we changed lanes or don't have a cache
        if (_cachedPathLane != CurrentLane)
        {
            _cachedLaneSequence = BuildLaneSequence(LookaheadDistance);
            _cachedPathLane = CurrentLane;
        }
        
        var result = new List<(Lane, double, double)>();
        var currentDist = 0.0;
        
        if (CurrentLane != null)
        {
            var remainingOnCurrent = (1.0 - LanePosition) * CurrentLane.Length;
            result.Add((CurrentLane, 0, remainingOnCurrent));
            currentDist += remainingOnCurrent;
        }
        
        foreach(var lane in _cachedLaneSequence)
        {
            result.Add((lane, currentDist, currentDist + lane.Length));
            currentDist += lane.Length;
        }

        return result;
    }
    
    private List<Lane> BuildLaneSequence(double maxDist)
    {
        var lanes = new List<Lane>();
        if (CurrentLane == null) return lanes;
        var accumulated = (1.0 - LanePosition) * CurrentLane.Length; // Approx
        var curr = CurrentLane;
    
        while(accumulated < maxDist)
        {
            var next = curr.EndNode.GetNextLane();
            if(next == null) break;
            lanes.Add(next);
            accumulated += next.Length;
            curr = next;
        }

        return lanes;
    }
    
    public double GetDistanceTo(Car other)
    {
        if (CurrentLane == null || other.CurrentLane == null)
            return double.MaxValue;
        
        var pathAhead = GetCachedPathAhead();
        
        foreach (var (lane, startDistance, endDistance) in pathAhead)
        {
            if (lane.Id != other.CurrentLane.Id) continue;
            var distanceAlongLane = other.LanePosition * lane.Length;
            var totalDistance = startDistance + distanceAlongLane;
            
            if (totalDistance > 0)
            {
                return totalDistance;
            }
        }

        return double.MaxValue;
    }
}

public enum CarColor
{
    Red,
    Blue,
    Green,
    Orange,
    Purple,
    DarkCyan,
    Crimson,
    DarkOrange
}

public record struct CarRenderData(double X, double Y, double DX, double DY, CarColor Color);
