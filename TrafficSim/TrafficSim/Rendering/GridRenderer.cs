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
        
        for (var x = 0; x < width; x++)
        {
            for (var y = 0; y < height; y++)
            {
                grid[x, y].IsDirty = true;
            }
        }
        
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
    
    public void UpdateCellVisual(Cell cell)
    {
        if (_grid == null) return;
        
        cell.IsDirty = true;
        RenderGrid();
    }
    
    public void UpdateAllDirtyCells()
    {
        RenderGrid();
    }
    
    private void RenderGrid()
    {
        if (_grid == null) return;
        
        using var renderOpen = _gridVisual.RenderOpen();
        
        var emptyBrush = Brushes.White;
        var roadBrush = Brushes.DarkGray;
        var intersectionBrush = Brushes.Blue;
        var gridLinePen = new Pen(Brushes.LightGray, 0.3);
        var arrowBrush = Brushes.Yellow;
        var arrowPen = new Pen(Brushes.Black, 1);
        
        gridLinePen.Freeze();
        arrowPen.Freeze();
        
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
                
                renderOpen.DrawRectangle(brush, gridLinePen, rect);
                
                if (cell.Type == CellType.Road && cell.Direction != TrafficDirection.None)
                {
                    DrawArrow(renderOpen, cell, x, y, arrowBrush, arrowPen);
                }
                
                cell.IsDirty = false;
            }
        }
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
