using System.Windows;
using System.Windows.Media;

namespace TrafficSim.Rendering;

/// <summary>
/// DrawingVisualHost class to avoid duplicate code in Car + Grid renderer
/// </summary>
internal class DrawingVisualHost : FrameworkElement
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