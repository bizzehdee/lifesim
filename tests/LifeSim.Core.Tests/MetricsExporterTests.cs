using System.Text.Json;
using LifeSim.Core.Events;
using LifeSim.Core.Metrics;
using LifeSim.Core.Snapshot;
using LifeSim.Core.World;

namespace LifeSim.Core.Tests;

public class MetricsExporterTests
{
    private static SimulationMetrics SampleMetrics() => new()
    {
        Population = 3,
        Extinct = false,
        Births = 2,
        Deaths = 1,
        SuccessfulGrazing = 4,
        FailedGrazing = 5,
        SuccessfulPredation = 1,
        FailedPredation = 2,
        EnergyMin = 1.25,
        EnergyAverage = 12.5,
        EnergyMax = 40.0,
        TraitAverages = new TraitAverages { Size = 1.5, SpeedCapacity = 2.0 },
        PopulationByBiome =
        [
            new BiomePopulation { Biome = Biome.Grassland, Count = 2 },
            new BiomePopulation { Biome = Biome.Desert, Count = 1 },
        ],
        ActiveEvents = [EventType.ResourceBlight],
    };

    [Fact]
    public void Csv_headerAndRow_haveMatchingColumnCounts()
    {
        string[] header = MetricsExporter.CsvHeader().Split(',');
        string[] row = MetricsExporter.CsvRow(5, SampleMetrics()).Split(',');

        Assert.Equal(header.Length, row.Length);
    }

    [Fact]
    public void Csv_formatsNumbersInvariantCulture_andMapsScalarColumns()
    {
        string[] header = MetricsExporter.CsvHeader().Split(',');
        string[] row = MetricsExporter.CsvRow(5, SampleMetrics()).Split(',');

        string Column(string name) => row[Array.IndexOf(header, name)];

        Assert.Equal("5", Column("tick"));
        Assert.Equal("3", Column("population"));
        Assert.Equal("false", Column("extinct"));
        Assert.Equal("12.5", Column("energy_avg"));    // invariant decimal point, not a comma
        Assert.Equal("2", Column("pop_grassland"));
        Assert.Equal("1", Column("pop_desert"));
        Assert.Equal("0", Column("pop_swamp"));         // absent from the list → zero
        Assert.Equal("1", Column("active_event_count"));
    }

    [Fact]
    public void Ndjson_isSingleLine_andParsesBackToTheSameValues()
    {
        string line = MetricsExporter.NdjsonLine(7, SampleMetrics());

        Assert.DoesNotContain('\n', line);

        using var doc = JsonDocument.Parse(line);
        JsonElement root = doc.RootElement;
        Assert.Equal(7, root.GetProperty("tick").GetInt64());

        JsonElement metrics = root.GetProperty("metrics");
        Assert.Equal(3, metrics.GetProperty("population").GetInt32());
        Assert.Equal(2, metrics.GetProperty("births").GetInt32());

        // Enums serialize as their (PascalCase) member names, matching the snapshot's enum fields.
        Assert.Equal("Grassland", metrics.GetProperty("population_by_biome")[0].GetProperty("biome").GetString());
        Assert.Equal("ResourceBlight", metrics.GetProperty("active_events")[0].GetString());
    }
}
