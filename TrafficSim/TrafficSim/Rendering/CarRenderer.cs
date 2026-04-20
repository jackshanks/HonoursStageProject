using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TrafficSim.Models;

namespace TrafficSim.Rendering;

/// <summary>
/// Handles drawing all vehicles on the UI canvas
/// </summary>
public class CarRenderer
{
    private readonly DrawingVisual _carsVisual;
    
    private static readonly Dictionary<CarColour, Brush> ColorBrushMap = new()
    {
        { CarColour.Red, Brushes.Red },
        { CarColour.Blue, Brushes.Blue },
        { CarColour.Green, Brushes.Green },
        { CarColour.Orange, Brushes.Orange },
        { CarColour.Purple, Brushes.Purple },
        { CarColour.DarkCyan, Brushes.DarkCyan },
        { CarColour.Crimson, Brushes.Crimson },
        { CarColour.DarkOrange, Brushes.DarkOrange }
    };
    
    private static readonly Pen BlackPen = new(Brushes.Black, 0.5);
    
    static CarRenderer()
    {
        BlackPen.Freeze();
    }
    
    public CarRenderer(Canvas canvas)
    {
        _carsVisual = new DrawingVisual();

        var host = new DrawingVisualHost(_carsVisual);
        Panel.SetZIndex(host, 100);
        canvas.Children.Add(host);
    }
    
    /// <summary>
    /// Repaints every car for the current frame
    /// </summary>
    public void UpdateAllCarVisuals(List<CarRenderData> renderData, double pixelsPerMeter)
    {
        using var dc = _carsVisual.RenderOpen();
        
        foreach (var carData in renderData)
        {
            DrawCar(dc, carData, pixelsPerMeter);
        }
    }
    
    /// <summary>
    /// Translates real-world dimensions to pixels and rotates the vehicle
    /// </summary>
    private static void DrawCar(DrawingContext dc, CarRenderData carData, double pixelsPerMeter)
    {
        var widthPx = Car.WidthMeters * pixelsPerMeter;
        var lengthPx = Car.LengthMeters * pixelsPerMeter;
        
        var brush = ColorBrushMap.GetValueOrDefault(carData.Colour, Brushes.Gray);
        
        var carX = carData.X * pixelsPerMeter;
        var carY = carData.Y * pixelsPerMeter;
        
        var rect = new Rect(-lengthPx / 2, -widthPx / 2, lengthPx, widthPx);
        
        var angleRadians = Math.Atan2(carData.DY, carData.DX);
        var angleDegrees = angleRadians * 180.0 / Math.PI;
        
        var matrix = Matrix.Identity;
        matrix.Rotate(angleDegrees);
        matrix.Translate(carX, carY);
        dc.PushTransform(new MatrixTransform(matrix));

        dc.DrawRectangle(brush, BlackPen, rect);

        dc.Pop();
    }
    
    /// <summary>
    /// Removes all drawn cars from the canvas
    /// </summary>
    public void ClearAllVisuals()
    {
        using var dc = _carsVisual.RenderOpen();
    }
}
