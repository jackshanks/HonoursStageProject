namespace TrafficSim.Models;

/// <summary>
/// Represents a car in the traffic simulation. 
/// Handles spatial positioning, movement mathematics, and route traversal.
/// </summary>
public class Car
{
    // Unique identifier for each car, handy for tracing specific vehicles through the simulation.
    public Guid Id { get; } = Guid.NewGuid();
    
    // Absolute position coordinates in the overall grid
    public double X { get; private set; }
    public double Y { get; private set; }
    
    // Current speed of the vehicle in metres per second (m/s)
    public double Speed { get; private set; }
    
    // Individual driver behaviour offset - makes some drivers naturally faster or slower than the speed limit
    public double SpeedOffsetMps { get; }
    
    private readonly double _fallbackMaxSpeed;
    
    // Dynamically calculate max speed based on the current lane, ensuring speed never dips below zero.
    // If we're somehow not on a lane, default to the fallback speed.
    public double MaxSpeed => CurrentLane != null ? Math.Max(CurrentLane.SpeedLimitMps + SpeedOffsetMps, 0) : _fallbackMaxSpeed;
    
    private readonly SimulationConfig _config;

    // The visual colour of the car (using American spelling for the C# property, but storing a set enum value)
    public CarColour Colour { get; }
    
    // Standard car dimensions in metres - based loosely on standard UK road cars
    public const double WidthMeters = 2.0;
    public const double LengthMeters = 3.5;

    // What lane are we currently traversing?
    public Lane? CurrentLane { get; private set; }
    
    // Progression through the current lane, scaled from 0.0 (start) to 1.0 (end)
    public double LanePosition { get; private set; }

    // X/Y directional vectors, used mainly for calculation and rendering the car's rotation
    private double _directionX;
    private double _directionY;

    private Lane? _cachedPathLane;
    private readonly List<Lane> _cachedLaneSequence = [];
    private readonly List<(Lane lane, double distanceToStart, double distanceToEnd)> _cachedPathAheadResult = [];

    // The predefined path of lanes from the start point to the end destination
    private readonly List<Lane>? _route;
    
    // Tracks where we are in the route list
    private int _routeIndex;
    
    // Where the car is ultimately trying to get to
    public TrafficNode? Destination { get; }

    public Car(Lane startLane, double speedOffsetMps, CarColour colour, SimulationConfig config, double startPosition = 0.0, List<Lane>? route = null, TrafficNode? destination = null, double? startSpeed = null)
    {
        // Initialise the car's starting state
        CurrentLane = startLane;
        
        // Clamp to prevent the car spawning outside the bounds of the lane
        LanePosition = Math.Clamp(startPosition, 0.0, 1.0);
        SpeedOffsetMps = speedOffsetMps;
        
        _fallbackMaxSpeed = Math.Max(startLane.SpeedLimitMps + speedOffsetMps, 0);
        Speed = Math.Min(startSpeed ?? _fallbackMaxSpeed, _fallbackMaxSpeed);
        Colour = colour;
        _config = config;
        _route = route;
        _routeIndex = 1; // Start at 1 as 0 is our startLane
        Destination = destination;

        // Calculate our starting World X and Y based on our proportional position along the starting lane
        var pos = startLane.GetPositionAt(LanePosition);
        X = pos.X;
        Y = pos.Y;

        // Orient the car to face the direction of the lane
        (_directionX, _directionY) = startLane.GetDirectionAt(LanePosition);
    }

    /// <summary>
    /// Updates the car's position based on its current speed and the time elapsed.
    /// </summary>
    /// <returns>True if the car successfully moved, False if it has run out of route/track.</returns>
    public bool Move(double deltaTime)
    {
        if (CurrentLane == null)
        {
            return false;
        }
        
        // Distance = Speed * Time
        var distanceMeters = Speed * deltaTime;
        
        // Convert real distance into a fractional progression through the Lane's total length
        var progression = distanceMeters / CurrentLane.Length;
        LanePosition += progression;
        
        // Check if we've reached the end of the current lane
        if (LanePosition >= 1.0)
        {
            // If fully out of the lane, try to continue to the next one. 
            // Use our predefined route if we have one, otherwise just see what lane connects at the node.
            var nextLane = _route != null ? GetNextRouteLane() : CurrentLane.EndNode.GetNextLane();
            
            // If there's nowhere to go, the car has reached the end of the line. Time to despawn.
            if (nextLane == null)
            {
                return false;
            }
            
            // Carry over any excess distance we travelled into the new lane
            var overflow = LanePosition - 1.0;
            var oldLaneLength = CurrentLane.Length;
            
            // Move onto the new lane
            CurrentLane = nextLane;
            LanePosition = 0.0;

            if (overflow > 0)
            {
                // Convert the overflow fraction back to metres, then scale it to the size of our new lane
                var overflowMeters = overflow * oldLaneLength;
                LanePosition = Math.Min(overflowMeters / CurrentLane.Length, 1.0);
            }
        }
        
        // Update our physical coordinates for rendering and logic checks
        var pos = CurrentLane.GetPositionAt(LanePosition);
        X = pos.X;
        Y = pos.Y;
        
        // Update our orientation
        (_directionX, _directionY) = CurrentLane.GetDirectionAt(LanePosition);
        
        return true;
    }
    
    public (double dx, double dy) GetDirection()
    {
        return (_directionX, _directionY);
    }
    
    public void Accelerate(double deltaTime)
    {
        // Increase speed according to our config's acceleration rate, capping at our maximum allowed speed.
        Speed = Math.Min(Speed + _config.Acceleration * deltaTime, MaxSpeed);
    }
    
    public void Decelerate(double deltaTime)
    {
        // Brake according to config, ensuring we don't end up going backwards!
        Speed = Math.Max(Speed - _config.Deceleration * deltaTime, 0);
    }
    
    public void SetTargetSpeed(double targetSpeed, double deltaTime)
    {
        // Smoothly accelerate or brake to try and match the target speed
        if (Speed < targetSpeed)
        {
            Speed = Math.Min(Speed + _config.Acceleration * deltaTime, targetSpeed);
        }
        else if (Speed > targetSpeed)
        {
            Speed = Math.Max(Speed - _config.Deceleration * deltaTime, targetSpeed);
        }
    }
    
    /// <summary>
    /// Looks ahead along the car's upcoming route to build a sequence of upcoming lanes and distances.
    /// Essential for collision detection and yielding at junctions.
    /// </summary>
    public List<(Lane lane, double distanceToStart, double distanceToEnd)> GetCachedPathAhead()
    {
        // Build out the sequence of lanes up to our lookahead distance
        BuildLaneSequence(_config.LookaheadDistance);
        _cachedPathLane = CurrentLane;

        _cachedPathAheadResult.Clear();
        var currentDist = 0.0;

        if (CurrentLane != null)
        {
            // First, add the remaining portion of the lane we are currently on
            var remainingOnCurrent = (1.0 - LanePosition) * CurrentLane.Length;
            _cachedPathAheadResult.Add((CurrentLane, 0, remainingOnCurrent));
            currentDist += remainingOnCurrent;
        }

        // Next, append all the fully upcoming lanes to our path result
        foreach (var lane in _cachedLaneSequence)
        {
            _cachedPathAheadResult.Add((lane, currentDist, currentDist + lane.Length));
            currentDist += lane.Length;
        }

        return _cachedPathAheadResult;
    }

    // Fetches the next lane in our planned route and bumps the index tracker
    private Lane? GetNextRouteLane()
    {
        if (_route == null || _routeIndex >= _route.Count)
        {
            return null; // Route is finished
        }
        return _route[_routeIndex++];
    }

    // Populates the _cachedLaneSequence with the upcoming lanes the car intends to visit
    private void BuildLaneSequence(double maxDist)
    {
        _cachedLaneSequence.Clear();
        if (CurrentLane == null)
        {
            return;
        }
        
        // Start our accumulator with the distance left on our current lane
        var accumulated = (1.0 - LanePosition) * CurrentLane.Length;

        if (_route != null)
        {
            // Walk through our planned route until we hit the max lookahead distance
            for (var i = _routeIndex; i < _route.Count && accumulated < maxDist; i++)
            {
                _cachedLaneSequence.Add(_route[i]);
                accumulated += _route[i].Length;
            }
        }
        else
        {
            // Fallback: If we don't have a rigid route, just grab the next available lane blindly
            var curr = CurrentLane;
            
            while (accumulated < maxDist)
            {
                var next = curr.EndNode.GetNextLane();
                if (next == null) break; // Dead end
                
                _cachedLaneSequence.Add(next);
                accumulated += next.Length;
                curr = next;
            }
        }
    }
}

// Allowed colours for rendering the vehicles
public enum CarColour
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

// Lightweight struct to bundle position and appearance data for the rendering engine
public record struct CarRenderData(double X, double Y, double DX, double DY, CarColour Colour);
