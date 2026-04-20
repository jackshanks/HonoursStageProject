using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Globalization;
using TrafficSim.Models;

namespace TrafficSim.Rendering;

/// <summary>
/// Handles all visual rendering for the traffic UI grid
/// </summary>
public class GridRenderer
{
    private readonly Canvas _canvas;
    private readonly DrawingVisual _gridVisual;
    private Cell[,]? _grid;
    private int _width;
    private int _height;
    private double _cellSizePixels;

    private readonly Point[] _arrowPointsNorth = new Point[3];
    private readonly Point[] _arrowPointsEast = new Point[3];
    private readonly Point[] _arrowPointsSouth = new Point[3];
    private readonly Point[] _arrowPointsWest = new Point[3];

    private readonly HashSet<(int, int)> _giveWayNodes = [];
    private readonly Dictionary<(int, int), TrafficLightPhase> _trafficLightNodes = new();
    private readonly HashSet<(int, int)> _spawnNodes = [];
    private readonly Dictionary<(int, int), double> _spawnBacklogs = new();
    private readonly HashSet<(int, int)> _exitNodes = [];
    private (int x, int y)? _selectedNode;

    public bool IsEditMode { get; set; } = true;

    private readonly List<(double cx, double cy)> _junctionGroupCenters = [];

    private static readonly Pen GridLinePen = MakeFrozenPen(new SolidColorBrush(Color.FromRgb(165, 160, 148)), 0.5);
    private static readonly Pen ArrowPen = MakeFrozenPen(new SolidColorBrush(Color.FromRgb(32, 32, 32)), 1);
    private static readonly Pen GiveWayPen = MakeFrozenPen(new SolidColorBrush(Color.FromRgb(132, 42, 42)), 1.5);
    private static readonly Pen SelectedNodePen = MakeFrozenPen(new SolidColorBrush(Color.FromRgb(47, 79, 111)), 2.0);

    private static Pen MakeFrozenPen(Brush brush, double thickness)
    {
        var pen = new Pen(brush, thickness);
        pen.Freeze();
        return pen;
    }

    public GridRenderer(Canvas canvas)
    {
        _canvas = canvas;
        _gridVisual = new DrawingVisual();

        var host = new DrawingVisualHost(_gridVisual);
        Panel.SetZIndex(host, 0);
        _canvas.Children.Add(host);
    }

    /// <summary>
    /// Mounts the grid dimensions and calculates vector boundaries
    /// </summary>
    public void CreateVisuals(Cell[,] grid, int width, int height, double cellSizePixels)
    {
        _grid = grid;
        _width = width;
        _height = height;
        _cellSizePixels = cellSizePixels;

        _canvas.Width = width * cellSizePixels;
        _canvas.Height = height * cellSizePixels;

        PreCalculateArrowPoints(cellSizePixels);
        RenderGrid();
    }

    /// <summary>
    /// Pre-calculates directional arrows to save rendering time
    /// </summary>
    private void PreCalculateArrowPoints(double cellSize)
    {
        var center = cellSize / 2;
        var arrowSize = cellSize * 0.4;

        _arrowPointsNorth[0] = new Point(center - arrowSize / 2, center + arrowSize / 2);
        _arrowPointsNorth[1] = new Point(center, center - arrowSize / 2);
        _arrowPointsNorth[2] = new Point(center + arrowSize / 2, center + arrowSize / 2);

        _arrowPointsEast[0] = new Point(center - arrowSize / 2, center - arrowSize / 2);
        _arrowPointsEast[1] = new Point(center + arrowSize / 2, center);
        _arrowPointsEast[2] = new Point(center - arrowSize / 2, center + arrowSize / 2);

        _arrowPointsSouth[0] = new Point(center - arrowSize / 2, center - arrowSize / 2);
        _arrowPointsSouth[1] = new Point(center, center + arrowSize / 2);
        _arrowPointsSouth[2] = new Point(center + arrowSize / 2, center - arrowSize / 2);

        _arrowPointsWest[0] = new Point(center + arrowSize / 2, center - arrowSize / 2);
        _arrowPointsWest[1] = new Point(center - arrowSize / 2, center);
        _arrowPointsWest[2] = new Point(center + arrowSize / 2, center + arrowSize / 2);
    }

    /// <summary>
    /// Primary loop to iterate and draw every active cell
    /// </summary>
    public void RenderGrid()
    {
        if (_grid == null) return;

        using var renderOpen = _gridVisual.RenderOpen();

        var emptyBrush = new SolidColorBrush(Color.FromRgb(248, 247, 242));
        var roadBrush = new SolidColorBrush(Color.FromRgb(118, 118, 112));
        var intersectionBrush = new SolidColorBrush(Color.FromRgb(137, 147, 156));
        var arrowBrush = new SolidColorBrush(Color.FromRgb(245, 240, 214));

        for (var x = 0; x < _width; x++)
        {
            for (var y = 0; y < _height; y++)
            {
                var cell = _grid[x, y];
                var rect = new Rect(x * _cellSizePixels, y * _cellSizePixels, _cellSizePixels, _cellSizePixels);

                var brush = cell.Type switch
                {
                    CellType.Road => roadBrush,
                    CellType.Intersection => intersectionBrush,
                    _ => emptyBrush
                };

                renderOpen.DrawRectangle(brush, GridLinePen, rect);

                if (cell.Type == CellType.Road && cell.Direction != TrafficDirection.None)
                {
                    DrawArrow(renderOpen, cell, x, y, arrowBrush, ArrowPen);
                }

                if (_giveWayNodes.Contains((x, y)))
                {
                    DrawGiveWayTriangle(renderOpen, x, y);
                }
                else if (_trafficLightNodes.TryGetValue((x, y), out var lightPhase))
                {
                    DrawTrafficLight(renderOpen, x, y, lightPhase);
                }
                else if (cell.Type == CellType.Intersection)
                {
                    if (cell.JunctionType == JunctionType.TrafficLight)
                        DrawTrafficLight(renderOpen, x, y, TrafficLightPhase.Green);
                    else
                        DrawGiveWayTriangle(renderOpen, x, y);
                }

                if (_spawnNodes.Contains((x, y)))
                {
                    DrawSpawnIndicator(renderOpen, x, y);
                    DrawSpawnBacklogBadge(renderOpen, x, y, _spawnBacklogs.GetValueOrDefault((x, y), 0.0));
                }

                if (_exitNodes.Contains((x, y)))
                {
                    DrawExitIndicator(renderOpen, x, y);
                }

                if (_selectedNode.HasValue && _selectedNode.Value == (x, y))
                {
                    DrawSelectedHighlight(renderOpen, x, y);
                }
            }
        }

        foreach (var (cx, cy) in _junctionGroupCenters)
        {
            DrawGearIcon(renderOpen, cx, cy);
        }
    }

    public void SetJunctionGroupCenters(IEnumerable<(double cx, double cy)> centers)
    {
        _junctionGroupCenters.Clear();
        _junctionGroupCenters.AddRange(centers);
        RenderGrid();
    }

    public void ClearJunctionGroupCenters()
    {
        _junctionGroupCenters.Clear();
        RenderGrid();
    }

    public void SetGiveWayNodes(IEnumerable<(int gridX, int gridY)> nodes)
    {
        _giveWayNodes.Clear();
        foreach (var pos in nodes) _giveWayNodes.Add(pos);
        RenderGrid();
    }

    public void ClearGiveWayNodes()
    {
        _giveWayNodes.Clear();
        RenderGrid();
    }

    public void SetTrafficLightNodes(List<(int gridX, int gridY, TrafficLightPhase phase)> nodes)
    {
        _trafficLightNodes.Clear();
        foreach (var (gx, gy, phase) in nodes) _trafficLightNodes[(gx, gy)] = phase;
        RenderGrid();
    }

    public void ClearTrafficLightNodes()
    {
        _trafficLightNodes.Clear();
        RenderGrid();
    }

    public void SetSpawnNodes(IEnumerable<(int gridX, int gridY)> nodes)
    {
        _spawnNodes.Clear();
        foreach (var pos in nodes) _spawnNodes.Add(pos);
        RenderGrid();
    }

    public void ClearSpawnNodes()
    {
        _spawnNodes.Clear();
        RenderGrid();
    }

    public void SetSpawnBacklogs(IEnumerable<(int gridX, int gridY, double backlog)> nodes)
    {
        _spawnBacklogs.Clear();
        foreach (var (gx, gy, backlog) in nodes) _spawnBacklogs[(gx, gy)] = Math.Max(0.0, backlog);
        RenderGrid();
    }

    public void ClearSpawnBacklogs()
    {
        _spawnBacklogs.Clear();
        RenderGrid();
    }

    public void SetExitNodes(IEnumerable<(int gridX, int gridY)> nodes)
    {
        _exitNodes.Clear();
        foreach (var pos in nodes) _exitNodes.Add(pos);
        RenderGrid();
    }

    public void ClearExitNodes()
    {
        _exitNodes.Clear();
        RenderGrid();
    }

    public void SetSelectedNode(int gridX, int gridY)
    {
        _selectedNode = (gridX, gridY);
        RenderGrid();
    }

    public void ClearSelectedNode()
    {
        _selectedNode = null;
        RenderGrid();
    }

    /// <summary>
    /// Computes intersection triangle geometry
    /// </summary>
    private void DrawGiveWayTriangle(DrawingContext dc, int gridX, int gridY)
    {
        var offsetX = gridX * _cellSizePixels;
        var offsetY = gridY * _cellSizePixels;
        var cx = offsetX + _cellSizePixels / 2;
        var cy = offsetY + _cellSizePixels / 2;
        var half = _cellSizePixels * 0.22;

        var p0 = new Point(cx - half, cy - half * 0.6);
        var p1 = new Point(cx + half, cy - half * 0.6);
        var p2 = new Point(cx, cy + half);

        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            ctx.BeginFigure(p0, isFilled: true, isClosed: true);
            ctx.LineTo(p1, isStroked: true, isSmoothJoin: false);
            ctx.LineTo(p2, isStroked: true, isSmoothJoin: false);
        }
        geo.Freeze();
        dc.DrawGeometry(Brushes.White, GiveWayPen, geo);
    }

    /// <summary>
    /// Draws the settings hub icon over a junction
    /// </summary>
    private void DrawGearIcon(DrawingContext dc, double pixelCx, double pixelCy)
    {
        var fontSize = Math.Max(8.0, _cellSizePixels * 0.55);
        var formatted = new FormattedText(
            "⚙",
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface("Segoe UI"),
            fontSize,
            new SolidColorBrush(Color.FromRgb(35, 35, 35)),
            1.0);

        dc.DrawText(formatted, new Point(pixelCx - formatted.Width / 2, pixelCy - formatted.Height / 2));
    }

    private void DrawTrafficLight(DrawingContext dc, int gridX, int gridY, TrafficLightPhase phase)
    {
        var cx = gridX * _cellSizePixels + _cellSizePixels / 2;
        var cy = gridY * _cellSizePixels + _cellSizePixels / 2;
        var radius = _cellSizePixels * 0.25;

        var brush = phase switch
        {
            TrafficLightPhase.Green => new SolidColorBrush(Color.FromRgb(76, 138, 82)),
            TrafficLightPhase.Yellow => new SolidColorBrush(Color.FromRgb(196, 156, 68)),
            TrafficLightPhase.Red => new SolidColorBrush(Color.FromRgb(156, 70, 70)),
            _ => Brushes.Gray
        };

        dc.DrawEllipse(brush, null, new Point(cx, cy), radius, radius);
    }

    private void DrawSpawnIndicator(DrawingContext dc, int gridX, int gridY)
    {
        var ox = gridX * _cellSizePixels;
        var oy = gridY * _cellSizePixels;
        var size = _cellSizePixels * 0.22;
        var margin = _cellSizePixels * 0.06;
        var right = ox + _cellSizePixels - margin;
        var top = oy + margin;

        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            ctx.BeginFigure(new Point(right - size, top + size), isFilled: true, isClosed: true);
            ctx.LineTo(new Point(right, top + size), isStroked: false, isSmoothJoin: false);
            ctx.LineTo(new Point(right - size / 2, top), isStroked: false, isSmoothJoin: false);
        }
        geo.Freeze();
        dc.DrawGeometry(new SolidColorBrush(Color.FromRgb(76, 138, 82)), null, geo);
    }

    private void DrawSpawnBacklogBadge(DrawingContext dc, int gridX, int gridY, double backlog)
    {
        var backlogCount = (int)Math.Floor(backlog);
        var label = backlogCount.ToString(CultureInfo.InvariantCulture);

        var fontSize = Math.Max(9.0, _cellSizePixels * 0.28);
        var formattedText = new FormattedText(
            label,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface("Segoe UI"),
            fontSize,
            new SolidColorBrush(Color.FromRgb(248, 247, 242)),
            1.0);

        var centerX = gridX * _cellSizePixels + _cellSizePixels * 0.26;
        var centerY = gridY * _cellSizePixels + _cellSizePixels * 0.23;
        var textX = centerX - formattedText.Width / 2;
        var textY = centerY - formattedText.Height / 2;

        var padding = Math.Max(2.0, _cellSizePixels * 0.08);
        var badgeRect = new Rect(
            textX - padding,
            textY - padding * 0.8,
            formattedText.Width + padding * 2,
            formattedText.Height + padding * 1.6);

        dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(220, 58, 56, 50)), null, badgeRect);
        dc.DrawText(formattedText, new Point(textX, textY));
    }

    private void DrawExitIndicator(DrawingContext dc, int gridX, int gridY)
    {
        var ox = gridX * _cellSizePixels;
        var oy = gridY * _cellSizePixels;
        var size = _cellSizePixels * 0.22;
        var margin = _cellSizePixels * 0.06;
        var right = ox + _cellSizePixels - margin;
        var top = oy + margin;

        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            ctx.BeginFigure(new Point(right - size, top), isFilled: true, isClosed: true);
            ctx.LineTo(new Point(right, top), isStroked: false, isSmoothJoin: false);
            ctx.LineTo(new Point(right - size / 2, top + size), isStroked: false, isSmoothJoin: false);
        }
        geo.Freeze();
        dc.DrawGeometry(new SolidColorBrush(Color.FromRgb(156, 70, 70)), null, geo);
    }

    private void DrawSelectedHighlight(DrawingContext dc, int gridX, int gridY)
    {
        var rect = new Rect(
            gridX * _cellSizePixels + 1,
            gridY * _cellSizePixels + 1,
            _cellSizePixels - 2,
            _cellSizePixels - 2);
        dc.DrawRectangle(null, SelectedNodePen, rect);
    }

    private void DrawArrow(DrawingContext dc, Cell cell, int gridX, int gridY, Brush fill, Pen stroke)
    {
        var points = cell.Direction switch
        {
            TrafficDirection.North => _arrowPointsNorth,
            TrafficDirection.East => _arrowPointsEast,
            TrafficDirection.South => _arrowPointsSouth,
            TrafficDirection.West => _arrowPointsWest,
            _ => null
        };

        if (points == null) return;

        var offsetX = gridX * _cellSizePixels;
        var offsetY = gridY * _cellSizePixels;

        var streamGeometry = new StreamGeometry();
        using (var ctx = streamGeometry.Open())
        {
            ctx.BeginFigure(new Point(points[0].X + offsetX, points[0].Y + offsetY), true, true);
            ctx.LineTo(new Point(points[1].X + offsetX, points[1].Y + offsetY), true, false);
            ctx.LineTo(new Point(points[2].X + offsetX, points[2].Y + offsetY), true, false);
        }
        streamGeometry.Freeze();

        dc.DrawGeometry(fill, stroke, streamGeometry);
    }
}
