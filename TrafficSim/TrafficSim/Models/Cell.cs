using System.Windows;
using System.Windows.Media;
using System.Windows.Shapes;

namespace TrafficSim.Models;

public class Cell(int x, int y, double cellSizeMeters = 4.0)
{
    public int X { get; } = x;
    public int Y { get; } = y;
    public double RealWorldX { get; } = x * cellSizeMeters; // X position in meters
    public double RealWorldY { get; } = y * cellSizeMeters; // Y position in meters
    public CellType Type { get; set; } = CellType.Empty;
    public TrafficDirection Direction { get; set; } = TrafficDirection.None;
    public Rectangle VisualElement { get; } = new();
    public Polygon ArrowElement { get; } = new();

    public void UpdateVisual(double cellSizePixels)
    {
        VisualElement.Width = cellSizePixels;
        VisualElement.Height = cellSizePixels;
        VisualElement.Stroke = Brushes.LightGray;
        VisualElement.StrokeThickness = 0.5;
        
        VisualElement.Fill = Type switch
        {
            CellType.Road => Brushes.Black,
            CellType.Intersection => Brushes.Blue,
            _ => Brushes.White
        };
        
        UpdateArrow(cellSizePixels);
    }
    
    private void UpdateArrow(double cellSizePixels)
    {
        ArrowElement.Points.Clear();
        
        if (Type == CellType.Road && Direction != TrafficDirection.None)
        {
            var center = cellSizePixels / 2;
            var arrowSize = cellSizePixels * 0.4;
            
            ArrowElement.Points =
            [
                new Point(center - arrowSize / 2, center + arrowSize / 2), // Left Base
                new Point(center, center - arrowSize / 2), // Tip of arrow
                new Point(center + arrowSize / 2, center + arrowSize / 2) // Right Base
            ];
            
            double angle = Direction switch
            {
                TrafficDirection.East => 90,
                TrafficDirection.South => 180,
                TrafficDirection.West => 270,
                _ => 0
            };
            
            // !This makes this entire section work by offloading the rotation of arrows to the renderer
            ArrowElement.RenderTransform = new RotateTransform(angle, center, center);
            ArrowElement.Fill = Brushes.Yellow;
            ArrowElement.Stroke = Brushes.Black;
            ArrowElement.StrokeThickness = 1;
            ArrowElement.Visibility = Visibility.Visible;
        }
        else
        {
            ArrowElement.Visibility = Visibility.Collapsed;
        }
    }
}

public enum CellType
{
    Empty,
    Road,
    Intersection
}

public enum TrafficDirection
{
    None,
    North,
    East,
    South,
    West
}