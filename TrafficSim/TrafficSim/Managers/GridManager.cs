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
    
    /// <summary>
    /// Creates and draws a grid
    /// </summary>
    /// <param name="width">How many cells wide</param>
    /// <param name="height">How many cells tall</param>
    /// <param name="cellSizePixels">Pixels per cell</param>
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

    /// <summary>
    /// Gets a cell from the grid
    /// </summary>
    /// <param name="x">x coord of the cell</param>
    /// <param name="y">y coord of the cell</param>
    /// <returns></returns>
    private Cell? GetCell(int x, int y)
    {
        if (x >= 0 && x < GridWidth && y >= 0 && y < GridHeight)
        {
            return _grid?[x, y];
        }
        return null;
    }
    
    /// <summary>
    /// Gets a cell from a pixel position, used for mouse actions
    /// </summary>
    /// <param name="pixelX">xThe pixels x value</param>
    /// <param name="pixelY">The pixels y value</param>
    /// <returns></returns>
    public Cell? GetCellFromPixel(double pixelX, double pixelY)
    {
        _gridLock.EnterReadLock();
        try
        {
            // Finds the matching cells by dividing by each cell's size and using floor (in case of negative coords)
            var x = (int)Math.Floor(pixelX / CellSizePixels);
            var y = (int)Math.Floor(pixelY / CellSizePixels);
            return GetCell(x, y);
        }
        finally
        {
            _gridLock.ExitReadLock();
        }
    }
    
    /// <summary>
    /// Gets a cell from a grid position
    /// </summary>
    /// <param name="x">x coord of the cell</param>
    /// <param name="y">y coord of the cell</param>
    /// <returns></returns>
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
    
    /// <summary>
    /// Gets the number of pixels per meter
    /// </summary>
    /// <returns></returns>
    public double GetPixelsPerMeter()
    {
        _gridLock.EnterReadLock();
        try
        {
            if (CellSizeMeters <= 0)
            {
                return 1;
            }
            return CellSizePixels / CellSizeMeters;
        }
        finally
        {
            _gridLock.ExitReadLock();
        }
    }
    
    /// <summary>
    /// Sets the cell direction
    /// </summary>
    /// <param name="x">x coord of the cell</param>
    /// <param name="y">y coord of the cell</param>
    /// <param name="direction">Direction using TrafficDirection Enum</param>
    public void SetCellDirection(int x, int y, TrafficDirection direction)
    {
        _gridLock.EnterWriteLock();
        try
        {
            var cell = GetCell(x, y);
            if (cell == null)
            {
                return;
            }
            cell.SetDirection(direction);
            _renderer.RenderGrid();
        }
        finally
        {
            _gridLock.ExitWriteLock();
        }
    }
    
    /// <summary>
    /// Sets the cell speed limit
    /// </summary>
    /// <param name="x">x coord of the cell</param>
    /// <param name="y">y coord of the cell</param>
    /// <param name="speedLimitMph">Speed limit in Mph</param>
    public void SetCellSpeedLimit(int x, int y, int speedLimitMph)
    {
        _gridLock.EnterWriteLock();
        try
        {
            var cell = GetCell(x, y);
            cell?.SetSpeedLimit(speedLimitMph);
        }
        finally
        {
            _gridLock.ExitWriteLock();
        }
    }

    /// <summary>
    /// Sets the cell's type and direction
    /// </summary>
    /// <param name="x">x coord of the cell</param>
    /// <param name="y">y coord of the cell</param>
    /// <param name="type">Type of cell using CellType Enum (I.E. junction/road)</param>
    /// <param name="direction">Direction using TrafficDirection Enum</param>
    public void SetCellTypeAndDirection(int x, int y, CellType type, TrafficDirection direction)
    {
        _gridLock.EnterWriteLock();
        try
        {
            var cell = GetCell(x, y);
            if (cell == null)
            {
                return;
            }
            cell.SetTypeAndDirection(type, direction);
            _renderer.RenderGrid();
        }
        finally
        {
            _gridLock.ExitWriteLock();
        }
    }
    
    /// <summary>
    /// Clears direction and type of all cells
    /// </summary>
    public void ClearAllCells()
    {
        _gridLock.EnterWriteLock();
        try
        {
            if (_grid == null)
            {
                return;
            }

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
    
    /// <summary>
    /// Sets all give way nodes in one (at build) to hand to the render
    /// </summary>
    /// <param name="nodes">List of nodes marked as give-way</param>
    public void SetGiveWayNodes(IEnumerable<(int gridX, int gridY)> nodes)
    {
        _renderer.SetGiveWayNodes(nodes);
    }

    /// <summary>
    /// Clears all give-way nodes
    /// </summary>
    public void ClearGiveWayNodes()
    {
        _renderer.ClearGiveWayNodes();
    }

    /// <summary>
    /// Sets traffic light node positions and their current phase
    /// </summary>
    public void SetTrafficLightNodes(List<(int gridX, int gridY, TrafficLightPhase phase)> nodes)
    {
        _renderer.SetTrafficLightNodes(nodes);
    }

    /// <summary>
    /// Clears all traffic light nodes
    /// </summary>
    public void ClearTrafficLightNodes()
    {
        _renderer.ClearTrafficLightNodes();
    }

    /// <summary>
    /// Sets spawn node indicator positions
    /// </summary>
    public void SetSpawnNodes(IEnumerable<(int gridX, int gridY)> nodes)
    {
        _renderer.SetSpawnNodes(nodes);
    }

    /// <summary>
    /// Clears all spawn node indicators
    /// </summary>
    public void ClearSpawnNodes()
    {
        _renderer.ClearSpawnNodes();
    }

    /// <summary>
    /// Sets exit node indicator positions
    /// </summary>
    public void SetExitNodes(IEnumerable<(int gridX, int gridY)> nodes)
    {
        _renderer.SetExitNodes(nodes);
    }

    /// <summary>
    /// Clears all exit node indicators
    /// </summary>
    public void ClearExitNodes()
    {
        _renderer.ClearExitNodes();
    }

    /// <summary>
    /// Highlights the node at the given grid position as selected
    /// </summary>
    public void SetSelectedNode(int gridX, int gridY)
    {
        _renderer.SetSelectedNode(gridX, gridY);
    }

    /// <summary>
    /// Removes the selected node highlight
    /// </summary>
    public void ClearSelectedNode()
    {
        _renderer.ClearSelectedNode();
    }

    /// <summary>
    /// Returns all non-empty cells for JSON
    /// </summary>
    public List<Cell> GetAllNonEmptyCells()
    {
        _gridLock.EnterReadLock();
        try
        {
            var cells = new List<Cell>();
            if (_grid == null)
            {
                return cells;
            }
            for (var x = 0; x < GridWidth; x++)
            {
                for (var y = 0; y < GridHeight; y++)
                {
                    if (_grid[x, y].Type != CellType.Empty)
                    {
                        cells.Add(_grid[x, y]);
                    }
                }
            }
            return cells;
        }
        finally
        {
            _gridLock.ExitReadLock();
        }
    }

    /// <summary>
    /// Checks if the grid has been created (Edge case as grid should be made on program run)
    /// </summary>
    /// <returns></returns>
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
    
    /// <summary>
    /// Gets all info of a cell to be displayed
    /// </summary>
    /// <param name="cell">Cell to get info about</param>
    /// <returns></returns>
    public static string GetCellInfo(Cell? cell)
    {
        if (cell == null)
        {
            return "Error: Cell not found";
        }
            
        var directionText = cell.Direction != TrafficDirection.None ? $" | Direction: {cell.Direction}" : "";
        return $"Grid: ({cell.X}, {cell.Y}) | Position: ({cell.RealWorldX:F1}m, {cell.RealWorldY:F1}m) | Type: {cell.Type}{directionText}";
    }
    
    /// <summary>
    /// gridLock requires a Dispose to it frees the object
    /// </summary>
    public void Dispose()
    {
        _gridLock.Dispose();
    }
}