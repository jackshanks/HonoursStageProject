using System.Windows.Controls;
using TrafficSim.Models;

namespace TrafficSim.Managers;

public class GridManager(Canvas canvas, double cellSizeMeters = 4.0)
{
    private int GridWidth { get; set; }
    private int GridHeight { get; set; }
    private double CellSizeMeters { get; set; } = cellSizeMeters;
    private double CellSizePixels { get; set; }
        
    private Cell[,]? _grid;
    
    public void CreateGrid(int width, int height, double cellSizePixels)
    {
        GridWidth = width;
        GridHeight = height;
        CellSizePixels = cellSizePixels;

        // Initialize the grid array
        _grid = new Cell[width, height];

        // Clear the canvas
        canvas.Children.Clear();

        // Set canvas size
        canvas.Width = width * cellSizePixels;
        canvas.Height = height * cellSizePixels;

        // Create grid cells
        for (var x = 0; x < width; x++)
        {
            for (var y = 0; y < height; y++)
            {
                var cell = new Cell(x, y, CellSizeMeters);
                cell.UpdateVisual(cellSizePixels);
                    
                Canvas.SetLeft(cell.VisualElement, x * cellSizePixels);
                Canvas.SetTop(cell.VisualElement, y * cellSizePixels);
                    
                canvas.Children.Add(cell.VisualElement);
                    
                // Add arrow element
                Canvas.SetLeft(cell.ArrowElement, x * cellSizePixels);
                Canvas.SetTop(cell.ArrowElement, y * cellSizePixels);
                canvas.Children.Add(cell.ArrowElement);
                    
                _grid[x, y] = cell;
            }
        }
    }

    private Cell? GetCell(int x, int y)
    {
        if (x >= 0 && x < GridWidth && y >= 0 && y < GridHeight)
        {
            return _grid?[x, y];
        }
        return null;
    }
    
    public Cell? GetCellFromPixel(double pixelX, double pixelY)
    {
        var x = (int)(pixelX / CellSizePixels);
        var y = (int)(pixelY / CellSizePixels);
        return GetCell(x, y);
    }
    
    public void SetCellType(int x, int y, CellType type)
    {
        var cell = GetCell(x, y);
        if (cell == null) return;
        
        cell.Type = type;
        cell.UpdateVisual(CellSizePixels);
    }
    
    public void SetCellDirection(int x, int y, TrafficDirection direction)
    {
        var cell = GetCell(x, y);
        if (cell == null) return;
        
        cell.Direction = direction;
        cell.UpdateVisual(CellSizePixels);
    }
    
    public void SetCellTypeAndDirection(int x, int y, CellType type, TrafficDirection direction)
    {
        var cell = GetCell(x, y);
        if (cell == null) return;
        
        cell.Type = type;
        cell.Direction = direction;
        cell.UpdateVisual(CellSizePixels);
    }
    
    public void ClearAllCells()
    {
        if (_grid == null) return;

        for (var x = 0; x < GridWidth; x++)
        {
            for (var y = 0; y < GridHeight; y++)
            {
                SetCellTypeAndDirection(x, y, CellType.Empty, TrafficDirection.None);
            }
        }
    }
    
    public bool HasGrid()
    {
        return _grid != null;
    }
    
    public static string GetCellInfo(Cell? cell)
    {
        if (cell == null) return "No cell";
            
        var directionText = cell.Direction != TrafficDirection.None ? $" | Direction: {cell.Direction}" : "";
        return $"Grid: ({cell.X}, {cell.Y}) | Position: ({cell.RealWorldX:F1}m, {cell.RealWorldY:F1}m) | Type: {cell.Type}{directionText}";
    }
}