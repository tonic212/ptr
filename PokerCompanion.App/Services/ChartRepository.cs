using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using PokerCompanion.Core;

namespace PokerCompanion.App.Services;

public sealed class ChartRepository
{
    public IReadOnlyList<Chart> Load()
    {
        string path = Path.Combine(BundledDataStore.Root, "Data", "charts.json");
        var charts = JsonSerializer.Deserialize<List<Chart>>(File.ReadAllText(path))
            ?? throw new InvalidDataException("Chart data is empty.");
        foreach (Chart chart in charts)
            if (chart.Actions.Count != 169 || !File.Exists(Path.Combine(BundledDataStore.Root, chart.ImagePath)))
                throw new InvalidDataException($"Invalid chart: {chart.Name}");
        return charts;
    }
}
