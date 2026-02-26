namespace TrafficSim.Models;

/// <summary>
/// Physics and Behaviour Constants
/// </summary>
public record SimulationConfig
{
    public double Acceleration { get; init; } = 5.0;
    public double Deceleration { get; init; } = 25.0;
    public double LookaheadDistance { get; init; } = 30.0;
    /// <summary>Gap (metres) at which a car matches the speed of the car ahead.</summary>
    public double SafeFollowingDistance { get; init; } = 5.0;
    /// <summary>Gap (metres) at which a car applies hard braking.</summary>
    public double MinFollowingDistance { get; init; } = 2.0;
    /// <summary>Gap (metres) at which a car begins to slow for slower traffic ahead.</summary>
    public double ReactionDistance { get; init; } = 15.0;

    public static SimulationConfig Default { get; } = new();
}