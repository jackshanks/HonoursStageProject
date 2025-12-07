using System.Windows.Controls;
using TrafficSim.Models;
using TrafficSim.Rendering;

namespace TrafficSim.Managers;

public class GridManager(Canvas canvas, double cellSizeMeters = 4.0)
{
    private readonly GridRenderer _renderer = new GridRenderer(canvas);
    private int GridWidth { get; set; }
    private int GridHeight { get; set; }
    public double CellSizeMeters { get; set; } = cellSizeMeters;
    private double CellSizePixels { get; set; }
    
    private Cell[,]? _grid;

    public double GetTotalWidthMeters() => GridWidth * CellSizeMeters;
    public double GetTotalHeightMeters() => GridHeight * CellSizeMeters;
    
    public void CreateGrid(int width, int height, double cellSizePixels)
    {
        GridWidth = width;
        GridHeight = height;
        CellSizePixels = cellSizePixels;
        
        _grid = new Cell[width, height];
        
        for (var x = 0; x < width; x++)
        {
            for (var y = 0; y < height; y++)
            {
                _grid[x, y] = new Cell(x, y, CellSizeMeters);
            }
        }
        
        _renderer.CreateVisuals(_grid, width, height, cellSizePixels);
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
    
    public Cell? GetCellFromWorldCoords(double worldX, double worldY)
    {
        var x = (int)(worldX / CellSizeMeters);
        var y = (int)(worldY / CellSizeMeters);
        return GetCell(x, y);
    }
    
    public double GetPixelsPerMeter()
    {
        if (CellSizeMeters <= 0) return 1; 
        return CellSizePixels / CellSizeMeters;
    }
    
    public void SetCellType(int x, int y, CellType type)
    {
        var cell = GetCell(x, y);
        if (cell == null) return;
        
        cell.SetType(type);
        _renderer.UpdateCellVisual(cell, CellSizePixels);
    }
    
    public void SetCellDirection(int x, int y, TrafficDirection direction)
    {
        var cell = GetCell(x, y);
        if (cell == null) return;
        
        cell.SetDirection(direction);
        _renderer.UpdateCellVisual(cell, CellSizePixels);
    }
    
    public void SetCellTypeAndDirection(int x, int y, CellType type, TrafficDirection direction)
    {
        var cell = GetCell(x, y);
        if (cell == null) return;
        
        cell.SetTypeAndDirection(type, direction);
        _renderer.UpdateCellVisual(cell, CellSizePixels);
    }
    
    public void ClearAllCells()
    {
        if (_grid == null) return;

        for (var x = 0; x < GridWidth; x++)
        {
            for (var y = 0; y < GridHeight; y++)
            {
                var cell = _grid[x, y];
                cell.SetTypeAndDirection(CellType.Empty, TrafficDirection.None);
            }
        }
        
        _renderer.UpdateAllDirtyCells(_grid, GridWidth, GridHeight, CellSizePixels);
    }
    
    public bool HasGrid()
    {
        return _grid != null;
    }
    
    public bool IsValidRoad(double worldX, double worldY)
    {
        var cell = GetCellFromWorldCoords(worldX, worldY);
        return cell?.Type == CellType.Road && cell.Direction != TrafficDirection.None;
    }
    
    public static string GetCellInfo(Cell? cell)
    {
        if (cell == null) return "No cell";
            
        var directionText = cell.Direction != TrafficDirection.None ? $" | Direction: {cell.Direction}" : "";
        return $"Grid: ({cell.X}, {cell.Y}) | Position: ({cell.RealWorldX:F1}m, {cell.RealWorldY:F1}m) | Type: {cell.Type}{directionText}";
    }
}