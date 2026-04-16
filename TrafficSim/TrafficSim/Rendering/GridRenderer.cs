using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TrafficSim.Models;

namespace TrafficSim.Rendering;

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
    private readonly HashSet<(int, int)> _exitNodes = [];
    private (int x, int y)? _selectedNode;

    // Frozen pen objects to avoid new objects every render call
    private static readonly Pen GridLinePen     = MakeFrozenPen(Brushes.LightGray, 0.3);
    private static readonly Pen ArrowPen        = MakeFrozenPen(Brushes.Black, 1);
    private static readonly Pen GiveWayPen      = MakeFrozenPen(Brushes.Red, 1.5);
    private static readonly Pen SelectedNodePen = MakeFrozenPen(Brushes.Yellow, 2.0);

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
        
        // DrawingVisual the solution to remove the grid being thousands of objects
        var host = new DrawingVisualHost(_gridVisual);
        Panel.SetZIndex(host, 0);
        _canvas.Children.Add(host);
    }

    public void CreateVisuals(Cell[,] grid, int width, int height, double cellSizePixels)
    {
        _grid = grid;
        _width = width;
        _height = height;
        _cellSizePixels = cellSizePixels;
        
        _canvas.Width = width * cellSizePixels;
        _canvas.Height = height * cellSizePixels;
        
        // Pre-calculate arrow points for each direction to reuse
        PreCalculateArrowPoints(cellSizePixels);
        
        RenderGrid();
    }
    
    private void PreCalculateArrowPoints(double cellSize)
    {
        var center = cellSize / 2;
        var arrowSize = cellSize * 0.4;
        
        // North arrow
        _arrowPointsNorth[0] = new Point(center - arrowSize / 2, center + arrowSize / 2);
        _arrowPointsNorth[1] = new Point(center, center - arrowSize / 2);
        _arrowPointsNorth[2] = new Point(center + arrowSize / 2, center + arrowSize / 2);
        
        // East arrow
        _arrowPointsEast[0] = new Point(center - arrowSize / 2, center - arrowSize / 2);
        _arrowPointsEast[1] = new Point(center + arrowSize / 2, center);
        _arrowPointsEast[2] = new Point(center - arrowSize / 2, center + arrowSize / 2);
        
        // South arrow
        _arrowPointsSouth[0] = new Point(center - arrowSize / 2, center - arrowSize / 2);
        _arrowPointsSouth[1] = new Point(center, center + arrowSize / 2);
        _arrowPointsSouth[2] = new Point(center + arrowSize / 2, center - arrowSize / 2);
        
        // West arrow
        _arrowPointsWest[0] = new Point(center + arrowSize / 2, center - arrowSize / 2);
        _arrowPointsWest[1] = new Point(center - arrowSize / 2, center);
        _arrowPointsWest[2] = new Point(center + arrowSize / 2, center + arrowSize / 2);
    }

    public void RenderGrid()
    {
        if (_grid == null)
        {
            return;
        }
        
        using var renderOpen = _gridVisual.RenderOpen();
        
        var emptyBrush = Brushes.White;
        var roadBrush = Brushes.DarkGray;
        var intersectionBrush = Brushes.Blue;
        var arrowBrush = Brushes.Yellow;
        
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

                if (_trafficLightNodes.TryGetValue((x, y), out var lightPhase))
                {
                    DrawTrafficLight(renderOpen, x, y, lightPhase);
                }

                if (_spawnNodes.Contains((x, y)))
                {
                    DrawSpawnIndicator(renderOpen, x, y);
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
    }

    public void SetGiveWayNodes(IEnumerable<(int gridX, int gridY)> nodes)
    {
        _giveWayNodes.Clear();
        foreach (var pos in nodes)
        {
            _giveWayNodes.Add(pos);
        }
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
        foreach (var (gx, gy, phase) in nodes)
        {
            _trafficLightNodes[(gx, gy)] = phase;
        }
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
        foreach (var pos in nodes)
        {
            _spawnNodes.Add(pos);
        }
        RenderGrid();
    }

    public void ClearSpawnNodes()
    {
        _spawnNodes.Clear();
        RenderGrid();
    }

    public void SetExitNodes(IEnumerable<(int gridX, int gridY)> nodes)
    {
        _exitNodes.Clear();
        foreach (var pos in nodes)
        {
            _exitNodes.Add(pos);
        }
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

    private void DrawGiveWayTriangle(DrawingContext dc, int gridX, int gridY)
    {
        var offsetX = gridX * _cellSizePixels;
        var offsetY = gridY * _cellSizePixels;
        var cx = offsetX + _cellSizePixels / 2;
        var cy = offsetY + _cellSizePixels / 2;
        var half = _cellSizePixels * 0.22;
        
        var p0 = new Point(cx - half, cy - half * 0.6); // top-left
        var p1 = new Point(cx + half, cy - half * 0.6); // top-right
        var p2 = new Point(cx,        cy + half); // bottom point

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
    
    private void DrawTrafficLight(DrawingContext dc, int gridX, int gridY, TrafficLightPhase phase)
    {
        var cx = gridX * _cellSizePixels + _cellSizePixels / 2;
        var cy = gridY * _cellSizePixels + _cellSizePixels / 2;
        var radius = _cellSizePixels * 0.25;

        var brush = phase switch
        {
            TrafficLightPhase.Green => Brushes.LimeGreen,
            TrafficLightPhase.Yellow => Brushes.Gold,
            TrafficLightPhase.Red => Brushes.Red,
            _ => Brushes.Gray
        };

        dc.DrawEllipse(brush, null, new Point(cx, cy), radius, radius);
    }

    private void DrawSpawnIndicator(DrawingContext dc, int gridX, int gridY)
    {
        // Small filled green upward triangle in the top-right corner
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
        dc.DrawGeometry(Brushes.LimeGreen, null, geo);
    }

    private void DrawExitIndicator(DrawingContext dc, int gridX, int gridY)
    {
        // Small filled red downward triangle in the top-right corner
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
        dc.DrawGeometry(Brushes.Red, null, geo);
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
        
        if (points == null)
        {
            return;
        }
        
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
