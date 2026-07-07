using Avalonia.Media;
using LifeSim.App.Presentation;
using LifeSim.Core.Configuration;
using LifeSim.Core.Determinism;
using LifeSim.Core.Neat;
using LifeSim.Core.Organisms;
using LifeSim.Core.Snapshot;
using LifeSim.Core.World;

namespace LifeSim.App.Tests;

public class PresentationTests
{
    private static OrganismSnapshot Organism(
        double energy = 50.0, OrganismAction? lastAction = null, ActionResult result = ActionResult.None,
        double size = 5.0) => new()
        {
            OrganismId = 1,
            Name = "Test-Test-Org",
            Energy = energy,
            Genome = new GenomeSnapshot { Size = size, ThermalCenter = 20.0, ThermalWidth = 10.0 },
            Brain = new NeatGenome(),
            LastAction = lastAction,
            LastActionResult = result,
        };

    [Fact]
    public void EnergyColour_rampsFromRedToGreen()
    {
        Color empty = SimulationPalette.EnergyColour(0, 100);
        Color full = SimulationPalette.EnergyColour(100, 100);
        Assert.True(empty.R > empty.G, "Empty should be reddish.");
        Assert.True(full.G > full.R, "Full should be greenish.");
    }

    [Fact]
    public void ModulateByEnergy_darkensDepletedTiles()
    {
        Color full = SimulationPalette.ModulateByEnergy(SimulationPalette.Grassland, 20, 20);
        Color empty = SimulationPalette.ModulateByEnergy(SimulationPalette.Grassland, 0, 20);
        Assert.Equal(SimulationPalette.Grassland, full);           // at cap → unchanged
        Assert.True(empty.G < full.G, "Depleted tile should be darker.");
    }

    [Fact]
    public void StressFitColour_isComfortableInsideEnvelopeAndPolarisedOutside()
    {
        Assert.Equal(SimulationPalette.Comfortable, SimulationPalette.StressFitColour(20, thermalCenter: 20, thermalWidth: 10));

        Color cold = SimulationPalette.StressFitColour(-20, 20, 10);
        Color hot = SimulationPalette.StressFitColour(60, 20, 10);
        Assert.True(cold.B > cold.R, "Too cold should read blue.");
        Assert.True(hot.R > hot.B, "Too hot should read red.");
    }

    [Fact]
    public void LineageColour_isStablePerIdAndVariesAcrossIds()
    {
        Assert.Equal(LineageColour.ForLineage(5), LineageColour.ForLineage(5));
        Assert.NotEqual(LineageColour.ForLineage(5), LineageColour.ForLineage(6));
        Assert.NotEqual(LineageColour.ForLineage(1), LineageColour.ForLineage(100));
    }

    [Fact]
    public void BiomeTile_blightDesaturates_andAnomalyTintsWarmOrCool()
    {
        Color plain = BiomeColours.Tile(Biome.Grassland, 20, 20, blightActive: false, temperatureOffset: 0);
        Color blighted = BiomeColours.Tile(Biome.Grassland, 20, 20, blightActive: true, temperatureOffset: 0);
        Color heat = BiomeColours.Tile(Biome.Grassland, 20, 20, blightActive: false, temperatureOffset: 20);
        Color cold = BiomeColours.Tile(Biome.Grassland, 20, 20, blightActive: false, temperatureOffset: -20);

        Assert.NotEqual(plain, blighted);
        Assert.True(heat.R > plain.R, "Heatwave should warm the tile.");
        Assert.True(cold.B > plain.B, "Ice age should cool the tile.");
    }

    [Theory]
    [InlineData(OrganismAction.MoveNorth, ActionResult.Success)]
    [InlineData(OrganismAction.MoveWest, ActionResult.Blocked)]
    public void Outline_moveActionsAreBlue(OrganismAction action, ActionResult result) =>
        Assert.Equal(SimulationPalette.Move, OrganismColours.Outline(action, result));

    [Fact]
    public void Outline_distinguishesPredationFromGrazing()
    {
        Assert.Equal(SimulationPalette.Predation, OrganismColours.Outline(OrganismAction.HarvestNorth, ActionResult.Killed));
        Assert.Equal(SimulationPalette.Graze, OrganismColours.Outline(OrganismAction.HarvestNorth, ActionResult.Success));
        Assert.Equal(SimulationPalette.Reproduce, OrganismColours.Outline(OrganismAction.Reproduce, ActionResult.Success));
        Assert.Equal(SimulationPalette.Idle, OrganismColours.Outline(null, ActionResult.None));
    }

    [Fact]
    public void Radius_scalesMonotonicallyWithSizeWithinBounds()
    {
        var bounds = new TraitBounds.Range(0.5, 10.0);
        double small = OrganismColours.Radius(0.5, bounds);
        double large = OrganismColours.Radius(10.0, bounds);
        double mid = OrganismColours.Radius(5.25, bounds);
        Assert.True(small < mid && mid < large);
        Assert.Equal(0.18, small, precision: 6);
        Assert.Equal(0.48, large, precision: 6);
    }

    [Fact]
    public void Fill_selectsPerColourMode()
    {
        OrganismSnapshot org = Organism(energy: 100, lastAction: OrganismAction.MoveNorth);
        Assert.Equal(SimulationPalette.Move, OrganismColours.Fill(ColourMode.Action, org, 1, 100, 20));
        Assert.Equal(SimulationPalette.EnergyColour(100, 100), OrganismColours.Fill(ColourMode.Energy, org, 1, 100, 20));
        Assert.Equal(LineageColour.ForLineage(42), OrganismColours.Fill(ColourMode.Lineage, org, 42, 100, 20));
    }

    [Fact]
    public void CooperationColour_onlyMarksSharesThatActuallyTransferred()
    {
        // A share that landed (had a neighbour, passed the roll) reads as cooperating…
        OrganismSnapshot shared = Organism(lastAction: OrganismAction.ShareNorth, result: ActionResult.Success);
        Assert.Equal(SimulationPalette.Share, OrganismColours.Fill(ColourMode.Cooperation, shared, 1, 100, 20));

        // …but a share *attempted* with no neighbour (no-op) must not — otherwise isolated organisms
        // look like they cooperated.
        OrganismSnapshot attempted = Organism(lastAction: OrganismAction.ShareNorth, result: ActionResult.NoOp);
        Assert.Equal(SimulationPalette.Neutral, OrganismColours.Fill(ColourMode.Cooperation, attempted, 1, 100, 20));
    }

    [Fact]
    public void Legend_everyModeProducesFillActionAndBiomeSections()
    {
        foreach (ColourMode mode in Enum.GetValues<ColourMode>())
        {
            IReadOnlyList<LegendSection> legend = LegendBuilder.Build(mode);
            Assert.Contains(legend, s => s.Title.StartsWith("Fill", StringComparison.Ordinal));
            Assert.Contains(legend, s => s.Title.Contains("Last action", StringComparison.Ordinal));
            Assert.Contains(legend, s => s.Title.StartsWith("Biomes", StringComparison.Ordinal) && s.Entries.Count == 4);
        }
    }

    [Fact]
    public void NeatGraphLayout_placesInputsLeftOutputsRight_andFlagsNoRecurrenceInAGenesisBrain()
    {
        NeatGenome brain = NeatGenomeFactory.CreateMinimalFullyConnected(new Prng(1));
        NeatGraph graph = NeatGraphLayout.Build(brain);

        Assert.Equal(brain.Nodes.Count, graph.Nodes.Count);
        Assert.All(graph.Nodes, n => Assert.InRange(n.X, 0.0, 1.0));
        Assert.All(graph.Nodes, n => Assert.InRange(n.Y, 0.0, 1.0));
        Assert.All(graph.Nodes.Where(n => n.Type == NodeType.Input), n => Assert.Equal(0.0, n.X));
        Assert.All(graph.Nodes.Where(n => n.Type == NodeType.Output), n => Assert.Equal(1.0, n.X));
        Assert.All(graph.Edges, e => Assert.False(e.Recurrent)); // input→output is feed-forward
    }

    [Fact]
    public void NeatGraphLayout_placesChainedHiddenNodesInSeparateDepthColumns()
    {
        // input → h2 → h3 → output: the two hidden nodes are at different feed-forward depths, so the
        // effective-network layout must put them in distinct columns and report depth 3.
        var brain = new NeatGenome
        {
            Nodes =
            [
                new NodeGene { Id = 0, Type = NodeType.Input },
                new NodeGene { Id = 1, Type = NodeType.Output },
                new NodeGene { Id = 2, Type = NodeType.Hidden },
                new NodeGene { Id = 3, Type = NodeType.Hidden },
            ],
            Connections =
            [
                new ConnectionGene { InnovationId = 1, From = 0, To = 2, Weight = 0.5, Enabled = true },
                new ConnectionGene { InnovationId = 2, From = 2, To = 3, Weight = 0.5, Enabled = true },
                new ConnectionGene { InnovationId = 3, From = 3, To = 1, Weight = 0.5, Enabled = true },
            ],
        };

        NeatGraph graph = NeatGraphLayout.Build(brain);

        Assert.Equal(3, graph.Depth);
        double h2 = graph.Nodes.First(n => n.Id == 2).X;
        double h3 = graph.Nodes.First(n => n.Id == 3).X;
        Assert.True(h2 < h3, "A deeper hidden node should sit further right.");
        Assert.All(graph.Edges, e => Assert.False(e.Recurrent)); // the whole chain is feed-forward
    }

    [Fact]
    public void NeatGraphLayout_flagsBackwardEdgesAsRecurrent()
    {
        var brain = new NeatGenome
        {
            Nodes =
            [
                new NodeGene { Id = 0, Type = NodeType.Input },
                new NodeGene { Id = 1, Type = NodeType.Output },
                new NodeGene { Id = 2, Type = NodeType.Hidden },
            ],
            Connections = [new ConnectionGene { InnovationId = 1, From = 1, To = 2, Weight = 0.5, Enabled = true }], // output → hidden
        };

        NeatGraph graph = NeatGraphLayout.Build(brain);
        Assert.Single(graph.Edges);
        Assert.True(graph.Edges[0].Recurrent);
    }
}
