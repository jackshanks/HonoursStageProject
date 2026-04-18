using System.ComponentModel;
using System.Diagnostics;
using System.IO;
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
/// Coordinates editor interactions, simulation state, and rendering updates.
/// </summary>
public partial class MainWindow
{
    private readonly GridManager _gridManager;
    private readonly TrafficManager _trafficManager;
    private readonly CarRenderer _carRenderer;
    private readonly List<CarRenderData> _renderBuffer = [];
    private readonly Lock _physicsLock = new();
    private readonly Stopwatch _fpsStopwatch = Stopwatch.StartNew();

    private const double FixedTimeStep = 1.0 / 60.0;
    private const double MaxAccumulatedTime = 0.1;

    private TimeSpan _lastRenderTime = TimeSpan.MinValue;
    private Task? _physicsTask;
    private CancellationTokenSource? _physicsCancellationToken;
    private double _accumulatedTime;
    private double _simulationSpeed = 1.0;
    private bool _collisionsEnabled;
    private bool _isDrawing;
    private Cell? _lastDrawnCell;
    private bool _isErasing;
    private Cell? _lastErasedCell;
    private bool _isNetworkBuilt;
    private bool _isSimulationRunning;
    private bool _isClosing;
    private bool _closeReady;
    private bool _updatingNodePanel;
    private int _frameCount;
    private double _fps;

    private record struct SimNodeSelection(NodeKind Kind, int GridX, int GridY);

    private SimNodeSelection? _selectedSimNode;
    private List<(double cx, double cy, List<Cell> cells)> _junctionGroups = [];

    public MainWindow(GridData? initialGrid = null)
    {
        InitializeComponent();

        _gridManager = new GridManager(GridCanvas);
        _trafficManager = new TrafficManager(_gridManager);
        _carRenderer = new CarRenderer(GridCanvas);

        LoadInitialGrid(initialGrid);
        WireUiEvents();
        UpdateUiState();
    }

    private void LoadInitialGrid(GridData? initialGrid)
    {
        try
        {
            if (initialGrid == null)
            {
                ParseAndCreateGrid();
                return;
            }

            TxtGridWidth.Text = initialGrid.GridWidth.ToString();
            TxtGridHeight.Text = initialGrid.GridHeight.ToString();
            GridSerialiser.ApplyToGrid(initialGrid, _gridManager, ReadCellSizePixels());
            UpdateJunctionGroups();
        }
        catch (Exception ex)
        {
            ShowError($"Error creating grid: {ex.Message}");
        }
    }

    private void WireUiEvents()
    {
        _collisionsEnabled = ChkCollisions.IsChecked == true;

        ChkCollisions.Checked += (_, _) => SetCollisionsEnabled(true);
        ChkCollisions.Unchecked += (_, _) => SetCollisionsEnabled(false);

        CompositionTarget.Rendering += RenderLoop;

        SliderSimSpeed.ValueChanged += (_, _) =>
        {
            lock (_physicsLock)
            {
                _simulationSpeed = SliderSimSpeed.Value;
            }

            TxtSimSpeed.Text = $"{SliderSimSpeed.Value:F1}x";
        };

        SliderSpawnInterval.ValueChanged += OnSpawnIntervalSliderChanged;
        SliderExitWeight.ValueChanged += OnExitWeightSliderChanged;
        SliderGreenDuration.ValueChanged += OnTrafficLightSliderChanged;
        SliderYellowDuration.ValueChanged += OnTrafficLightSliderChanged;
        SliderAllRedDuration.ValueChanged += OnTrafficLightSliderChanged;
    }

    private void SetCollisionsEnabled(bool enabled)
    {
        lock (_physicsLock)
        {
            _collisionsEnabled = enabled;
        }
    }

    private void UpdateUiState()
    {
        BtnBuildNetwork.IsEnabled = !_isSimulationRunning;
        BtnCreateGrid.IsEnabled = !_isSimulationRunning;
        BtnClear.IsEnabled = !_isSimulationRunning;
        BtnLoad.IsEnabled = !_isSimulationRunning;

        var editingVisibility = _isNetworkBuilt ? Visibility.Collapsed : Visibility.Visible;
        GridSetupCard.Visibility = editingVisibility;
        RoadDrawingCard.Visibility = editingVisibility;

        SimRunControlsPanel.Visibility = _isNetworkBuilt ? Visibility.Visible : Visibility.Collapsed;
        BtnStartSim.IsEnabled = !_isSimulationRunning;
        BtnStopSim.IsEnabled = _isSimulationRunning;
        StatusText.Text = GetStatusText();
    }

    private string GetStatusText()
    {
        if (_isSimulationRunning)
        {
            return "Running. Left-click nodes to configure.";
        }

        if (_isNetworkBuilt)
        {
            return "Network built. Click Start to begin, or return to editing.";
        }

        return "Draw roads with left-click, then build network to start.";
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
                        var adjustedDeltaTime = deltaTime * _simulationSpeed;
                        _accumulatedTime = Math.Min(_accumulatedTime + adjustedDeltaTime, MaxAccumulatedTime);

                        while (_accumulatedTime >= FixedTimeStep)
                        {
                            _trafficManager.UpdatePhysics(FixedTimeStep, _collisionsEnabled);
                            _accumulatedTime -= FixedTimeStep;
                        }
                    }

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
        _physicsCancellationToken = null;
        _physicsTask = null;
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
            _gridManager.SetSpawnBacklogs(_trafficManager.GetSpawnBacklogRenderData());

            if (_isSimulationRunning)
            {
                _gridManager.SetTrafficLightNodes(_trafficManager.GetTrafficLightRenderData());
            }
        }

        UpdateFps();
    }

    private void UpdateFps()
    {
        _frameCount++;
        var elapsed = _fpsStopwatch.Elapsed.TotalSeconds;
        if (elapsed < 0.5)
        {
            return;
        }

        _fps = _frameCount / elapsed;
        _frameCount = 0;
        _fpsStopwatch.Restart();
        FpsText.Text = $"FPS: {_fps:F0}";
    }

    private void ParseAndCreateGrid()
    {
        var width = int.Parse(TxtGridWidth.Text);
        var height = int.Parse(TxtGridHeight.Text);
        var cellSize = ReadCellSizePixels();

        if (width <= 0 || height <= 0 || cellSize <= 0)
        {
            throw new ArgumentException("Please enter valid positive numbers.");
        }

        _gridManager.CreateGrid(width, height, cellSize);
    }

    private double ReadCellSizePixels() => double.Parse(TxtCellSize.Text);

    private void BtnCreateGrid_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            ParseAndCreateGrid();
            ResetEditorState(clearGridOverlays: true);
            UpdateJunctionGroups();
            UpdateUiState();
        }
        catch (ArgumentException ex)
        {
            ShowWarning(ex.Message, "Invalid Input");
        }
        catch (Exception ex)
        {
            ShowError($"Error creating grid: {ex.Message}");
        }
    }

    private void BtnBuildNetwork_Click(object sender, RoutedEventArgs e)
    {
        if (!_gridManager.HasGrid())
        {
            ShowWarning("Please create a grid first.", "No Grid");
            return;
        }

        try
        {
            var success = BuildNetworkFromGrid(_gridManager.GridWidth, _gridManager.GridHeight);
            _isNetworkBuilt = true;

            _gridManager.SetEditMode(false);
            _gridManager.SetGiveWayNodes(_trafficManager.GetGiveWayNodePositions());
            PopulateSimNodeIndicators();
            UpdateUiState();

            if (success)
            {
                ShowInfo("Lane network built successfully!", "Success");
            }
            else
            {
                ShowWarning("Network built but may have disconnected nodes.", "Warning");
            }
        }
        catch (Exception ex)
        {
            ShowError($"Error building network: {ex.Message}");
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
            ShowWarning("Please build the network first.", "Network Not Built");
            return;
        }

        _trafficManager.StartStatistics();
        BtnViewStatistics.Visibility = Visibility.Collapsed;
        _isSimulationRunning = true;
        StartPhysicsThread();
        UpdateUiState();
    }

    private async void BtnStopSim_Click(object sender, RoutedEventArgs e)
    {
        await StopSimulationAsync(clearTraffic: false);
        ClearSimNodeSelection();
        BtnViewStatistics.Visibility = Visibility.Visible;
        UpdateUiState();
    }

    private void BtnViewStatistics_Click(object sender, RoutedEventArgs e)
    {
        var stats = _trafficManager.GetFinalStatistics();
        if (stats == null)
        {
            return;
        }

        new StatisticsWindow(stats) { Owner = this }.Show();
    }

    private async void BtnReturnToEditing_Click(object sender, RoutedEventArgs e)
    {
        await StopSimulationAsync(clearTraffic: true);
        ResetToEditingMode();
    }

    private void BtnClear_Click(object sender, RoutedEventArgs e)
    {
        if (!_gridManager.HasGrid())
        {
            return;
        }

        _gridManager.ClearAllCells();
        ResetEditorState(clearGridOverlays: true);
        UpdateJunctionGroups();
        UpdateUiState();
    }

    private async Task StopSimulationAsync(bool clearTraffic)
    {
        if (!_isSimulationRunning)
        {
            if (clearTraffic)
            {
                _trafficManager.ClearTraffic();
                _carRenderer.ClearAllVisuals();
            }

            return;
        }

        _isSimulationRunning = false;
        await StopPhysicsThreadAsync();

        if (clearTraffic)
        {
            _trafficManager.ClearTraffic();
            _carRenderer.ClearAllVisuals();
        }
    }

    private void ResetToEditingMode()
    {
        _isNetworkBuilt = false;
        _trafficManager.ClearNetwork();
        _gridManager.ClearGiveWayNodes();
        _gridManager.ClearTrafficLightNodes();
        _gridManager.SetEditMode(true);
        ClearSimNodeIndicators();
        UpdateJunctionGroups();
        NetworkInfoText.Text = "Network not built";
        UpdateUiState();
    }

    private void ResetEditorState(bool clearGridOverlays)
    {
        _trafficManager.ClearNetwork();
        _trafficManager.ClearTraffic();
        _carRenderer.ClearAllVisuals();
        _gridManager.SetEditMode(true);

        if (clearGridOverlays)
        {
            _gridManager.ClearGiveWayNodes();
            _gridManager.ClearTrafficLightNodes();
        }

        ClearSimNodeIndicators();
        _isNetworkBuilt = false;
        NetworkInfoText.Text = "Network not built";
    }

    private void GridCanvas_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!_gridManager.HasGrid() || _isSimulationRunning || _isNetworkBuilt)
        {
            return;
        }

        _isErasing = true;
        _lastErasedCell = null;
        EraseAtPosition(e.GetPosition(GridCanvas));
    }

    private void GridCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!_gridManager.HasGrid())
        {
            return;
        }

        var clickPosition = e.GetPosition(GridCanvas);

        if (_isSimulationRunning)
        {
            SelectSimNode(clickPosition.X, clickPosition.Y);
            return;
        }

        if (_isNetworkBuilt)
        {
            return;
        }

        var group = FindClickedJunctionGroup(clickPosition);
        if (group != null)
        {
            OpenJunctionConfig(group);
            return;
        }

        _isDrawing = true;
        _lastDrawnCell = null;
        DrawRoadAtPosition(clickPosition);
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

        if (_isDrawing &&
            e.LeftButton == MouseButtonState.Pressed &&
            !_isSimulationRunning &&
            !_isNetworkBuilt &&
            cell != _lastDrawnCell)
        {
            DrawRoadAtPosition(position);
            _lastDrawnCell = cell;
        }

        if (_isErasing &&
            e.RightButton == MouseButtonState.Pressed &&
            !_isSimulationRunning &&
            !_isNetworkBuilt &&
            cell != _lastErasedCell)
        {
            EraseAtPosition(position);
            _lastErasedCell = cell;
        }
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

        if (IsJunctionModeSelected())
        {
            ApplyJunctionToCell(cell);
            return;
        }

        ApplyRoadToCell(cell);
    }

    private void ApplyJunctionToCell(Cell cell)
    {
        if (cell.Type != CellType.Intersection)
        {
            _gridManager.SetCellTypeAndDirection(cell.X, cell.Y, CellType.Intersection, TrafficDirection.None);
        }

        cell.JunctionType = GetSelectedJunctionType();
        UpdateJunctionGroups();
    }

    private void ApplyRoadToCell(Cell cell)
    {
        var previousType = cell.Type;
        var direction = GetSelectedDirection();
        var speedLimit = GetSelectedSpeedLimit();

        switch (cell.Type)
        {
            case CellType.Empty:
            case CellType.Intersection:
                _gridManager.SetCellTypeAndDirection(cell.X, cell.Y, CellType.Road, direction);
                break;
            case CellType.Road:
                _gridManager.SetCellDirection(cell.X, cell.Y, direction);
                break;
        }

        _gridManager.SetCellSpeedLimit(cell.X, cell.Y, speedLimit);

        if (previousType == CellType.Intersection)
        {
            UpdateJunctionGroups();
        }
    }

    private bool IsJunctionModeSelected() => RbJunction.IsChecked == true;

    private JunctionType GetSelectedJunctionType()
    {
        return RbTrafficLight.IsChecked == true
            ? JunctionType.TrafficLight
            : JunctionType.GiveWay;
    }

    private int GetSelectedSpeedLimit()
    {
        var speedButtons = new[] { RbSpeed20, RbSpeed30, RbSpeed40, RbSpeed50, RbSpeed60, RbSpeed70 };
        foreach (var button in speedButtons)
        {
            if (button.IsChecked == true && button.Tag is string tag && int.TryParse(tag, out var mph))
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
        if (cell == null)
        {
            return;
        }

        var wasIntersection = cell.Type == CellType.Intersection;
        if (cell.Type is CellType.Road or CellType.Intersection)
        {
            _gridManager.SetCellTypeAndDirection(cell.X, cell.Y, CellType.Empty, TrafficDirection.None);
        }

        if (wasIntersection)
        {
            UpdateJunctionGroups();
        }
    }

    private TrafficDirection GetSelectedDirection()
    {
        if (RbNorth.IsChecked == true) return TrafficDirection.North;
        if (RbEast.IsChecked == true) return TrafficDirection.East;
        if (RbSouth.IsChecked == true) return TrafficDirection.South;
        return RbWest.IsChecked == true ? TrafficDirection.West : TrafficDirection.East;
    }

    private void UpdateJunctionGroups()
    {
        _junctionGroups = _gridManager.ComputeJunctionGroups()
            .Select(cells =>
            {
                var centerX = cells.Average(cell => (cell.X + 0.5) * _gridManager.CellSizePixels);
                var centerY = cells.Average(cell => (cell.Y + 0.5) * _gridManager.CellSizePixels);
                return (centerX, centerY, cells);
            })
            .ToList();

        _gridManager.SetJunctionGroupCenters(_junctionGroups.Select(group => (group.cx, group.cy)));
    }

    private List<Cell>? FindClickedJunctionGroup(Point pixelPosition)
    {
        var hitRadius = _gridManager.CellSizePixels * 0.5;
        foreach (var (centerX, centerY, cells) in _junctionGroups)
        {
            if (Math.Abs(pixelPosition.X - centerX) < hitRadius &&
                Math.Abs(pixelPosition.Y - centerY) < hitRadius)
            {
                return cells;
            }
        }

        return null;
    }

    private void OpenJunctionConfig(List<Cell> cells)
    {
        var dialog = new JunctionConfigWindow(cells) { Owner = this };
        dialog.ShowDialog();
        _gridManager.RenderGrid();
    }

    private void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        if (!_gridManager.HasGrid())
        {
            return;
        }

        var dialog = new SaveFileDialog
        {
            Filter = "JSON files (*.json)|*.json",
            DefaultExt = ".json",
            Title = "Save Road Network"
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            var data = GridSerialiser.ExtractGridData(_gridManager);
            GridSerialiser.SaveToFile(data, dialog.FileName);
            StatusText.Text = $"Network saved to {Path.GetFileName(dialog.FileName)}";
        }
        catch (Exception ex)
        {
            ShowError($"Error saving network: {ex.Message}");
        }
    }

    private async void BtnLoad_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "JSON files (*.json)|*.json",
            Title = "Load Road Network"
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            await StopSimulationAsync(clearTraffic: false);

            var data = GridSerialiser.LoadFromFile(dialog.FileName);
            TxtGridWidth.Text = data.GridWidth.ToString();
            TxtGridHeight.Text = data.GridHeight.ToString();
            GridSerialiser.ApplyToGrid(data, _gridManager, ReadCellSizePixels());

            ResetEditorState(clearGridOverlays: true);
            UpdateJunctionGroups();
            StatusText.Text = $"Loaded {Path.GetFileName(dialog.FileName)}";
            UpdateUiState();
        }
        catch (Exception ex)
        {
            ShowError($"Error loading network: {ex.Message}");
        }
    }

    private async void BtnMainMenu_Click(object sender, RoutedEventArgs e)
    {
        await StopSimulationAsync(clearTraffic: true);

        var menu = new MainMenu();
        menu.Show();
        Close();
    }

    protected override async void OnClosing(CancelEventArgs e)
    {
        if (_closeReady)
        {
            _gridManager.Dispose();
            base.OnClosing(e);
            return;
        }

        e.Cancel = true;
        if (_isClosing)
        {
            return;
        }

        _isClosing = true;
        CompositionTarget.Rendering -= RenderLoop;

        if (_isSimulationRunning)
        {
            await StopPhysicsThreadAsync();
        }

        _closeReady = true;
        Dispatcher.BeginInvoke(new Action(Close));
    }

    private void SelectSimNode(double pixelX, double pixelY)
    {
        var hit = _trafficManager.GetSimNodeAt(pixelX, pixelY);
        if (hit == null)
        {
            ClearSimNodeSelection();
            return;
        }

        var (kind, gridX, gridY) = hit.Value;
        _selectedSimNode = new SimNodeSelection(kind, gridX, gridY);
        _gridManager.SetSelectedNode(gridX, gridY);
        UpdateSelectedNodePanel();
    }

    private void ClearSimNodeSelection()
    {
        _selectedSimNode = null;
        _gridManager.ClearSelectedNode();
        UpdateSelectedNodePanel();
    }

    private void UpdateSelectedNodePanel()
    {
        TxtNoNodeSelected.Visibility = _selectedSimNode == null ? Visibility.Visible : Visibility.Collapsed;
        PanelSpawnNode.Visibility = Visibility.Collapsed;
        PanelExitNode.Visibility = Visibility.Collapsed;
        PanelTrafficLight.Visibility = Visibility.Collapsed;

        if (_selectedSimNode == null)
        {
            return;
        }

        _updatingNodePanel = true;
        try
        {
            var (kind, gridX, gridY) = _selectedSimNode.Value;
            switch (kind)
            {
                case NodeKind.Spawn:
                    ShowSpawnNodePanel(gridX, gridY);
                    break;
                case NodeKind.Exit:
                    ShowExitNodePanel(gridX, gridY);
                    break;
                case NodeKind.TrafficLight:
                    ShowTrafficLightPanel(gridX, gridY);
                    break;
            }
        }
        finally
        {
            _updatingNodePanel = false;
        }
    }

    private void ShowSpawnNodePanel(int gridX, int gridY)
    {
        PanelSpawnNode.Visibility = Visibility.Visible;
        TxtSelectedSpawnNode.Text = $"SPAWN NODE ({gridX}, {gridY})";
        var rate = _trafficManager.GetSpawnRate(gridX, gridY);
        SliderSpawnInterval.Value = rate;
        TxtSpawnInterval.Text = $"{rate:F0} cars/min";
    }

    private void ShowExitNodePanel(int gridX, int gridY)
    {
        PanelExitNode.Visibility = Visibility.Visible;
        TxtSelectedExitNode.Text = $"EXIT NODE ({gridX}, {gridY})";
        var weight = _trafficManager.GetExitNodeWeight(gridX, gridY);
        SliderExitWeight.Value = weight;
        TxtExitWeight.Text = $"{weight:F1}";
    }

    private void ShowTrafficLightPanel(int gridX, int gridY)
    {
        PanelTrafficLight.Visibility = Visibility.Visible;
        TxtSelectedTrafficLight.Text = $"JUNCTION ({gridX}, {gridY})";

        var timings = _trafficManager.GetTrafficLightTimings(gridX, gridY);
        if (!timings.HasValue)
        {
            return;
        }

        SliderGreenDuration.Value = timings.Value.green;
        SliderYellowDuration.Value = timings.Value.yellow;
        SliderAllRedDuration.Value = timings.Value.allRed;
        TxtGreenDuration.Text = $"{timings.Value.green:F0} s";
        TxtYellowDuration.Text = $"{timings.Value.yellow:F1} s";
        TxtAllRedDuration.Text = $"{timings.Value.allRed:F1} s";
    }

    private void PopulateSimNodeIndicators()
    {
        var spawnNodes = _trafficManager.GetSpawnNodeInfos();
        var exitNodes = _trafficManager.GetExitNodeInfos();
        _gridManager.SetSpawnNodes(spawnNodes.Select(node => (node.gridX, node.gridY)));
        _gridManager.SetExitNodes(exitNodes.Select(node => (node.gridX, node.gridY)));
    }

    private void ClearSimNodeIndicators()
    {
        ClearSimNodeSelection();
        _gridManager.ClearSpawnBacklogs();
        _gridManager.ClearSpawnNodes();
        _gridManager.ClearExitNodes();
    }

    private void OnSpawnIntervalSliderChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_updatingNodePanel)
        {
            return;
        }

        TxtSpawnInterval.Text = $"{SliderSpawnInterval.Value:F0} cars/min";
        if (_selectedSimNode?.Kind == NodeKind.Spawn)
        {
            _trafficManager.SetSpawnRate(
                _selectedSimNode.Value.GridX,
                _selectedSimNode.Value.GridY,
                SliderSpawnInterval.Value);
        }
    }

    private void OnExitWeightSliderChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_updatingNodePanel)
        {
            return;
        }

        TxtExitWeight.Text = $"{SliderExitWeight.Value:F1}";
        if (_selectedSimNode?.Kind == NodeKind.Exit)
        {
            _trafficManager.SetExitNodeWeight(
                _selectedSimNode.Value.GridX,
                _selectedSimNode.Value.GridY,
                SliderExitWeight.Value);
        }
    }

    private void OnTrafficLightSliderChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_updatingNodePanel)
        {
            return;
        }

        TxtGreenDuration.Text = $"{SliderGreenDuration.Value:F0} s";
        TxtYellowDuration.Text = $"{SliderYellowDuration.Value:F1} s";
        TxtAllRedDuration.Text = $"{SliderAllRedDuration.Value:F1} s";

        if (_selectedSimNode?.Kind == NodeKind.TrafficLight)
        {
            _trafficManager.SetTrafficLightTimings(
                _selectedSimNode.Value.GridX,
                _selectedSimNode.Value.GridY,
                SliderGreenDuration.Value,
                SliderYellowDuration.Value,
                SliderAllRedDuration.Value);
        }
    }

    private static void ShowInfo(string message, string title)
    {
        MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private static void ShowWarning(string message, string title)
    {
        MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private static void ShowError(string message)
    {
        MessageBox.Show(message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
    }
}
