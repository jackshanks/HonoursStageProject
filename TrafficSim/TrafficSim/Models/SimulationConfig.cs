namespace TrafficSim.Models;

/// <summary>
/// Physics and Behaviour Constants
/// </summary>
public record SimulationConfig
{
    /// <summary>Acceleration rate (m/s²)</summary>
    public double Acceleration { get; init; } = 2.5;
    /// <summary>Deceleration rate (m/s²)</summary>
    public double Deceleration { get; init; } = 7.0;
    /// <summary>Distance (meters) to react to other vehicles</summary>
    public double LookaheadDistance { get; init; } = 30.0;
    /// <summary>Gap (metres) at which a car matches the speed of the car ahead.</summary>
    public double SafeFollowingDistance { get; init; } = 5.0;
    /// <summary>Gap (metres) at which a car applies hard braking.</summary>
    public double MinFollowingDistance { get; init; } = 2.0;
    /// <summary>Gap (metres) at which a car begins to slow for slower traffic ahead.</summary>
    public double ReactionDistance { get; init; } = 15.0;
    /// <summary>Seconds between automatic car spawns at each spawn node.</summary>
    public double SpawnInterval { get; init; } = 7.5;
    /// <summary>Maximum buffered spawn releases allowed from one node per physics tick.</summary>
    public int SpawnQueueMaxReleasePerTick { get; init; } = 1;
    /// <summary>Minimum time between two successful releases from the same spawn node (seconds).</summary>
    public double SpawnMinInterReleaseSeconds { get; init; } = 0.15;
    /// <summary>Multiplier on reaction distance used for balanced spawn gap speed scaling.</summary>
    public double SpawnBalancedGapFactor { get; init; } = 0.7;
    /// <summary>Gap (metres) from the end of a give-way lane at which a car starts to give way.</summary>
    public double GiveWayCheckDistance { get; init; } = 12.0;
    /// <summary>Distance (meters) at which a car starts to slow when approaching any give-way, regardless of traffic.</summary>
    public double GiveWayApproachDistance { get; init; } = 25.0;
    /// <summary>% of MaxSpeed a car is capped at inside the give-way approach zone.</summary>
    public double GiveWayApproachSpeedFactor { get; init; } = 0.5;
    /// <summary>Gap (metres) within which another car is considered a conflict at a junction.</summary>
    public double ConflictCheckDistance { get; init; } = 20.0;
    /// <summary>Seconds that a traffic light phase stays green.</summary>
    public double TrafficLightGreenDuration { get; init; } = 20.0;
    /// <summary>Seconds that a traffic light phase stays yellow.</summary>
    public double TrafficLightYellowDuration { get; init; } = 3.0;
    /// <summary>Seconds of all-red gap between phases.</summary>
    public double TrafficLightAllRedDuration { get; init; } = 1.0;

    public static SimulationConfig Default { get; } = new();
}