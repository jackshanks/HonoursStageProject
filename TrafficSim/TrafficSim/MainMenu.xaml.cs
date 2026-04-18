using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using TrafficSim.Managers;
using TrafficSim.Models;

namespace TrafficSim;

public partial class MainMenu
{
    private readonly List<(string DisplayName, string ResourceName)> _prebuiltNetworks;

    public MainMenu()
    {
        InitializeComponent();

        _prebuiltNetworks = GridSerialiser.GetPrebuiltNetworks();
        PrebuiltList.ItemsSource = _prebuiltNetworks.Select(network => network.DisplayName);
        BtnLoadPrebuilt.IsEnabled = _prebuiltNetworks.Count > 0;
    }

    private void BtnNewNetwork_Click(object sender, RoutedEventArgs e)
    {
        OpenEditor(null);
    }

    private void BtnLoadNetwork_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "JSON files (*.json)|*.json",
            Title = "Load Road Network"
        };

        if (dialog.ShowDialog() != true) return;

        LoadGrid(() => GridSerialiser.LoadFromFile(dialog.FileName), "Error loading network");
    }

    private void BtnLoadPrebuilt_Click(object sender, RoutedEventArgs e)
    {
        LoadSelectedPrebuilt();
    }

    private void PrebuiltList_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        LoadSelectedPrebuilt();
    }

    private void LoadSelectedPrebuilt()
    {
        var index = PrebuiltList.SelectedIndex;
        if (index < 0 || index >= _prebuiltNetworks.Count) return;

        LoadGrid(() => GridSerialiser.LoadPrebuilt(_prebuiltNetworks[index].ResourceName), "Error loading pre-built network");
    }

    private void LoadGrid(Func<GridData> loadAction, string errorMessage)
    {
        try
        {
            OpenEditor(loadAction());
        }
        catch (Exception ex)
        {
            MessageBox.Show($"{errorMessage}: {ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OpenEditor(GridData? gridData)
    {
        var editor = new MainWindow(gridData);
        editor.Show();
        Close();
    }
}
