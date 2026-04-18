using TrafficSim.Models;

namespace TrafficSim.Utility;

public static class TrafficUtility
{
    public static TrafficDirection GetOpposite(TrafficDirection direction) => direction switch
    {
        TrafficDirection.North => TrafficDirection.South,
        TrafficDirection.South => TrafficDirection.North,
        TrafficDirection.East  => TrafficDirection.West,
        TrafficDirection.West  => TrafficDirection.East,
        _                      => TrafficDirection.None
    };

    public readonly record struct TrafficSignalState( TrafficNode? LightNode, TrafficLightPhase? Phase, double DistanceToStopLine, bool OnApproachLane);
    
    public static TrafficSignalState FindNearestTrafficSignal(Car car)
    {
        foreach (var (lane, distToLaneStart, _) in car.GetCachedPathAhead())
        {
            if (lane.EndNode.TrafficLight == null)
            {
                continue;
            }

            var laneRemaining = lane.Id == car.CurrentLane!.Id ? (1.0 - car.LanePosition) * lane.Length : lane.Length;
            var distToStopLine = distToLaneStart + laneRemaining;
            var lightNode = lane.EndNode;
            return new TrafficSignalState( lightNode, lightNode.TrafficLight!.GetPhaseForNode(lightNode), distToStopLine, lane == car.CurrentLane);
        }

        return new TrafficSignalState(null, null, 0.0, false);
    }
}