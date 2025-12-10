using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using TrafficSim.Managers;
using TrafficSim.Models;
using TrafficSim.Rendering;

namespace TrafficSim;

public partial class MainWindow : Window
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
    private long _lastTicks;

    private bool _isDrawing;
    private Cell? _lastDrawnCell;
    
    private double _simulationSpeed = 1.0;
    
    private volatile bool _collisionsEnabled;

    public MainWindow()
    {
        InitializeComponent();
        _gridManager = new GridManager(GridCanvas);
        _trafficManager = new TrafficManager(_gridManager);
        _carRenderer = new CarRenderer(GridCanvas);

        CreateInitialGrid();

        var stopwatch = Stopwatch.StartNew();
        _lastTicks = stopwatch.ElapsedTicks;
        
        _collisionsEnabled = ChkCollisions.IsChecked == true;
        
        ChkCollisions.Checked += (_, _) => _collisionsEnabled = true;
        ChkCollisions.Unchecked += (_, _) => _collisionsEnabled = false;
        
        StartPhysicsThread();

        // Render timer runs at 60fps on UI thread
        _renderTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(16)
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
                        var adjustedDeltaTime = deltaTime * _simulationSpeed;
                        _accumulatedTime += adjustedDeltaTime;

                        if (_accumulatedTime > MaxAccumulatedTime)
                        {
                            _accumulatedTime = MaxAccumulatedTime;
                        }
                    }
                    
                    var collisionsEnabled = _collisionsEnabled;
                    
                    lock (_physicsLock)
                    {
                        while (_accumulatedTime >= FixedTimeStep)
                        {
                            _trafficManager.UpdatePhysics(FixedTimeStep, collisionsEnabled);
                            _accumulatedTime -= FixedTimeStep;
                        }
                    }
                    
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
    
    private void StopPhysicsThread()
    {
        _physicsCancellationToken?.Cancel();
        
        if (_physicsTask != null)
        {
            var timeout = TimeSpan.FromMilliseconds(500);
            var completed = _physicsTask.Wait(timeout);
            
            if (!completed)
            {
                Debug.WriteLine("Physics thread did not stop within timeout, continuing shutdown.");
            }
        }
        
        _physicsCancellationToken?.Dispose();
    }
    
    private void RenderLoop(object? sender, EventArgs e)
    {
        var pixelsPerMeter = _gridManager.GetPixelsPerMeter();
        var cars = _trafficManager.GetCars();
        _carRenderer.UpdateAllCarVisuals(cars, pixelsPerMeter);
        
        CarCountText.Text = $"Cars: {_trafficManager.GetCarCount()}";
    }

    private void CreateInitialGrid()
    {
        var width = int.Parse(TxtGridWidth.Text);
        var height = int.Parse(TxtGridHeight.Text);
        var cellSize = double.Parse(TxtCellSize.Text);

        _gridManager.CreateGrid(width, height, cellSize);
        StatusText.Text =
            $"Grid created: {width} x {height} cells (each cell = 4m x 4m). Left-click: draw roads, Right-click: spawn cars.";
    }

    private void BtnCreateGrid_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var width = int.Parse(TxtGridWidth.Text);
            var height = int.Parse(TxtGridHeight.Text);
            var cellSize = double.Parse(TxtCellSize.Text);

            if (width <= 0 || height <= 0 || cellSize <= 0)
            {
                MessageBox.Show("Please enter valid positive numbers.", "Invalid Input",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _gridManager.CreateGrid(width, height, cellSize);
            _trafficManager.ClearTraffic();
            _carRenderer.ClearAllVisuals();
            StatusText.Text =
                $"Grid created: {width} x {height} cells (each cell = 4m x 4m). Total area: {width * 4}m x {height * 4}m";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error creating grid: {ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void BtnClear_Click(object sender, RoutedEventArgs e)
    {
        if (!_gridManager.HasGrid()) return;

        _gridManager.ClearAllCells();
        _trafficManager.ClearTraffic();
        _carRenderer.ClearAllVisuals();
        StatusText.Text = "Grid and traffic cleared.";
    }

    private void GridCanvas_RightClick(object sender, MouseButtonEventArgs e)
    {
        if (!_gridManager.HasGrid()) return;

        var pos = e.GetPosition(GridCanvas);
        var success = _trafficManager.SpawnCarAt(pos.X, pos.Y);

        if (!success)
        {
            StatusText.Text = "Cannot spawn car here - must be on a road with a valid direction.";
        }
    }

    private void GridCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!_gridManager.HasGrid()) return;

        _isDrawing = true;
        _lastDrawnCell = null;
        DrawRoadAtPosition(e.GetPosition(GridCanvas));
    }

    private void GridCanvas_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_gridManager.HasGrid()) return;

        var position = e.GetPosition(GridCanvas);
        var cell = _gridManager.GetCellFromPixel(position.X, position.Y);

        if (cell != null)
        {
            StatusText.Text = GridManager.GetCellInfo(cell);
        }

        if (!_isDrawing || e.LeftButton != MouseButtonState.Pressed) return;
        if (cell == _lastDrawnCell) return;
        DrawRoadAtPosition(position);
        _lastDrawnCell = cell;
    }

    private void GridCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _isDrawing = false;
        _lastDrawnCell = null;
    }

    private void DrawRoadAtPosition(Point position)
    {
        var cell = _gridManager.GetCellFromPixel(position.X, position.Y);

        if (cell == null) return;

        var selectedDirection = GetSelectedDirection();

        switch (cell.Type)
        {
            case CellType.Empty:
                _gridManager.SetCellTypeAndDirection(cell.X, cell.Y, CellType.Road, selectedDirection);
                break;

            case CellType.Road when cell.Direction == selectedDirection:
                _gridManager.SetCellTypeAndDirection(cell.X, cell.Y, CellType.Empty, TrafficDirection.None);
                break;

            case CellType.Road:
                _gridManager.SetCellDirection(cell.X, cell.Y, selectedDirection);
                break;

            case CellType.Intersection:
                break;

            default:
                throw new ArgumentOutOfRangeException();
        }

        StatusText.Text = GridManager.GetCellInfo(cell);
    }

    private TrafficDirection GetSelectedDirection()
    {
        if (RbNorth.IsChecked == true) return TrafficDirection.North;
        if (RbEast.IsChecked == true) return TrafficDirection.East;
        if (RbSouth.IsChecked == true) return TrafficDirection.South;
        if (RbWest.IsChecked == true) return TrafficDirection.West;

        return TrafficDirection.East;
    }
    
    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        _renderTimer.Stop();
        StopPhysicsThread();
        _gridManager.Dispose();
        base.OnClosing(e);
    }
}