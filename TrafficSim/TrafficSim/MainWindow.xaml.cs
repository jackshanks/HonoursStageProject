using System.Windows;
using System.Windows.Input;
using TrafficSim.Managers;
using TrafficSim.Models;

namespace TrafficSim
{
    public partial class MainWindow : Window
    {
        private readonly GridManager _gridManager;
        private bool _isDrawing = false;

        public MainWindow()
        {
            InitializeComponent();
            _gridManager = new GridManager(GridCanvas);
            // Ensure a grid is made upon start!
            CreateInitialGrid();
        }

        private void CreateInitialGrid()
        {
            var width = int.Parse(TxtGridWidth.Text);
            var height = int.Parse(TxtGridHeight.Text);
            var cellSize = double.Parse(TxtCellSize.Text);

            _gridManager.CreateGrid(width, height, cellSize);
            StatusText.Text = $"Grid created: {width} x {height} cells (each cell = 4m x 4m). Select direction and draw roads.";
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
                StatusText.Text = $"Grid created: {width} x {height} cells (each cell = 4m x 4m). Total area: {width*4}m x {height*4}m";
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
            StatusText.Text = "Grid cleared. Select direction and draw roads.";
        }

        private void GridCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (!_gridManager.HasGrid()) return;

            _isDrawing = true;
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

            // Draw while dragging
            // TODO: SO buggy :>
            if (_isDrawing && e.LeftButton == MouseButtonState.Pressed)
            {
                DrawRoadAtPosition(position);
            }
        }

        private void GridCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            _isDrawing = false;
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
    }
}