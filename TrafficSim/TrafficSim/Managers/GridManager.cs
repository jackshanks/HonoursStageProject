using System.Windows.Controls;
using TrafficSim.Models;
using TrafficSim.Rendering;

namespace TrafficSim.Managers;

/// <summary>
/// Creates and manages the grid
/// </summary>
/// <param name="canvas">UI Element</param>
/// <param name="cellSizeMeters">Assigns each cell to a meter length for real-world comparisons</param>
public class GridManager(Canvas canvas, double cellSizeMeters = 4.0)
{
    private readonly GridRenderer _renderer = new(canvas);
    private readonly ReaderWriterLockSlim _gridLock = new();
    public int GridWidth { get; private set; }
    public int GridHeight { get; private set; }
    public double CellSizeMeters { get; } = cellSizeMeters;
    private double CellSizePixels { get; set; }
    
    private Cell[,]? _grid;
    
    public void CreateGrid(int width, int height, double cellSizePixels)
    {
        _gridLock.EnterWriteLock();
        try
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
        finally
        {
            _gridLock.ExitWriteLock();
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
        _gridLock.EnterReadLock();
        try
        {
            var x = (int)Math.Floor(pixelX / CellSizePixels);
            var y = (int)Math.Floor(pixelY / CellSizePixels);
            return GetCell(x, y);
        }
        finally
        {
            _gridLock.ExitReadLock();
        }
    }
    
    public Cell? GetCellFromGridCoords(int x, int y)
    {
        _gridLock.EnterReadLock();
        try
        {
            return GetCell(x, y);
        }
        finally
        {
            _gridLock.ExitReadLock();
        }
    }
    
    public double GetPixelsPerMeter()
    {
        _gridLock.EnterReadLock();
        try
        {
            if (CellSizeMeters <= 0) return 1;
            return CellSizePixels / CellSizeMeters;
        }
        finally
        {
            _gridLock.ExitReadLock();
        }
    }
    
    public void SetCellDirection(int x, int y, TrafficDirection direction)
    {
        _gridLock.EnterWriteLock();
        try
        {
            var cell = GetCell(x, y);
            if (cell == null) return;
            
            cell.SetDirection(direction);
            _renderer.UpdateCellVisual(cell);
        }
        finally
        {
            _gridLock.ExitWriteLock();
        }
    }
    
    public void SetCellTypeAndDirection(int x, int y, CellType type, TrafficDirection direction)
    {
        _gridLock.EnterWriteLock();
        try
        {
            var cell = GetCell(x, y);
            if (cell == null) return;
            
            cell.SetTypeAndDirection(type, direction);
            _renderer.UpdateCellVisual(cell);
        }
        finally
        {
            _gridLock.ExitWriteLock();
        }
    }
    
    public void ClearAllCells()
    {
        _gridLock.EnterWriteLock();
        try
        {
            if (_grid == null) return;

            for (var x = 0; x < GridWidth; x++)
            {
                for (var y = 0; y < GridHeight; y++)
                {
                    _grid[x, y].SetTypeAndDirection(CellType.Empty, TrafficDirection.None);
                }
            }
            _renderer.RenderGrid();
        }
        finally
        {
            _gridLock.ExitWriteLock();
        }
    }
    
    public bool HasGrid()
    {
        _gridLock.EnterReadLock();
        try
        {
            return _grid != null;
        }
        finally
        {
            _gridLock.ExitReadLock();
        }
    }
    
    public static string GetCellInfo(Cell? cell)
    {
        if (cell == null) return "Error: Cell not found";
            
        var directionText = cell.Direction != TrafficDirection.None ? $" | Direction: {cell.Direction}" : "";
        return $"Grid: ({cell.X}, {cell.Y}) | Position: ({cell.RealWorldX:F1}m, {cell.RealWorldY:F1}m) | Type: {cell.Type}{directionText}";
    }
    
    public void Dispose()
    {
        _gridLock.Dispose();
    }
}