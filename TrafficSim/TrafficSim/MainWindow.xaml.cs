using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
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
    private readonly DispatcherTimer _renderTimer;
    
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
    
    private double _simulationSpeed = 1.0;
    private bool _collisionsEnabled;
    
    private bool _isNetworkBuilt;
    private bool _isSimulationRunning;
    private bool _isClosing;
    private bool _closeReady;

    public MainWindow()
    {
        InitializeComponent();
        _gridManager = new GridManager(GridCanvas);
        _trafficManager = new TrafficManager(_gridManager);
        _carRenderer = new CarRenderer(GridCanvas);

        ParseAndCreateGrid();
        
        _collisionsEnabled = ChkCollisions.IsChecked == true;
        
        ChkCollisions.Checked += (_, _) => { lock (_physicsLock) { _collisionsEnabled = true; } };
        ChkCollisions.Unchecked += (_, _) => { lock (_physicsLock) { _collisionsEnabled = false; } };

        // render timer at 60fps on the main thread
        _renderTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(16.667)
        };
        _renderTimer.Tick += RenderLoop;
        _renderTimer.Start();

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
        
        BtnStartSim.IsEnabled = _isNetworkBuilt && !_isSimulationRunning;
        BtnStopSim.IsEnabled = _isSimulationRunning;
        
        if (_isSimulationRunning)
        {
            StatusText.Text = "Simulation running. Right-click to spawn cars. Stop simulation to edit grid.";
        }
        else if (_isNetworkBuilt)
        {
            StatusText.Text = "Network built. Click 'Start Simulation' to begin. Left-click: draw roads.";
        }
        else
        {
            StatusText.Text = "Draw roads with left-click, then build network to start simulation.";
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

            while (!token.IsCancellationRequested)
            {
                try
                {
                    var currentTicks = physicsStopwatch.ElapsedTicks;
                    var elapsedTicks = currentTicks - lastPhysicsTicks;
                    lastPhysicsTicks = currentTicks;

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
                    
                    // Delay to prevent CPU locking
                    await Task.Delay(1, token);
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
        var pixelsPerMeter = _gridManager.GetPixelsPerMeter();
        
        var renderData = _trafficManager.GetRenderData();
        _carRenderer.UpdateAllCarVisuals(renderData, pixelsPerMeter);
        CarCountText.Text = renderData.Count.ToString();

        if (_isNetworkBuilt)
        {
            NetworkInfoText.Text = _trafficManager.GetNetworkInfo();
        }
    }

    private void ParseAndCreateGrid()
    {
        var width = int.Parse(TxtGridWidth.Text);
        var height = int.Parse(TxtGridHeight.Text);
        var cellSize = double.Parse(TxtCellSize.Text);

        if (width <= 0 || height <= 0 || cellSize <= 0)
            throw new ArgumentException("Please enter valid positive numbers.");

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
            var width = int.Parse(TxtGridWidth.Text);
            var height = int.Parse(TxtGridHeight.Text);
            
            var success = BuildNetworkFromGrid(width, height);
            
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
        await StopPhysicsThreadAsync();
        UpdateUiState();
    }
    
    private void BtnClear_Click(object sender, RoutedEventArgs e)
    {
        if (!_gridManager.HasGrid()) return;

        _gridManager.ClearAllCells();
        _trafficManager.ClearNetwork();
        _trafficManager.ClearTraffic();
        _carRenderer.ClearAllVisuals();
        
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

        if (!_gridManager.HasGrid() || _isSimulationRunning) return;

        _isErasing = true;
        _lastErasedCell = null;
        EraseAtPosition(e.GetPosition(GridCanvas));
    }

    private void GridCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!_gridManager.HasGrid() || _isSimulationRunning) return;

        _isDrawing = true;
        _lastDrawnCell = null;
        DrawRoadAtPosition(e.GetPosition(GridCanvas));
    }

    private void GridCanvas_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_gridManager.HasGrid()) return;

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
        if (_isSimulationRunning) return;

        var cell = _gridManager.GetCellFromPixel(position.X, position.Y);
        if (cell == null) return;

        var selectedDirection = GetSelectedDirection();

        if (cell.Type == CellType.Empty)
            _gridManager.SetCellTypeAndDirection(cell.X, cell.Y, CellType.Road, selectedDirection);
        else if (cell.Type == CellType.Road)
            _gridManager.SetCellDirection(cell.X, cell.Y, selectedDirection);
    }

    private void EraseAtPosition(Point position)
    {
        if (_isSimulationRunning) return;

        var cell = _gridManager.GetCellFromPixel(position.X, position.Y);

        if (cell?.Type is CellType.Road or CellType.Intersection)
            _gridManager.SetCellTypeAndDirection(cell.X, cell.Y, CellType.Empty, TrafficDirection.None);
    }

    private TrafficDirection GetSelectedDirection()
    {
        if (RbNorth.IsChecked == true) return TrafficDirection.North;
        if (RbEast.IsChecked == true) return TrafficDirection.East;
        if (RbSouth.IsChecked == true) return TrafficDirection.South;
        return RbWest.IsChecked == true ? TrafficDirection.West : TrafficDirection.East;
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
        if (_isClosing) return;

        // Defer the close to await the physics task without blocking the UI thread.
        _isClosing = true;

        _renderTimer.Stop();
        if (_isSimulationRunning)
            await StopPhysicsThreadAsync();

        _closeReady = true;
        Dispatcher.BeginInvoke(new Action(Close));
    }
}