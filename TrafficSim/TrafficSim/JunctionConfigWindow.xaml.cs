using System.Windows;
using System.Windows.Controls;
using TrafficSim.Models;

namespace TrafficSim;

public partial class JunctionConfigWindow
{
    private readonly IReadOnlyList<Cell> _cells;
    private readonly (CheckBox checkBox, TrafficDirection direction)[] _giveWayDirectionCheckboxes;
    private readonly (CheckBox checkBox, TrafficDirection from, TrafficDirection to)[] _turnCheckboxes;

    public JunctionConfigWindow(IReadOnlyList<Cell> cells)
    {
        InitializeComponent();
        _cells = cells;

        _giveWayDirectionCheckboxes =
        [
            (DlgChkGWNorth, TrafficDirection.North),
            (DlgChkGWEast, TrafficDirection.East),
            (DlgChkGWSouth, TrafficDirection.South),
            (DlgChkGWWest, TrafficDirection.West)
        ];

        _turnCheckboxes =
        [
            (DlgChkTurnNN, TrafficDirection.North, TrafficDirection.North),
            (DlgChkTurnNE, TrafficDirection.North, TrafficDirection.East),
            (DlgChkTurnNW, TrafficDirection.North, TrafficDirection.West),
            (DlgChkTurnEN, TrafficDirection.East, TrafficDirection.North),
            (DlgChkTurnEE, TrafficDirection.East, TrafficDirection.East),
            (DlgChkTurnES, TrafficDirection.East, TrafficDirection.South),
            (DlgChkTurnSE, TrafficDirection.South, TrafficDirection.East),
            (DlgChkTurnSS, TrafficDirection.South, TrafficDirection.South),
            (DlgChkTurnSW, TrafficDirection.South, TrafficDirection.West),
            (DlgChkTurnWN, TrafficDirection.West, TrafficDirection.North),
            (DlgChkTurnWS, TrafficDirection.West, TrafficDirection.South),
            (DlgChkTurnWW, TrafficDirection.West, TrafficDirection.West)
        ];

        PopulateFromCell(cells[0]);
        UpdateGiveWaySectionVisibility();
    }

    private void PopulateFromCell(Cell cell)
    {
        TxtJunctionHeader.Text = _cells.Count == 1
            ? $"Junction ({cell.X}, {cell.Y})"
            : $"Junction group - {_cells.Count} cells";

        SetSelectedJunctionType(cell.JunctionType);

        foreach (var (checkBox, direction) in _giveWayDirectionCheckboxes)
        {
            checkBox.IsChecked = cell.GiveWayDirections.Contains(direction);
        }

        foreach (var (checkBox, from, to) in _turnCheckboxes)
        {
            checkBox.IsChecked = !cell.BlockedTurns.Contains((from, to));
        }
    }

    private void DlgJunctionType_Changed(object sender, RoutedEventArgs e)
    {
        UpdateGiveWaySectionVisibility();
    }

    private void UpdateGiveWaySectionVisibility()
    {
        if (GiveWaySection == null)
        {
            return;
        }

        GiveWaySection.Visibility = DlgRbGiveWay.IsChecked == true
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void BtnOk_Click(object sender, RoutedEventArgs e)
    {
        var junctionType = GetSelectedJunctionType();
        var giveWayDirections = _giveWayDirectionCheckboxes
            .Where(item => item.checkBox.IsChecked == true)
            .Select(item => item.direction)
            .ToList();
        var blockedTurns = _turnCheckboxes
            .Where(item => item.checkBox.IsChecked != true)
            .Select(item => (item.from, item.to))
            .ToList();

        foreach (var cell in _cells)
        {
            cell.JunctionType = junctionType;

            cell.GiveWayDirections.Clear();
            foreach (var direction in giveWayDirections)
            {
                cell.GiveWayDirections.Add(direction);
            }

            cell.BlockedTurns.Clear();
            foreach (var turn in blockedTurns)
            {
                cell.BlockedTurns.Add(turn);
            }
        }

        DialogResult = true;
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void SetSelectedJunctionType(JunctionType junctionType)
    {
        DlgRbGiveWay.IsChecked = junctionType == JunctionType.GiveWay;
        DlgRbTrafficLight.IsChecked = junctionType == JunctionType.TrafficLight;
    }

    private JunctionType GetSelectedJunctionType()
    {
        return DlgRbTrafficLight.IsChecked == true
            ? JunctionType.TrafficLight
            : JunctionType.GiveWay;
    }
}
