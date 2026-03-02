using TrafficSim.Models;

namespace TrafficSim.Utility;

public static class TrafficDirectionUtility
{
    public static TrafficDirection GetOpposite(TrafficDirection direction) => direction switch
    {
        TrafficDirection.North => TrafficDirection.South,
        TrafficDirection.South => TrafficDirection.North,
        TrafficDirection.East  => TrafficDirection.West,
        TrafficDirection.West  => TrafficDirection.East,
        _                      => TrafficDirection.None
    };
}