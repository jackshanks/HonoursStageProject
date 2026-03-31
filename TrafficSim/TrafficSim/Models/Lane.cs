using System.Windows;

namespace TrafficSim.Models;

/// <summary>
/// Represents a connection between two traffic nodes.
/// Can be either straight or curved (using quadratic Bezier).
/// </summary>
public class Lane
{
    public Guid Id { get; }
    public TrafficNode StartNode { get; }
    public TrafficNode EndNode { get; }
    public LaneType Type { get; }
    public double Length { get; private set; }
    private Point? ControlPoint { get; }
    
    public TrafficDirection StartDirection { get; }
    public TrafficDirection EndDirection { get; }
    public double SpeedLimitMps { get; }
    
    private readonly Point _startPoint;
    private readonly Point _endPoint;
    // Chached direction so isn't calculated on fly for simple node to node
    private readonly (double dx, double dy) _straightDirection;

    public Lane(TrafficNode startNode, TrafficNode endNode, TrafficDirection startDirection, TrafficDirection endDirection, double speedLimitMps = 30 * 0.44704) //conversion to mps from 30 mph
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
        
        startNode.OutgoingLanes.Add(this);
        endNode.IncomingLanes.Add(this);
    }
    
    public Point GetPositionAt(double t)
    {
        t = Math.Clamp(t, 0.0, 1.0);
        
        if (Type == LaneType.Straight)
        {
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