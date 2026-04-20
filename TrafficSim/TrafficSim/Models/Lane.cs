using System.Windows;

namespace TrafficSim.Models;

/// <summary>
/// A physical track connecting two traffic nodes that cars actually drive along
/// </summary>
public class Lane
{
    public Guid Id { get; }
    
    // Where the lane starts and ends
    public TrafficNode StartNode { get; }
    public TrafficNode EndNode { get; }
    
    // Whether this lane is a straight line or a curve
    public LaneType Type { get; }
    
    // Real-world length in metres (used for calculating time to traverse)
    public double Length { get; private set; }
    
    // Point used to bend the Bezier curve for corners
    private Point? ControlPoint { get; }
    
    // Directions of the lane segment
    public TrafficDirection StartDirection { get; }
    public TrafficDirection EndDirection { get; }
    
    // Speed limit in metres per second
    public double SpeedLimitMps { get; }

    // List of other lanes that physically cross this one (creates yield scenarios)
    public List<Lane> ConflictingLanes { get; } = [];
    
    // Exact fractional points where this lane intersects with others
    public List<LaneConflict> Conflicts { get; } = [];
    
    // True if this lane exists solely inside a junction to connect other lanes
    public bool IsJunctionConnector { get; internal set; }
    
    private readonly Point _startPoint;
    private readonly Point _endPoint;
    
    // Cached direction so we don't recalculate trig every frame for straight lines
    private readonly (double dx, double dy) _straightDirection;

    public Lane(TrafficNode startNode, TrafficNode endNode, TrafficDirection startDirection, TrafficDirection endDirection, double speedLimitMps = 30 * 0.44704) // conversion to mps from 30 mph
    {
        Id = Guid.NewGuid();
        StartNode = startNode;
        EndNode = endNode;
        StartDirection = startDirection;
        EndDirection = endDirection;
        SpeedLimitMps = speedLimitMps;

        _startPoint = new Point(startNode.X, startNode.Y);
        _endPoint = new Point(endNode.X, endNode.Y);

        if (startDirection != endDirection)
        {
            // A lot of maths and code just to make a nice looking turn
            Type = LaneType.Curved;
            ControlPoint = CalculateControlPoint(startNode, endNode, startDirection, endDirection);
            Length = CalculateCurveLength();
        }
        else
        {
            Type = LaneType.Straight;
            Length = CalculateStraightLength();
            
            // Calculates the direction, and allows us to pre-define direction if straight
            var dx = _endPoint.X - _startPoint.X;
            var dy = _endPoint.Y - _startPoint.Y;
            var len = Math.Sqrt(dx * dx + dy * dy);
            _straightDirection = len > 0 ? (dx / len, dy / len) : (0, 0);
        }
        
        // Link this lane back to its parent nodes so graph traversal works
        startNode.OutgoingLanes.Add(this);
        endNode.IncomingLanes.Add(this);
    }
    
    // Gets X/Y coordinates based on a fraction (t) between 0.0 (start) and 1.0 (end)
    public Point GetPositionAt(double t)
    {
        t = Math.Clamp(t, 0.0, 1.0);
        
        if (Type == LaneType.Straight)
        {
            // Simple linear interpolation
            return new Point(
                _startPoint.X + (_endPoint.X - _startPoint.X) * t,
                _startPoint.Y + (_endPoint.Y - _startPoint.Y) * t
            );
        }

        // quadratic bezier B(t) = (1-t)²P0 + 2(1-t)tP1 + t²P2 - math makes my head hurt
        var oneMinusT = 1.0 - t;
        var cp = ControlPoint!.Value;
            
        return new Point(
            oneMinusT * oneMinusT * _startPoint.X + 
            2 * oneMinusT * t * cp.X + 
            t * t * _endPoint.X,
                
            oneMinusT * oneMinusT * _startPoint.Y + 
            2 * oneMinusT * t * cp.Y + 
            t * t * _endPoint.Y
        );
    }
    
    // Gets the direction vector the car should face at position 't'
    public (double dx, double dy) GetDirectionAt(double t)
    {
        t = Math.Clamp(t, 0.0, 1.0);
        
        if (Type == LaneType.Straight)
        {
            return _straightDirection;
        }
        
        // quadratic bezier B'(t) = 2(1-t)(P1-P0) + 2t(P2-P1) - lots of math
        var oneMinusT = 1.0 - t;
        var cp = ControlPoint!.Value;
            
        var dx = 2 * oneMinusT * (cp.X - _startPoint.X) + 2 * t * (_endPoint.X - cp.X);
        var dy = 2 * oneMinusT * (cp.Y - _startPoint.Y) + 2 * t * (_endPoint.Y - cp.Y);
            
        var length = Math.Sqrt(dx * dx + dy * dy);
        return length > 0 ? (dx / length, dy / length) : (0, 0);
    }
    
    // Finds the intersection point to bend the corner around
    private static Point CalculateControlPoint(TrafficNode startNode, TrafficNode endNode, 
        TrafficDirection startDir, TrafficDirection endDir)
    {
        var startX = startNode.X;
        var startY = startNode.Y;
        var endX = endNode.X;
        var endY = endNode.Y;
        
        var controlX = startDir switch
        {
            TrafficDirection.East => endX,
            TrafficDirection.West => endX,
            _ => startX
        };
        
        var controlY = startDir switch
        {
            TrafficDirection.North => endY,
            TrafficDirection.South => endY,
            _ => startY
        };
        
        return new Point(controlX, controlY);
    }
    
    private double CalculateStraightLength()
    {
        var dx = _endPoint.X - _startPoint.X;
        var dy = _endPoint.Y - _startPoint.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }
    
    // Estimates curve length by breaking it into small straight segments
    private double CalculateCurveLength()
    {
        const int segments = 20;
        var totalLength = 0.0;
        
        for (var i = 0; i < segments; i++)
        {
            var t0 = (double)i / segments;
            var t1 = (double)(i + 1) / segments;
            
            var p0 = GetPositionAt(t0);
            var p1 = GetPositionAt(t1);
            
            var dx = p1.X - p0.X;
            var dy = p1.Y - p0.Y;
            totalLength += Math.Sqrt(dx * dx + dy * dy);
        }
        
        return totalLength;
    }
}

public enum LaneType
{
    Straight,
    Curved
}

/// <summary>
/// Data mapping exactly where two lanes cross over each other
/// </summary>
public struct LaneConflict
{
    public Lane ConflictingLane { get; set; }
    public double MyFraction { get; set; } // Position 0.0-1.0 on my lane
    public double TheirFraction { get; set; } // Position 0.0-1.0 on their lane
}