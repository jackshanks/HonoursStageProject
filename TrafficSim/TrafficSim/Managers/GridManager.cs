using System.Windows.Controls;
using TrafficSim.Models;
using TrafficSim.Rendering;

namespace TrafficSim.Managers;

public class GridManager(Canvas canvas, double cellSizeMeters = 4.0)
{
    private readonly GridRenderer _renderer = new GridRenderer(canvas);
    private readonly ReaderWriterLockSlim _gridLock = new();
    private int GridWidth { get; set; }
    private int GridHeight { get; set; }
    public double CellSizeMeters { get; set; } = cellSizeMeters;
    private double CellSizePixels { get; set; }
    
    private Cell[,]? _grid;

    public double GetTotalWidthMeters()
    {
        _gridLock.EnterReadLock();
        try
        {
            return GridWidth * CellSizeMeters;
        }
        finally
        {
            _gridLock.ExitReadLock();
        }
    }
    public double GetTotalHeightMeters()
    {
        _gridLock.EnterReadLock();
        try
        {
            return GridHeight * CellSizeMeters;
        }
        finally
        {
            _gridLock.ExitReadLock();
        }
    }
    
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
    public Cell? GetCellFromWorldCoords(double worldX, double worldY)
    {
        _gridLock.EnterReadLock();
        try
        {
            var x = (int)Math.Floor(worldX / CellSizeMeters);
            var y = (int)Math.Floor(worldY / CellSizeMeters);
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

    public void SetCellType(int x, int y, CellType type)
    {
        _gridLock.EnterWriteLock();
        try
        {
            var cell = GetCell(x, y);
            if (cell == null) return;
            
            cell.SetType(type);
            _renderer.UpdateCellVisual(cell, CellSizePixels);
        }
        finally
        {
            _gridLock.ExitWriteLock();
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
            _renderer.UpdateCellVisual(cell, CellSizePixels);
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
            _renderer.UpdateCellVisual(cell, CellSizePixels);
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
                    var cell = _grid[x, y];
                    cell.SetTypeAndDirection(CellType.Empty, TrafficDirection.None);
                }
            }
            
            _renderer.UpdateAllDirtyCells(_grid, GridWidth, GridHeight, CellSizePixels);
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
    
    public bool IsValidRoad(double worldX, double worldY)
    {
        _gridLock.EnterReadLock();
        try
        {
            var x = (int)Math.Floor(worldX / CellSizeMeters);
            var y = (int)Math.Floor(worldY / CellSizeMeters);
            var cell = GetCell(x, y);
            return cell?.Type == CellType.Road && cell.Direction != TrafficDirection.None;
        }
        finally
        {
            _gridLock.ExitReadLock();
        }
    }
    
    public static string GetCellInfo(Cell? cell)
    {
        if (cell == null) return "No cell";
            
        var directionText = cell.Direction != TrafficDirection.None ? $" | Direction: {cell.Direction}" : "";
        return $"Grid: ({cell.X}, {cell.Y}) | Position: ({cell.RealWorldX:F1}m, {cell.RealWorldY:F1}m) | Type: {cell.Type}{directionText}";
    }
    
    public void Dispose()
    {
        _gridLock?.Dispose();
    }
}