namespace TrafficSim.Models;

/// <summary>
/// Physics and Behaviour Constants
/// </summary>
public record SimulationConfig
{
    /// <summary>Speed (meters) to accelerate</summary>
    public double Acceleration { get; init; } = 5.0;
    /// <summary>Speed (meters) to decelerate</summary>
    public double Deceleration { get; init; } = 25.0;
    /// <summary>Distance (meters) to react to other vehicles</summary>
    public double LookaheadDistance { get; init; } = 30.0;
    /// <summary>Gap (metres) at which a car matches the speed of the car ahead.</summary>
    public double SafeFollowingDistance { get; init; } = 5.0;
    /// <summary>Gap (metres) at which a car applies hard braking.</summary>
    public double MinFollowingDistance { get; init; } = 2.0;
    /// <summary>Gap (metres) at which a car begins to slow for slower traffic ahead.</summary>
    public double ReactionDistance { get; init; } = 15.0;
    /// <summary>Seconds between automatic car spawns at each spawn node.</summary>
    public double SpawnInterval { get; init; } = 3.0;
    /// <summary>Gap (metres) from the end of a give-way lane at which a car starts yielding.</summary>
    public double GiveWayCheckDistance { get; init; } = 12.0;
    /// <summary>Gap (metres) within which another car is considered a conflict at a junction.</summary>
    public double ConflictCheckDistance { get; init; } = 20.0;

    public static SimulationConfig Default { get; } = new();
}