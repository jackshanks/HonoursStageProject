using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace TrafficSim.Models;

public class Car
{
    public Guid Id { get; } = Guid.NewGuid();
    public double X { get; private set; }
    public double Y { get; private set; }
    public double Speed { get; private set; }
    public double MaxSpeed { get; }
    public TrafficDirection Direction { get; set; }
    public CarColor Color { get; }

    public const double WidthMeters = 2.0;
    public const double LengthMeters = 3.5;
    
    private const double Deceleration = 10.0;
    private const double Acceleration = 5.0;
    public Car(double startX, double startY, double speed, TrafficDirection direction)
    {
        X = startX;
        Y = startY;
        Speed = speed;
        MaxSpeed = speed;
        Direction = direction;
        
        var random = new Random(Guid.NewGuid().GetHashCode());
        var colors = Enum.GetValues<CarColor>();
        Color = colors[random.Next(colors.Length)];
    }

    public void Move(double deltaTime)
    {
        var distance = Speed * deltaTime;

        switch (Direction)
        {
            case TrafficDirection.North: 
                Y -= distance; 
                break;
            case TrafficDirection.South: 
                Y += distance; 
                break;
            case TrafficDirection.East:  
                X += distance; 
                break;
            case TrafficDirection.West:  
                X -= distance; 
                break;
            case TrafficDirection.None:
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }
    }
    
    public void SetPosition(double x, double y)
    {
        X = x;
        Y = y;
    }
    
    public void Accelerate(double deltaTime)
    {
        Speed = Math.Min(Speed + Acceleration * deltaTime, MaxSpeed);
    }
    
    public void Decelerate(double deltaTime)
    {
        Speed = Math.Max(Speed - Deceleration * deltaTime, 0);
    }
    
    public void SetTargetSpeed(double targetSpeed, double deltaTime)
    {
        if (Speed < targetSpeed)
        {
            Speed = Math.Min(Speed + Acceleration * deltaTime, targetSpeed);
        }
        else if (Speed > targetSpeed)
        {
            Speed = Math.Max(Speed - Deceleration * deltaTime, targetSpeed);
        }
    }
    
    public double GetDistanceTo(Car other)
    {
        return Direction switch
        {
            TrafficDirection.North => Y - other.Y,
            TrafficDirection.South => other.Y - Y,
            TrafficDirection.East => other.X - X,
            TrafficDirection.West => X - other.X,
            _ => double.MaxValue
        };
    }
    
    public bool IsCarAhead(Car other, double laneWidth = 4.0)
    {
        if (Direction != other.Direction) 
            return false;
        
        var perpDistance = Direction switch
        {
            TrafficDirection.North or TrafficDirection.South => Math.Abs(X - other.X),
            TrafficDirection.East or TrafficDirection.West => Math.Abs(Y - other.Y),
            _ => double.MaxValue
        };
        
        if (perpDistance > laneWidth) 
            return false;
        
        var forwardDistance = GetDistanceTo(other);
        return forwardDistance > 0;
    }
}

public enum CarColor
{
    Red,
    Blue,
    Green,
    Orange,
    Purple,
    DarkCyan,
    Crimson,
    DarkOrange
}
