using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using TrafficSim.Models;

namespace TrafficSim.Rendering;

public class CarRenderer(Canvas canvas)
{
    private readonly Dictionary<Guid, Rectangle> _carVisuals = new();
    
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
    
    public void UpdateCarVisual(Car car, double pixelsPerMeter)
    {
        if (!_carVisuals.TryGetValue(car.Id, out var rectangle))
        {
            rectangle = new Rectangle
            {
                Fill = ColorBrushMap.GetValueOrDefault(car.Color, Brushes.Gray),
                Stroke = Brushes.Black,
                StrokeThickness = 0.5
            };
            Panel.SetZIndex(rectangle, 100);
            canvas.Children.Add(rectangle);
            _carVisuals[car.Id] = rectangle;
        }
        
        var widthPx = Car.WidthMeters * pixelsPerMeter;
        var lengthPx = Car.LengthMeters * pixelsPerMeter;
        
        if (car.Direction is TrafficDirection.North or TrafficDirection.South)
        {
            rectangle.Width = widthPx;
            rectangle.Height = lengthPx;
        }
        else
        {
            rectangle.Width = lengthPx;
            rectangle.Height = widthPx;
        }
        
        Canvas.SetLeft(rectangle, car.X * pixelsPerMeter - rectangle.Width / 2);
        Canvas.SetTop(rectangle, car.Y * pixelsPerMeter - rectangle.Height / 2);
    }
    
    public void UpdateAllCarVisuals(IReadOnlyList<Car> cars, double pixelsPerMeter)
    {
        var currentCarIds = new HashSet<Guid>(cars.Select(c => c.Id));
        
        var removedIds = _carVisuals.Keys.Where(id => !currentCarIds.Contains(id)).ToList();
        foreach (var id in removedIds)
        {
            RemoveCarVisual(id);
        }
        
        foreach (var car in cars)
        {
            UpdateCarVisual(car, pixelsPerMeter);
        }
    }
    
    public void RemoveCarVisual(Guid carId)
    {
        if (!_carVisuals.TryGetValue(carId, out var rectangle)) return;
        
        canvas.Children.Remove(rectangle);
        _carVisuals.Remove(carId);
    }
    
    public void ClearAllVisuals()
    {
        foreach (var rectangle in _carVisuals.Values)
        {
            canvas.Children.Remove(rectangle);
        }
        _carVisuals.Clear();
    }
}
