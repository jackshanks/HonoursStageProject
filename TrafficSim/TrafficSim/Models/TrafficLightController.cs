namespace TrafficSim.Models;

/// <summary>
/// Controls the traffic light phases for a single junction group
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
    /// Updates the phase durations.
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
                _timer = 0.0;
                break;
            case TrafficLightPhase.Yellow when _timer >= _yellowDuration:
                _currentSubPhase = TrafficLightPhase.Red;
                _timer = 0.0;
                break;
            case TrafficLightPhase.Red when _timer >= _allRedDuration:
                _currentPhaseIndex = (_currentPhaseIndex + 1) % phaseGroups.Count;
                _currentSubPhase = TrafficLightPhase.Green;
                _timer = 0.0;
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }
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

        return activeGroup.Contains(direction) ? _currentSubPhase : TrafficLightPhase.Red;
    }
}
