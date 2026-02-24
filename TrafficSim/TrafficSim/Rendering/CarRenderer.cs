using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TrafficSim.Models;

namespace TrafficSim.Rendering;

public class CarRenderer
{
    private readonly DrawingVisual _carsVisual;
    
    private static readonly Dictionary<CarColor, Brush> ColorBrushMap = new()
    {
        { CarColor.Red, Brushes.Red },
        { CarColor.Blue, Brushes.Blue },
        { CarColor.Green, Brushes.Green },
        { CarColor.Orange, Brushes.Orange },
        { CarColor.Purple, Brushes.Purple },
        { CarColor.DarkCyan, Brushes.DarkCyan },
        { CarColor.Crimson, Brushes.Crimson },
        { CarColor.DarkOrange, Brushes.DarkOrange }
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
    
    public void UpdateAllCarVisuals(List<CarRenderData> renderData, double pixelsPerMeter)
    {
        using var dc = _carsVisual.RenderOpen();
        
        foreach (var carData in renderData)
        {
            DrawCar(dc, carData, pixelsPerMeter);
        }
    }
    
    private static void DrawCar(DrawingContext dc, CarRenderData carData, double pixelsPerMeter)
    {
        var widthPx = Car.WidthMeters * pixelsPerMeter;
        var lengthPx = Car.LengthMeters * pixelsPerMeter;
        
        var brush = ColorBrushMap.GetValueOrDefault(carData.Color, Brushes.Gray);
        
        var carX = carData.X * pixelsPerMeter;
        var carY = carData.Y * pixelsPerMeter;
        
        var rect = new Rect(-lengthPx / 2, -widthPx / 2, lengthPx, widthPx);
        
        var angleRadians = Math.Atan2(carData.DY, carData.DX);
        var angleDegrees = angleRadians * 180.0 / Math.PI;
        
        dc.PushTransform(new TranslateTransform(carX, carY));
        dc.PushTransform(new RotateTransform(angleDegrees));
        
        dc.DrawRectangle(brush, BlackPen, rect);
        
        dc.Pop();
        dc.Pop();
    }
    
    public void ClearAllVisuals()
    {
        using var dc = _carsVisual.RenderOpen();
    }
    
    private class DrawingVisualHost : FrameworkElement
    {
        private readonly VisualCollection _children;
        
        public DrawingVisualHost(DrawingVisual visual)
        {
            _children = new VisualCollection(this) { visual };
        }
        
        protected override int VisualChildrenCount => _children.Count;
        
        protected override Visual GetVisualChild(int index)
        {
            if (index < 0 || index >= _children.Count)
                throw new ArgumentOutOfRangeException(nameof(index));
            return _children[index];
        }
    }
}
