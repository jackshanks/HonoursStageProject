using System.Windows.Controls;
using TrafficSim.Models;
using TrafficSim.Rendering;

namespace TrafficSim.Managers;

/// <summary>
/// Creates and manages the grid
/// </summary>
public class GridManager(Canvas canvas, double cellSizeMeters = 4.0)
{
    private readonly GridRenderer _renderer = new(canvas);
    private readonly ReaderWriterLockSlim _gridLock = new();
    
    public int GridWidth { get; private set; }
    public int GridHeight { get; private set; }
    public double CellSizeMeters { get; } = cellSizeMeters;
    public double CellSizePixels { get; private set; }
    
    private Cell[,]? _grid;
    
    /// <summary>
    /// Initialises a new blank grid
    /// </summary>
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
    /// Safely fetches a cell by grid coords
    /// </summary>
    private Cell? GetCell(int x, int y)
    {
        if (x >= 0 && x < GridWidth && y >= 0 && y < GridHeight)
        {
            return _grid?[x, y];
        }
        return null;
    }
    
    /// <summary>
    /// Converts a pixel click into a cell reference
    /// </summary>
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
    
    /// <summary>
    /// Thread-safe cell getter
    /// </summary>
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
    /// Scale factor for drawing on canvas
    /// </summary>
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
    
    /// <summary>
    /// Updates cell direction
    /// </summary>
    public void SetCellDirection(int x, int y, TrafficDirection direction)
    {
        _gridLock.EnterWriteLock();
        try
        {
            var cell = GetCell(x, y);
            if (cell == null) return;
            
            cell.SetDirection(direction);
            _renderer.RenderGrid();
        }
        finally
        {
            _gridLock.ExitWriteLock();
        }
    }
    
    /// <summary>
    /// Updates cell speed limit
    /// </summary>
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
    /// Updates cell type and direction
    /// </summary>
    public void SetCellTypeAndDirection(int x, int y, CellType type, TrafficDirection direction)
    {
        _gridLock.EnterWriteLock();
        try
        {
            var cell = GetCell(x, y);
            if (cell == null) return;
            
            cell.SetTypeAndDirection(type, direction);
            _renderer.RenderGrid();
        }
        finally
        {
            _gridLock.ExitWriteLock();
        }
    }
    
    /// <summary>
    /// Resets all cells to empty
    /// </summary>
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
    
    /// <summary>
    /// Hands give-way nodes to renderer
    /// </summary>
    public void SetGiveWayNodes(IEnumerable<(int gridX, int gridY)> nodes) => _renderer.SetGiveWayNodes(nodes);

    /// <summary>
    /// Clears visual give-way nodes
    /// </summary>
    public void ClearGiveWayNodes() => _renderer.ClearGiveWayNodes();

    /// <summary>
    /// Hands traffic light info to renderer
    /// </summary>
    public void SetTrafficLightNodes(List<(int gridX, int gridY, TrafficLightPhase phase)> nodes) => _renderer.SetTrafficLightNodes(nodes);

    /// <summary>
    /// Clears visual traffic lights
    /// </summary>
    public void ClearTrafficLightNodes() => _renderer.ClearTrafficLightNodes();

    /// <summary>
    /// Hands spawn node locations to renderer
    /// </summary>
    public void SetSpawnNodes(IEnumerable<(int gridX, int gridY)> nodes) => _renderer.SetSpawnNodes(nodes);

    /// <summary>
    /// Clears visual spawn nodes
    /// </summary>
    public void ClearSpawnNodes() => _renderer.ClearSpawnNodes();

    /// <summary>
    /// Sets visible vehicle queue text at spawn nodes
    /// </summary>
    public void SetSpawnBacklogs(IEnumerable<(int gridX, int gridY, double backlog)> nodes) => _renderer.SetSpawnBacklogs(nodes);

    /// <summary>
    /// Clears backlog text
    /// </summary>
    public void ClearSpawnBacklogs() => _renderer.ClearSpawnBacklogs();

    /// <summary>
    /// Hands exit node locations to renderer
    /// </summary>
    public void SetExitNodes(IEnumerable<(int gridX, int gridY)> nodes) => _renderer.SetExitNodes(nodes);

    /// <summary>
    /// Clears visual exit nodes
    /// </summary>
    public void ClearExitNodes() => _renderer.ClearExitNodes();

    /// <summary>
    /// Highlights user-selected node
    /// </summary>
    public void SetSelectedNode(int gridX, int gridY) => _renderer.SetSelectedNode(gridX, gridY);

    /// <summary>
    /// Clears user selection highlight
    /// </summary>
    public void ClearSelectedNode() => _renderer.ClearSelectedNode();

    /// <summary>
    /// Gets all drawn cells for JSON exporting
    /// </summary>
    public List<Cell> GetAllNonEmptyCells()
    {
        _gridLock.EnterReadLock();
        try
        {
            var cells = new List<Cell>();
            if (_grid == null) return cells;
            
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
    /// Safety check before using grid
    /// </summary>
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
    /// Formats cell details for bottom status bar
    /// </summary>
    public static string GetCellInfo(Cell? cell)
    {
        if (cell == null) return "Error: Cell not found";
            
        var directionText = cell.Direction != TrafficDirection.None ? $" | Direction: {cell.Direction}" : "";
        return $"Grid: ({cell.X}, {cell.Y}) | Position: ({cell.RealWorldX:F1}m, {cell.RealWorldY:F1}m) | Type: {cell.Type}{directionText}";
    }
    
    /// <summary>
    /// Groups touching intersection cells together into distinct junctions
    /// </summary>
    public List<List<Cell>> ComputeJunctionGroups()
    {
        _gridLock.EnterReadLock();
        try
        {
            if (_grid == null) return [];
            var visited = new HashSet<(int, int)>();
            var groups = new List<List<Cell>>();

            for (var x = 0; x < GridWidth; x++)
            {
                for (var y = 0; y < GridHeight; y++)
                {
                    if (_grid[x, y].Type != CellType.Intersection || visited.Contains((x, y))) continue;
                    
                    var group = new List<Cell>();
                    var queue = new Queue<(int, int)>();
                    queue.Enqueue((x, y));
                    visited.Add((x, y));

                    while (queue.Count > 0)
                    {
                        var (cx, cy) = queue.Dequeue();
                        group.Add(_grid[cx, cy]);
                        
                        foreach (var (nx, ny) in new[] { (cx - 1, cy), (cx + 1, cy), (cx, cy - 1), (cx, cy + 1) })
                        {
                            if (nx < 0 || nx >= GridWidth || ny < 0 || ny >= GridHeight || visited.Contains((nx, ny)) || _grid[nx, ny].Type != CellType.Intersection) continue;
                            
                            visited.Add((nx, ny));
                            queue.Enqueue((nx, ny));
                        }
                    }
                    groups.Add(group);
                }
            }
            return groups;
        }
        finally
        {
            _gridLock.ExitReadLock();
        }
    }

    /// <summary>
    /// Submits cluster centers to renderer for bounding boxes
    /// </summary>
    public void SetJunctionGroupCenters(IEnumerable<(double cx, double cy)> centers) => _renderer.SetJunctionGroupCenters(centers);

    /// <summary>
    /// Toggles rendering mode filters
    /// </summary>
    public void SetEditMode(bool isEditMode)
    {
        _renderer.IsEditMode = isEditMode;
        _renderer.RenderGrid();
    }

    /// <summary>
    /// Forces UI update
    /// </summary>
    public void RenderGrid() => _renderer.RenderGrid();

    /// <summary>
    /// Clean up lock on close
    /// </summary>
    public void Dispose() => _gridLock.Dispose();
}