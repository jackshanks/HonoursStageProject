using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using TrafficSim.Managers;
using TrafficSim.Models;
using TrafficSim.Rendering;

namespace TrafficSim;

/// <summary>
/// Interaction logic for physics and UI. Uses threading to try to increase performance with large numbers of cars
/// </summary>
public partial class MainWindow
{
    private readonly GridManager _gridManager;
    private readonly TrafficManager _trafficManager;
    private readonly CarRenderer _carRenderer;
    private TimeSpan _lastRenderTime = TimeSpan.MinValue;
    
    private Task? _physicsTask;
    private CancellationTokenSource? _physicsCancellationToken;
    private readonly Lock _physicsLock = new();

    // Fixed time-step physics config for pure logic
    private const double FixedTimeStep = 1.0 / 60.0;
    private const double MaxAccumulatedTime = 0.1; // Cap to amount of time passage to prevent spiral
    private double _accumulatedTime;

    private bool _isDrawing;
    private Cell? _lastDrawnCell;
    private bool _isErasing;
    private Cell? _lastErasedCell;

    private Cell? _selectedJunctionCell;
    private bool _updatingGiveWayCheckboxes;
    
    private double _simulationSpeed = 1.0;
    private bool _collisionsEnabled;
    
    private bool _isNetworkBuilt;
    private readonly List<CarRenderData> _renderBuffer = new();
    private bool _isSimulationRunning;
    private bool _isClosing;
    private bool _closeReady;

    // FPS tracking
    private readonly Stopwatch _fpsStopwatch = Stopwatch.StartNew();
    private int _frameCount;
    private double _fps;

    public MainWindow(GridData? initialGrid = null)
    {
        InitializeComponent();
        _gridManager = new GridManager(GridCanvas);
        _trafficManager = new TrafficManager(_gridManager);
        _carRenderer = new CarRenderer(GridCanvas);

        try
        {
            if (initialGrid != null)
            {
                TxtGridWidth.Text = initialGrid.GridWidth.ToString();
                TxtGridHeight.Text = initialGrid.GridHeight.ToString();
                var cellSizePixels = double.Parse(TxtCellSize.Text);
                GridSerialiser.ApplyToGrid(initialGrid, _gridManager, cellSizePixels);
            }
            else
            {
                ParseAndCreateGrid();
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error creating grid: {ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        
        _collisionsEnabled = ChkCollisions.IsChecked == true;

        ChkCollisions.Checked += (_, _) => { lock (_physicsLock) { _collisionsEnabled = true; } };
        ChkCollisions.Unchecked += (_, _) => { lock (_physicsLock) { _collisionsEnabled = false; } };

        ChkGiveWayNorth.Checked   += GiveWayCheckbox_Changed;
        ChkGiveWayNorth.Unchecked += GiveWayCheckbox_Changed;
        ChkGiveWayEast.Checked    += GiveWayCheckbox_Changed;
        ChkGiveWayEast.Unchecked  += GiveWayCheckbox_Changed;
        ChkGiveWaySouth.Checked   += GiveWayCheckbox_Changed;
        ChkGiveWaySouth.Unchecked += GiveWayCheckbox_Changed;
        ChkGiveWayWest.Checked    += GiveWayCheckbox_Changed;
        ChkGiveWayWest.Unchecked  += GiveWayCheckbox_Changed;

        CompositionTarget.Rendering += RenderLoop;

        SliderSimSpeed.ValueChanged += (_, _) =>
        {
            lock (_physicsLock)
            {
                _simulationSpeed = SliderSimSpeed.Value;
            }
            TxtSimSpeed.Text = $"{SliderSimSpeed.Value:F1}x";
        };
        
        UpdateUiState();
    }
    
    private void UpdateUiState()
    {
        BtnBuildNetwork.IsEnabled = !_isSimulationRunning;
        BtnCreateGrid.IsEnabled = !_isSimulationRunning;
        BtnClear.IsEnabled = !_isSimulationRunning;
        BtnLoad.IsEnabled = !_isSimulationRunning;

        SimRunControlsPanel.Visibility = _isNetworkBuilt ? Visibility.Visible : Visibility.Collapsed;
        BtnStartSim.IsEnabled = !_isSimulationRunning;
        BtnStopSim.IsEnabled = _isSimulationRunning;

        if (_isSimulationRunning)
        {
            StatusText.Text = "Simulation running. Right-click to spawn cars.";
        }
        else if (_isNetworkBuilt)
        {
            StatusText.Text = "Network built. Click ▶ Start to begin, or return to editing.";
        }
        else
        {
            StatusText.Text = "Draw roads with left-click, then build network to start.";
        }
    }
    
    private void StartPhysicsThread()
    {
        _physicsCancellationToken = new CancellationTokenSource();
        var token = _physicsCancellationToken.Token;

        _physicsTask = Task.Run(async () =>
        {
            var physicsStopwatch = Stopwatch.StartNew();
            var lastPhysicsTicks = physicsStopwatch.ElapsedTicks;
            var ticksPerFrame = (long)(Stopwatch.Frequency * FixedTimeStep);

            while (!token.IsCancellationRequested)
            {
                try
                {
                    var frameStart = physicsStopwatch.ElapsedTicks;
                    var elapsedTicks = frameStart - lastPhysicsTicks;
                    lastPhysicsTicks = frameStart;

                    var deltaTime = (double)elapsedTicks / Stopwatch.Frequency;

                    lock (_physicsLock)
                    {
                        var collisionsEnabled = _collisionsEnabled;
                        var adjustedDeltaTime = deltaTime * _simulationSpeed;
                        _accumulatedTime += adjustedDeltaTime;

                        if (_accumulatedTime > MaxAccumulatedTime)
                        {
                            _accumulatedTime = MaxAccumulatedTime;
                        }

                        while (_accumulatedTime >= FixedTimeStep)
                        {
                            _trafficManager.UpdatePhysics(FixedTimeStep, collisionsEnabled);
                            _accumulatedTime -= FixedTimeStep;
                        }
                    }

                    // Sleep for the remainder of the 16.67 ms frame budget so the thread
                    // yields the CPU without over-sleeping (Task.Delay(1) sleeps ~15 ms on Windows).
                    var elapsed = physicsStopwatch.ElapsedTicks - frameStart;
                    var remaining = ticksPerFrame - elapsed;
                    if (remaining > 0)
                    {
                        var remainingMs = (int)(remaining * 1000 / Stopwatch.Frequency);
                        if (remainingMs > 1)
                        {
                            await Task.Delay(remainingMs, token);
                        }
                        else
                        {
                            await Task.Yield();
                        }
                    }
                    else
                    {
                        await Task.Yield();
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Physics thread error: {ex.Message}");
                }
            }
        }, token);
    }
    
    private async Task StopPhysicsThreadAsync()
    {
        _physicsCancellationToken?.Cancel();

        if (_physicsTask != null)
        { 
            await _physicsTask;
        }

        _physicsCancellationToken?.Dispose();
    }
    
    private void RenderLoop(object? sender, EventArgs e)
    {
        if (e is RenderingEventArgs args)
        {
            if (args.RenderingTime == _lastRenderTime)
            {
                return;
            }
            _lastRenderTime = args.RenderingTime;
        }

        var pixelsPerMeter = _gridManager.GetPixelsPerMeter();

        _trafficManager.GetRenderData(_renderBuffer);
        _carRenderer.UpdateAllCarVisuals(_renderBuffer, pixelsPerMeter);
        CarCountText.Text = _renderBuffer.Count.ToString();

        if (_isNetworkBuilt)
        {
            NetworkInfoText.Text = _trafficManager.GetNetworkInfo();
        }

        _frameCount++;
        var elapsed = _fpsStopwatch.Elapsed.TotalSeconds;
        if (elapsed >= 0.5)
        {
            _fps = _frameCount / elapsed;
            _frameCount = 0;
            _fpsStopwatch.Restart();
            FpsText.Text = $"FPS: {_fps:F0}";
        }
    }

    private void ParseAndCreateGrid()
    {
        var width = int.Parse(TxtGridWidth.Text);
        var height = int.Parse(TxtGridHeight.Text);
        var cellSize = double.Parse(TxtCellSize.Text);

        if (width <= 0 || height <= 0 || cellSize <= 0)
        {
            throw new ArgumentException("Please enter valid positive numbers.");
        }

        _gridManager.CreateGrid(width, height, cellSize);
    }
    
    private void BtnCreateGrid_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            ParseAndCreateGrid();
            _trafficManager.ClearNetwork();
            _trafficManager.ClearTraffic();
            _carRenderer.ClearAllVisuals();
            ClearJunctionSelection();

            _isNetworkBuilt = false;
            NetworkInfoText.Text = "Network not built";
            
            UpdateUiState();
        }
        catch (ArgumentException ex)
        {
            MessageBox.Show(ex.Message, "Invalid Input",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error creating grid: {ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void BtnBuildNetwork_Click(object sender, RoutedEventArgs e)
    {
        if (!_gridManager.HasGrid())
        {
            MessageBox.Show("Please create a grid first.", "No Grid",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        
        try
        {
            var success = BuildNetworkFromGrid(_gridManager.GridWidth, _gridManager.GridHeight);

            if (success)
            {
                _isNetworkBuilt = true;
                MessageBox.Show("Lane network built successfully!", "Success",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show("Warning: Network built but may have disconnected nodes.", "Warning",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                _isNetworkBuilt = true;
            }

            _gridManager.SetGiveWayNodes(_trafficManager.GetGiveWayNodePositions());
            UpdateUiState();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error building network: {ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
    
    private bool BuildNetworkFromGrid(int width, int height)
    {
        var grid = new Cell[width, height];
        
        for (var x = 0; x < width; x++)
        {
            for (var y = 0; y < height; y++)
            {
                var cell = _gridManager.GetCellFromGridCoords(x, y);
                if (cell != null)
                {
                    grid[x, y] = cell;
                }
            }
        }
        
        return _trafficManager.BuildNetwork(grid, width, height, _gridManager.CellSizeMeters);
    }
    
    private void BtnStartSim_Click(object sender, RoutedEventArgs e)
    {
        if (!_isNetworkBuilt)
        {
            MessageBox.Show("Please build the network first.", "Network Not Built",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        
        _isSimulationRunning = true;
        StartPhysicsThread();
        UpdateUiState();
    }
    
    private async void BtnStopSim_Click(object sender, RoutedEventArgs e)
    {
        _isSimulationRunning = false;
        UpdateUiState();
        await StopPhysicsThreadAsync();
    }

    private async void BtnReturnToEditing_Click(object sender, RoutedEventArgs e)
    {
        if (_isSimulationRunning)
        {
            _isSimulationRunning = false;
            await StopPhysicsThreadAsync();
            _trafficManager.ClearTraffic();
            _carRenderer.ClearAllVisuals();
        }

        _isNetworkBuilt = false;
        _trafficManager.ClearNetwork();
        _gridManager.ClearGiveWayNodes();
        NetworkInfoText.Text = "Network not built";
        UpdateUiState();
    }
    
    private void BtnClear_Click(object sender, RoutedEventArgs e)
    {
        if (!_gridManager.HasGrid())
        {
            return;
        }

        _gridManager.ClearAllCells();
        _gridManager.ClearGiveWayNodes();
        _trafficManager.ClearNetwork();
        _trafficManager.ClearTraffic();
        _carRenderer.ClearAllVisuals();
        ClearJunctionSelection();

        _isNetworkBuilt = false;
        NetworkInfoText.Text = "Network not built";

        UpdateUiState();
    }

    private void GridCanvas_RightClick(object sender, MouseButtonEventArgs e)
    {
        if (_isSimulationRunning && _isNetworkBuilt)
        {
            var pos = e.GetPosition(GridCanvas);
            var success = _trafficManager.SpawnCarAt(pos.X, pos.Y);
            if (!success)
            {
                StatusText.Text = "Cannot spawn car here. Click on a road with traffic flow.";
            }
            return;
        }

        if (!_gridManager.HasGrid() || _isSimulationRunning)
        {
            return;
        }

        _isErasing = true;
        _lastErasedCell = null;
        EraseAtPosition(e.GetPosition(GridCanvas));
    }

    private void GridCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!_gridManager.HasGrid() || _isSimulationRunning)
        {
            return;
        }

        _isDrawing = true;
        _lastDrawnCell = null;
        DrawRoadAtPosition(e.GetPosition(GridCanvas));
    }

    private void GridCanvas_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_gridManager.HasGrid())
        {
            return;
        }

        var position = e.GetPosition(GridCanvas);
        var cell = _gridManager.GetCellFromPixel(position.X, position.Y);

        if (cell != null && !_isSimulationRunning)
        {
            StatusText.Text = GridManager.GetCellInfo(cell);
        }

        if (_isDrawing && e.LeftButton == MouseButtonState.Pressed && !_isSimulationRunning && cell != _lastDrawnCell)
        {
            DrawRoadAtPosition(position);
            _lastDrawnCell = cell;
        }

        if (!_isErasing || e.RightButton != MouseButtonState.Pressed || _isSimulationRunning || cell == _lastErasedCell) return;
        EraseAtPosition(position);
        _lastErasedCell = cell;
    }

    private void GridCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _isDrawing = false;
        _lastDrawnCell = null;
    }

    private void GridCanvas_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        _isErasing = false;
        _lastErasedCell = null;
    }

    private void DrawRoadAtPosition(Point position)
    {
        if (_isSimulationRunning)
        {
            return;
        }

        var cell = _gridManager.GetCellFromPixel(position.X, position.Y);
        if (cell == null)
        {
            return;
        }

        if (GetSelectedCellType() == CellType.Intersection)
        {
            if (cell.Type != CellType.Intersection)
            {
                _gridManager.SetCellTypeAndDirection(cell.X, cell.Y, CellType.Intersection, TrafficDirection.None);
            }
            SelectJunctionCell(cell);
            return;
        }

        var selectedDirection = GetSelectedDirection();

        var speedLimit = GetSelectedSpeedLimit();

        switch (cell.Type)
        {
            case CellType.Empty or CellType.Intersection:
                _gridManager.SetCellTypeAndDirection(cell.X, cell.Y, CellType.Road, selectedDirection);
                _gridManager.SetCellSpeedLimit(cell.X, cell.Y, speedLimit);
                break;
            case CellType.Road:
                _gridManager.SetCellDirection(cell.X, cell.Y, selectedDirection);
                _gridManager.SetCellSpeedLimit(cell.X, cell.Y, speedLimit);
                break;
        }
    }

    private CellType GetSelectedCellType()
    {
        return RbIntersection.IsChecked == true ? CellType.Intersection : CellType.Road;
    }

    private int GetSelectedSpeedLimit()
    {
        foreach (var rb in new[] { RbSpeed20, RbSpeed30, RbSpeed40, RbSpeed50, RbSpeed60, RbSpeed70 })
        {
            if (rb.IsChecked == true && rb.Tag is string tag && int.TryParse(tag, out var mph))
            {
                return mph;
            }
        }
        return 30;
    }

    private void EraseAtPosition(Point position)
    {
        if (_isSimulationRunning)
        {
            return;
        }

        var cell = _gridManager.GetCellFromPixel(position.X, position.Y);

        if (cell?.Type is CellType.Road or CellType.Intersection)
        {
            if (cell == _selectedJunctionCell)
                ClearJunctionSelection();
            _gridManager.SetCellTypeAndDirection(cell.X, cell.Y, CellType.Empty, TrafficDirection.None);
        }
    }

    private TrafficDirection GetSelectedDirection()
    {
        if (RbNorth.IsChecked == true)
        {
            return TrafficDirection.North;
        }
        if (RbEast.IsChecked == true)
        {
            return TrafficDirection.East;
        }
        if (RbSouth.IsChecked == true)
        {
            return TrafficDirection.South;
        }
        return RbWest.IsChecked == true ? TrafficDirection.West : TrafficDirection.East;
    }

    private void SelectJunctionCell(Cell cell)
    {
        _selectedJunctionCell = cell;
        TxtSelectedJunction.Text = $"({cell.X}, {cell.Y})";

        _updatingGiveWayCheckboxes = true;
        ChkGiveWayNorth.IsChecked = cell.GiveWayDirections.Contains(TrafficDirection.North);
        ChkGiveWayEast.IsChecked  = cell.GiveWayDirections.Contains(TrafficDirection.East);
        ChkGiveWaySouth.IsChecked = cell.GiveWayDirections.Contains(TrafficDirection.South);
        ChkGiveWayWest.IsChecked  = cell.GiveWayDirections.Contains(TrafficDirection.West);
        _updatingGiveWayCheckboxes = false;
    }

    private void GiveWayCheckbox_Changed(object sender, RoutedEventArgs e)
    {
        if (_updatingGiveWayCheckboxes || _selectedJunctionCell == null)
        {
            return;
        }
        UpdateGiveWayDirection(TrafficDirection.North, ChkGiveWayNorth.IsChecked == true);
        UpdateGiveWayDirection(TrafficDirection.East,  ChkGiveWayEast.IsChecked  == true);
        UpdateGiveWayDirection(TrafficDirection.South, ChkGiveWaySouth.IsChecked == true);
        UpdateGiveWayDirection(TrafficDirection.West,  ChkGiveWayWest.IsChecked  == true);
    }

    private void UpdateGiveWayDirection(TrafficDirection dir, bool active)
    {
        if (_selectedJunctionCell == null)
        {
            return;
        }
        if (active)
        {
            _selectedJunctionCell.GiveWayDirections.Add(dir);
        }
        else
        {
            _selectedJunctionCell.GiveWayDirections.Remove(dir);
        }
    }

    private void ClearJunctionSelection()
    {
        _selectedJunctionCell = null;
        TxtSelectedJunction.Text = "(none — click a junction)";
        _updatingGiveWayCheckboxes = true;
        ChkGiveWayNorth.IsChecked = false;
        ChkGiveWayEast.IsChecked  = false;
        ChkGiveWaySouth.IsChecked = false;
        ChkGiveWayWest.IsChecked  = false;
        _updatingGiveWayCheckboxes = false;
    }
    
    private void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        if (!_gridManager.HasGrid()) return;

        var dialog = new SaveFileDialog
        {
            Filter = "JSON files (*.json)|*.json",
            DefaultExt = ".json",
            Title = "Save Road Network"
        };

        if (dialog.ShowDialog() != true) return;

        try
        {
            var data = GridSerialiser.ExtractGridData(_gridManager);
            GridSerialiser.SaveToFile(data, dialog.FileName);
            StatusText.Text = $"Network saved to {System.IO.Path.GetFileName(dialog.FileName)}";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error saving network: {ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void BtnLoad_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "JSON files (*.json)|*.json",
            Title = "Load Road Network"
        };

        if (dialog.ShowDialog() != true) return;

        try
        {
            if (_isSimulationRunning)
            {
                _isSimulationRunning = false;
                await StopPhysicsThreadAsync();
            }

            var data = GridSerialiser.LoadFromFile(dialog.FileName);
            TxtGridWidth.Text = data.GridWidth.ToString();
            TxtGridHeight.Text = data.GridHeight.ToString();
            var cellSizePixels = double.Parse(TxtCellSize.Text);
            GridSerialiser.ApplyToGrid(data, _gridManager, cellSizePixels);

            _trafficManager.ClearNetwork();
            _trafficManager.ClearTraffic();
            _carRenderer.ClearAllVisuals();
            _gridManager.ClearGiveWayNodes();
            ClearJunctionSelection();

            _isNetworkBuilt = false;
            NetworkInfoText.Text = "Network not built";
            StatusText.Text = $"Loaded {System.IO.Path.GetFileName(dialog.FileName)}";
            UpdateUiState();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error loading network: {ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void BtnMainMenu_Click(object sender, RoutedEventArgs e)
    {
        if (_isSimulationRunning)
        {
            _isSimulationRunning = false;
            await StopPhysicsThreadAsync();
            _trafficManager.ClearTraffic();
            _carRenderer.ClearAllVisuals();
        }

        var menu = new MainMenu();
        menu.Show();
        Close();
    }

    protected override async void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (_closeReady)
        {
            _gridManager.Dispose();
            base.OnClosing(e);
            return;
        }

        // Cancel any re-entrant close events while async cleanup is in progress.
        e.Cancel = true;
        if (_isClosing)
        {
            return;
        }

        // Defer the close to await the physics task without blocking the UI thread.
        _isClosing = true;

        CompositionTarget.Rendering -= RenderLoop;
        if (_isSimulationRunning)
        {
            await StopPhysicsThreadAsync();
        }

        _closeReady = true;
        Dispatcher.BeginInvoke(new Action(Close));
    }
}