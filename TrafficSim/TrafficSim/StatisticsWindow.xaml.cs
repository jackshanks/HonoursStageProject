using System.Windows;
using TrafficSim.Models;

namespace TrafficSim;

/// <summary>
/// Displays volume, flow, speed, and density statistics collected during a simulation run.
/// </summary>
public partial class StatisticsWindow
{
    public StatisticsWindow(TrafficStatistics stats)
    {
        InitializeComponent();

        PopulateSummary(stats);
        LaneDataGrid.ItemsSource = stats.PerLaneStats;
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();

    private void PopulateSummary(TrafficStatistics stats)
    {
        TxtVolume.Text = stats.TotalVolume.ToString("N0");
        TxtFlow.Text = stats.FlowVehPerHour.ToString("F1");
        TxtSpeed.Text = stats.AverageSpeedMph.ToString("F1");
        TxtDensity.Text = stats.AverageDensityVehPerKm.ToString("F2");
        TxtDuration.Text = FormatDuration(stats.SimulationDurationSeconds);
    }

    private static string FormatDuration(double durationSeconds)
    {
        var totalSeconds = (int)durationSeconds;
        var minutes = totalSeconds / 60;
        var seconds = totalSeconds % 60;
        return $"{minutes}m {seconds}s";
    }
}
