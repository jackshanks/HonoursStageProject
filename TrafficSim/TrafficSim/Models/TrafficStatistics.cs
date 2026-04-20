namespace TrafficSim.Models;

/// <summary>
/// Per-lane data
/// </summary>
public record LaneStatEntry(
    string Route,
    string Type,
    double LengthM,
    double AvgOccupancy,
    double AvgSpeedMph,
    double AvgDensityVehPerKm,
    double AvgFlowVehPerHour
);

/// <summary>
/// Live tracker for simulation statistics
/// </summary>
public class TrafficStatistics
{
    private int _completedVehicles;
    private int _snapshotCount;
    private double _totalNetworkCars;
    private double _totalWeightedSpeedMps;
    private int _speedSnapshotCount;

    private readonly Dictionary<Guid, LaneAccumulator> _laneAccumulators = new();
    
    // Calculated when sim ends
    public int TotalVolume { get; private set; }
    public double FlowVehPerHour { get; private set; }
    public double AverageSpeedMph { get; private set; }
    public double AverageDensityVehPerKm { get; private set; }
    public double SimulationDurationSeconds { get; private set; }
    public IReadOnlyList<LaneStatEntry> PerLaneStats { get; private set; } = [];

    /// <summary>
    /// Called when a car reaches its exit
    /// </summary>
    public void RecordVehicleCompletion() => _completedVehicles++;

    /// <summary>
    /// Records current frame stats for the final average
    /// </summary>
    public void RecordSnapshot(Dictionary<Guid, List<Car>> carsPerLane, IReadOnlyCollection<Lane> allLanes)
    {
        _snapshotCount++;

        var networkCarCount = 0;
        var networkSpeedWeightedSum = 0.0;
        var networkCarsForSpeed = 0;

        foreach (var lane in allLanes)
        {
            if (!_laneAccumulators.TryGetValue(lane.Id, out var acc))
            {
                acc = new LaneAccumulator(lane);
                _laneAccumulators[lane.Id] = acc;
            }

            if (carsPerLane.TryGetValue(lane.Id, out var cars) && cars.Count > 0)
            {
                var speedSum = cars.Sum(c => c.Speed);
                var avgSpeed = speedSum / cars.Count;

                acc.AddSnapshot(cars.Count, avgSpeed);
                networkCarCount += cars.Count;
                networkSpeedWeightedSum += speedSum;
                networkCarsForSpeed += cars.Count;
            }
            else
            {
                acc.AddSnapshot(0, 0.0);
            }
        }

        _totalNetworkCars += networkCarCount;
        if (networkCarsForSpeed <= 0) return;
        _totalWeightedSpeedMps += networkSpeedWeightedSum / networkCarsForSpeed;
        _speedSnapshotCount++;
    }

    /// <summary>
    /// Convertssnapshots into final values
    /// </summary>
    public void Finalise(double totalSimTime, double totalLaneLengthKm)
    {
        SimulationDurationSeconds = totalSimTime;
        TotalVolume = _completedVehicles;

        FlowVehPerHour = totalSimTime > 0 ? TotalVolume / totalSimTime * 3600.0 : 0;

        var avgNetworkCars = _snapshotCount > 0 ? _totalNetworkCars / _snapshotCount : 0;
        AverageDensityVehPerKm = totalLaneLengthKm > 0 ? avgNetworkCars / totalLaneLengthKm : 0;
        AverageSpeedMph = _speedSnapshotCount > 0 ? _totalWeightedSpeedMps / _speedSnapshotCount * 2.23694 : 0;

        var laneStats = new List<LaneStatEntry>();
        foreach (var acc in _laneAccumulators.Values)
        {
            var entry = acc.ToStatEntry();
            if (entry.AvgOccupancy > 0)
            {
                laneStats.Add(entry);
            }
        }
        PerLaneStats = laneStats.OrderByDescending(e => e.AvgFlowVehPerHour).ToList();
    }

    /// <summary>
    /// Tracks a single lane's stats over time
    /// </summary>
    private sealed class LaneAccumulator(Lane lane)
    {
        private int _snapshotCount;
        private double _totalCarCount;
        private double _totalSpeedSum;
        private int _snapshotsWithCars;

        public void AddSnapshot(int carCount, double avgSpeed)
        {
            _snapshotCount++;
            _totalCarCount += carCount;
            if (carCount <= 0) return;
            _totalSpeedSum += avgSpeed;
            _snapshotsWithCars++;
        }

        public LaneStatEntry ToStatEntry()
        {
            var avgOccupancy = _snapshotCount > 0 ? _totalCarCount / _snapshotCount : 0.0;
            var avgSpeedMps = _snapshotsWithCars > 0 ? _totalSpeedSum / _snapshotsWithCars : 0.0;
            var avgSpeedMph = avgSpeedMps * 2.23694;
            var lengthKm = lane.Length / 1000.0;
            var avgDensity = lengthKm > 0 ? avgOccupancy / lengthKm : 0.0;
            var avgFlow = avgDensity * (avgSpeedMps * 3.6);

            var route = $"({lane.StartNode.GridX},{lane.StartNode.GridY})\u2192({lane.EndNode.GridX},{lane.EndNode.GridY})";
            var type = lane.Type == LaneType.Straight ? "Straight" : "Curved";

            return new LaneStatEntry(
                route,
                type,
                Math.Round(lane.Length, 1),
                Math.Round(avgOccupancy, 2),
                Math.Round(avgSpeedMph, 1),
                Math.Round(avgDensity, 1),
                Math.Round(avgFlow, 1)
            );
        }
    }
}
