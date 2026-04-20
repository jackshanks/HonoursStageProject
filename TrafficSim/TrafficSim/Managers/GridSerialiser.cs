using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using TrafficSim.Models;

namespace TrafficSim.Managers;

/// <summary>
/// Saves and loads grid data to JSON files
/// </summary>
public static class GridSerialiser
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>
    /// Packs the current grid state into a serialisable object
    /// </summary>
    public static GridData ExtractGridData(GridManager gridManager)
    {
        var data = new GridData
        {
            GridWidth = gridManager.GridWidth,
            GridHeight = gridManager.GridHeight,
            CellSizeMeters = gridManager.CellSizeMeters
        };

        foreach (var cell in gridManager.GetAllNonEmptyCells())
        {
            data.Cells.Add(new CellData
            {
                X = cell.X,
                Y = cell.Y,
                Type = cell.Type,
                Direction = cell.Direction,
                JunctionType = cell.JunctionType,
                SpeedLimitMph = cell.SpeedLimitMph,
                GiveWayDirections = [.. cell.GiveWayDirections],
                BlockedTurns = cell.BlockedTurns.Select(t => new BlockedTurnData { From = t.From, To = t.To }).ToList(),
                GreenDuration = cell.GreenDuration,
                YellowDuration = cell.YellowDuration,
                AllRedDuration = cell.AllRedDuration,
                SpawnRateCarsPerMinute = cell.SpawnRateCarsPerMinute,
                ExitWeight = cell.ExitWeight
            });
        }

        return data;
    }

    /// <summary>
    /// Serialises grid data to a JSON file
    /// </summary>
    public static void SaveToFile(GridData data, string filePath)
    {
        var json = JsonSerializer.Serialize(data, JsonOptions);
        File.WriteAllText(filePath, json);
    }

    /// <summary>
    /// Deserialises grid data from a JSON file
    /// </summary>
    public static GridData LoadFromFile(string filePath)
    {
        var json = File.ReadAllText(filePath);
        return JsonSerializer.Deserialize<GridData>(json, JsonOptions) ?? throw new InvalidDataException("Failed to deserialise grid data.");
    }

    /// <summary>
    /// Deserialises grid data from a stream
    /// </summary>
    public static GridData LoadFromStream(Stream stream)
    {
        return JsonSerializer.Deserialize<GridData>(stream, JsonOptions) ?? throw new InvalidDataException("Failed to deserialise grid data.");
    }

    /// <summary>
    /// Applies loaded JSON data back onto the visual grid
    /// </summary>
    public static void ApplyToGrid(GridData data, GridManager gridManager, double cellSizePixels)
    {
        gridManager.CreateGrid(data.GridWidth, data.GridHeight, cellSizePixels);

        foreach (var cd in data.Cells)
        {
            gridManager.SetCellTypeAndDirection(cd.X, cd.Y, cd.Type, cd.Direction);
            gridManager.SetCellSpeedLimit(cd.X, cd.Y, cd.SpeedLimitMph);

            var cell = gridManager.GetCellFromGridCoords(cd.X, cd.Y);
            if (cell == null)
            {
                continue;
            }
            cell.JunctionType = cd.JunctionType;
            cell.GreenDuration = cd.GreenDuration;
            cell.YellowDuration = cd.YellowDuration;
            cell.AllRedDuration = cd.AllRedDuration;
            cell.SpawnRateCarsPerMinute = cd.SpawnRateCarsPerMinute;
            cell.ExitWeight = cd.ExitWeight;
            cell.GiveWayDirections.Clear();
            foreach (var dir in cd.GiveWayDirections)
            {
                cell.GiveWayDirections.Add(dir);
            }

            cell.BlockedTurns.Clear();
            foreach (var bt in cd.BlockedTurns)
            {
                cell.BlockedTurns.Add((bt.From, bt.To));
            }
        }
    }

    /// <summary>
    /// Finds all preset networks compiled into the app
    /// </summary>
    public static List<(string DisplayName, string ResourceName)> GetPrebuiltNetworks()
    {
        var assembly = Assembly.GetExecutingAssembly();
        const string prefix = "TrafficSim.PrebuiltNetworks.";
        const string suffix = ".json";
        var results = new List<(string, string)>();

        foreach (var name in assembly.GetManifestResourceNames())
        {
            if (!name.StartsWith(prefix) || !name.EndsWith(suffix))
            {
                continue;
            }
            var displayName = name[prefix.Length..^suffix.Length].Replace("_", " ");
            results.Add((displayName, name));
        }

        results.Sort((a, b) => string.Compare(a.Item1, b.Item1, StringComparison.Ordinal));
        return results;
    }

    /// <summary>
    /// Loads a preset network from an embedded resource
    /// </summary>
    public static GridData LoadPrebuilt(string resourceName)
    {
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream(resourceName) ?? throw new FileNotFoundException($"Embedded resource not found: {resourceName}");
        return LoadFromStream(stream);
    }
}
