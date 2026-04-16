namespace TrafficSim.Models;

/// <summary>
/// Controls the traffic light phases for a single junction group.
/// One instance per traffic-light junction. Cycles through phase groups
/// (e.g., North/South green then East/West green) with yellow and all-red gaps.
/// </summary>
public class TrafficLightController( List<HashSet<TrafficDirection>> phaseGroups, SimulationConfig config)
{
    private double _greenDuration = config.TrafficLightGreenDuration;
    private double _yellowDuration = config.TrafficLightYellowDuration;
    private double _allRedDuration = config.TrafficLightAllRedDuration;

    private int _currentPhaseIndex;
    private TrafficLightPhase _currentSubPhase = TrafficLightPhase.Green;
    private double _timer;

    /// <summary>
    /// Updates the phase durations. Called under _carsLock from TrafficManager.
    /// </summary>
    public void SetTimings(double green, double yellow, double allRed)
    {
        _greenDuration = green;
        _yellowDuration = yellow;
        _allRedDuration = allRed;
    }

    /// <summary>
    /// Returns the current phase durations.
    /// </summary>
    public (double green, double yellow, double allRed) GetTimings()
    {
        return (_greenDuration, _yellowDuration, _allRedDuration);
    }

    /// <summary>
    /// Advance the traffic light timer and cycle phases when needed.
    /// Called once per physics tick inside the cars lock.
    /// </summary>
    public void Update(double deltaTime)
    {
        if (phaseGroups.Count == 0)
        {
            return;
        }

        _timer += deltaTime;

        switch (_currentSubPhase)
        {
            case TrafficLightPhase.Green when _timer >= _greenDuration:
                _currentSubPhase = TrafficLightPhase.Yellow;
                break;
            case TrafficLightPhase.Yellow when _timer >= _yellowDuration:
                // Transition to all-red gap
                _currentSubPhase = TrafficLightPhase.Red;
                break;
            case TrafficLightPhase.Red when _timer >= _allRedDuration:
                // Advance to the next phase group and go green
                _currentPhaseIndex = (_currentPhaseIndex + 1) % phaseGroups.Count;
                _currentSubPhase = TrafficLightPhase.Green;
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }

        _timer = 0.0;
    }

    /// <summary>
    /// Gets the current traffic light phase for a given approach node.
    /// </summary>
    public TrafficLightPhase GetPhaseForNode(TrafficNode node)
    {
        if (phaseGroups.Count == 0)
        {
            return TrafficLightPhase.Green;
        }

        var direction = node.ApproachDirection;
        var activeGroup = phaseGroups[_currentPhaseIndex];

        if (activeGroup.Contains(direction))
        {
            // This node's direction is currently active
            return _currentSubPhase;
        }

        // This node's direction is not active
        return TrafficLightPhase.Red;
    }
}
