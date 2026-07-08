using LifeSim.App.Presentation;
using LifeSim.App.ViewModels;
using LifeSim.Core.Configuration;
using LifeSim.Core.Events;
using LifeSim.Core.Simulation;
using LifeSim.Core.Snapshot;
using LifeSim.Core.World;

namespace LifeSim.App.Tests;

public class WorldViewTests
{
    private static WorldSnapshot BuildSnapshot(int ticks = 8, ulong seed = 42)
    {
        var config = SimulationConfig.Default with { InitialPopulation = 30 };
        var world = SimulationWorld.CreateGenesis(new WorldState { Seed = seed, Width = 48, Height = 48 }, config);
        for (int i = 0; i < ticks && !world.Extinct; i++)
        {
            world.Advance();
        }

        return world.ToSnapshot();
    }

    [Fact]
    public void GlobalStatistics_summarisesTheSnapshotIntoSections()
    {
        WorldSnapshot snapshot = BuildSnapshot();
        IReadOnlyList<StatSection> sections = GlobalStatistics.Build(snapshot);

        Assert.Contains(sections, s => s.Title == "Overview");
        Assert.Contains(sections, s => s.Title == "Multicellularity");   // on by default
        Assert.Contains(sections, s => s.Title == "Population by biome");

        StatSection overview = sections.First(s => s.Title == "Overview");
        StatRow population = overview.Rows.First(r => r.Label == "Population");
        Assert.Equal(snapshot.Organisms.Count.ToString(), population.Value);
    }

    [Fact]
    public void ToggleStatistics_opensAndPopulatesThePanel()
    {
        var vm = new WorldViewModel();
        vm.LoadSnapshot(BuildSnapshot());

        Assert.False(vm.IsStatisticsVisible);
        vm.ToggleStatisticsCommand.Execute(null);

        Assert.True(vm.IsStatisticsVisible);
        Assert.NotEmpty(vm.Statistics);

        vm.ToggleStatisticsCommand.Execute(null);
        Assert.False(vm.IsStatisticsVisible);
    }

    [Fact]
    public void NotificationsToggle_gatesWhetherEventsAreRaised_withoutBacklog()
    {
        static WorldSnapshot Frame(long tick, EventType? evt) => new()
        {
            Tick = tick,
            World = new WorldState { Seed = 42, Width = 32, Height = 32 },
            Configuration = SimulationConfig.Default,
            Metrics = new SimulationMetrics(),
            EnvironmentModifiers = evt is { } e ? [new EnvironmentModifier { Type = e, RemainingTicks = 5 }] : [],
        };

        var vm = new WorldViewModel();
        int count = 0;
        vm.NotificationRaised += _ => count++;

        // Disabled: the blight onset is folded into the watcher but not surfaced.
        vm.NotificationsEnabled = false;
        vm.LoadSnapshot(Frame(0, null));                 // seed
        vm.LoadSnapshot(Frame(1, EventType.ResourceBlight));
        Assert.Equal(0, count);

        // Re-enabled: only a NEW transition fires (blight lifting) — no backlog of the suppressed onset.
        vm.NotificationsEnabled = true;
        vm.LoadSnapshot(Frame(2, null));                 // blight ends
        Assert.True(count > 0);
    }

    [Fact]
    public void OrganismList_listsAliveOrganisms_sortedByTheChosenKeyAndDirection()
    {
        WorldSnapshot snap = BuildSnapshot(ticks: 30, seed: 909090);
        Assert.NotEmpty(snap.Organisms);

        IReadOnlyList<OrganismRow> byAgeDesc = OrganismListBuilder.Build(snap, OrganismSortKey.Age, ascending: false);
        Assert.Equal(snap.Organisms.Count, byAgeDesc.Count);
        for (int i = 1; i < byAgeDesc.Count; i++)
        {
            Assert.True(byAgeDesc[i - 1].Age >= byAgeDesc[i].Age, "Age-descending order violated.");
        }

        IReadOnlyList<OrganismRow> byNodesAsc = OrganismListBuilder.Build(snap, OrganismSortKey.BrainNodes, ascending: true);
        for (int i = 1; i < byNodesAsc.Count; i++)
        {
            Assert.True(byNodesAsc[i - 1].BrainNodes <= byNodesAsc[i].BrainNodes, "Brain-nodes-ascending order violated.");
        }
    }

    [Fact]
    public void OrganismsTab_populatesLive_andClickingARowJumpsToItsStats()
    {
        var vm = new WorldViewModel();
        vm.LoadSnapshot(BuildSnapshot());

        vm.SidebarTabIndex = 2; // Organisms tab
        Assert.NotEmpty(vm.OrganismList);

        OrganismRow row = vm.OrganismList[0];
        vm.SelectedOrganismRow = row;

        Assert.Equal(0, vm.SidebarTabIndex); // jumped to the Info tab (its stats)
        Assert.Equal(row.OrganismId, vm.SelectedOrganismId);
        Assert.NotNull(vm.Inspector);
        Assert.Equal(row.OrganismId, vm.Inspector!.OrganismId);
    }

    [Fact]
    public void ActiveEvents_labelsRunningEvents_forTheStatusBar()
    {
        static WorldSnapshot Frame(params EnvironmentModifier[] events) => new()
        {
            World = new WorldState { Seed = 1, Width = 16, Height = 16 },
            Configuration = SimulationConfig.Default,
            Metrics = new SimulationMetrics(),
            EnvironmentModifiers = [.. events],
        };

        var vm = new WorldViewModel();
        vm.LoadSnapshot(Frame(
            new EnvironmentModifier { Type = EventType.ClimaticAnomaly, Magnitude = -20, RemainingTicks = 42 },
            new EnvironmentModifier { Type = EventType.ResourceBlight, RemainingTicks = 10 }));

        Assert.Contains(vm.ActiveEvents, e => e.Contains("Ice age") && e.Contains("42"));
        Assert.Contains(vm.ActiveEvents, e => e.Contains("Blight"));

        // A warming anomaly reads as a heatwave, not an ice age.
        vm.LoadSnapshot(Frame(new EnvironmentModifier { Type = EventType.ClimaticAnomaly, Magnitude = 20, RemainingTicks = 5 }));
        Assert.Contains(vm.ActiveEvents, e => e.Contains("Heatwave"));

        // No events → nothing to show.
        vm.LoadSnapshot(Frame());
        Assert.Empty(vm.ActiveEvents);
    }

    [Fact]
    public void FocusOrganism_selectsItAndSwitchesToTheInfoTab()
    {
        WorldSnapshot snapshot = BuildSnapshot();
        var vm = new WorldViewModel();
        vm.LoadSnapshot(snapshot);
        vm.SidebarTabIndex = 1; // start on the Ranking tab
        long id = snapshot.Organisms[0].OrganismId;

        vm.FocusOrganism(id); // what a clickable notification invokes

        Assert.Equal(0, vm.SidebarTabIndex); // Info tab, where the inspector lives
        Assert.Equal(id, vm.SelectedOrganismId);
        Assert.NotNull(vm.Inspector);
        Assert.Equal(id, vm.Inspector!.OrganismId);
    }

    [Fact]
    public void WorldScene_buildsOneViewPerOrganism_andReturnsTileColours()
    {
        WorldSnapshot snapshot = BuildSnapshot();
        WorldScene scene = WorldScene.FromSnapshot(snapshot, ColourMode.Energy, selectedId: null);

        Assert.Equal(snapshot.Organisms.Count, scene.Organisms.Count);
        Assert.Equal(snapshot.World.Width, scene.Width);
        _ = scene.TileColour(0, 0); // does not throw; a colour is produced for any in-world tile
        Assert.Null(scene.SelectedFootprint);
    }

    [Fact]
    public void WorldScene_isIdenticalFromALiveSnapshotAndAReloadedOne()
    {
        // The Phase 13 exit criterion: the shared views render identically whether fed a live Core
        // frame or a deserialized snapshot. Both go through the same WorldScene producer.
        WorldSnapshot live = BuildSnapshot();
        WorldSnapshot reloaded = SnapshotSerializer.Load(SnapshotSerializer.Save(live));
        long selected = live.Organisms[0].OrganismId;

        foreach (ColourMode mode in Enum.GetValues<ColourMode>())
        {
            WorldScene fromLive = WorldScene.FromSnapshot(live, mode, selected);
            WorldScene fromReloaded = WorldScene.FromSnapshot(reloaded, mode, selected);

            Assert.Equal(fromLive.Organisms, fromReloaded.Organisms); // OrganismView is a record → value equality
            Assert.Equal(fromLive.SelectedFootprint, fromReloaded.SelectedFootprint);
            for (int y = 0; y < fromLive.Height; y += 7)
            {
                for (int x = 0; x < fromLive.Width; x += 7)
                {
                    Assert.Equal(fromLive.TileColour(x, y), fromReloaded.TileColour(x, y));
                }
            }
        }
    }

    [Fact]
    public void WorldScene_setsTheSelectedOrganismFootprint()
    {
        WorldSnapshot snapshot = BuildSnapshot();
        long id = snapshot.Organisms[0].OrganismId;

        WorldScene scene = WorldScene.FromSnapshot(snapshot, ColourMode.Energy, id);
        Assert.NotNull(scene.SelectedFootprint);
    }

    [Fact]
    public void Inspector_populatesEveryStatBlockForARealOrganism()
    {
        WorldSnapshot snapshot = BuildSnapshot();
        OrganismSnapshot organism = snapshot.Organisms[0];

        OrganismInspectorViewModel? inspector = OrganismInspectorViewModel.Create(snapshot, organism.OrganismId);

        Assert.NotNull(inspector);
        Assert.Equal(organism.Name, inspector.Name);
        Assert.Equal(15, inspector.Traits.Count); // + Sexuality
        // Founders start at Sexuality 0, so the derived mode is never sexual/mixed here.
        Assert.True(inspector.ReproductionMode is "Asexual (clones)" or "Sterile (soma)", inspector.ReproductionMode);

        // Body composition stats (multicellularity): cell count + one entry per cell type.
        Assert.True(inspector.CellCount >= 1.0);
        Assert.Equal(6, inspector.CellComposition.Count);
        Assert.Equal(new[] { "Germ", "Feeder", "Store", "Defender", "Mover", "Sensor" }, inspector.CellComposition.Select(c => c.Type));
        Assert.Equal(inspector.CellCount, inspector.CellComposition.Sum(c => c.Cells), precision: 6);

        Assert.Equal(15, inspector.ActionProbabilities.Count); // 4 move + 5 harvest + idle + reproduce + 4 share
        Assert.Equal(1.0, inspector.ActionProbabilities.Sum(p => p.Probability), precision: 6);
        Assert.Equal(organism.Brain.Nodes.Count, inspector.BrainGraph.Nodes.Count);
        Assert.Equal(
            inspector.Economy.Base + inspector.Economy.ThermalStress + inspector.Economy.SensoryTax + inspector.Economy.LastMovementCost,
            inspector.Economy.Total,
            precision: 9);
    }

    [Fact]
    public void Inspector_reportsChildCountAndLivingParentName()
    {
        // Run long enough that reproduction produces living parents and living children.
        WorldSnapshot snapshot = BuildSnapshot(ticks: 60, seed: 909090);

        var livingIds = snapshot.Organisms.Select(o => o.OrganismId).ToHashSet();
        LifeSim.Core.Snapshot.LineageSnapshot? pair = snapshot.Lineages.FirstOrDefault(
            l => l.ParentId is { } p && livingIds.Contains(p) && livingIds.Contains(l.OrganismId));
        Assert.NotNull(pair); // a living child of a living parent should exist after 60 ticks

        long parentId = pair.ParentId!.Value;
        string parentName = snapshot.Organisms.Single(o => o.OrganismId == parentId).Name;

        // From the child: its parent is identified, named, and alive.
        OrganismInspectorViewModel child = OrganismInspectorViewModel.Create(snapshot, pair.OrganismId)!;
        Assert.Equal(parentId, child.ParentId);
        Assert.Equal(parentName, child.ParentName);
        Assert.True(child.ParentAlive);

        // From the parent: the child count matches the lineage records naming it as parent.
        OrganismInspectorViewModel parent = OrganismInspectorViewModel.Create(snapshot, parentId)!;
        Assert.Equal(snapshot.Lineages.Count(l => l.ParentId == parentId), parent.ChildCount);
        Assert.True(parent.ChildCount >= 1);
    }

    [Fact]
    public void Inspector_returnsNullForAnUnknownOrganism()
    {
        WorldSnapshot snapshot = BuildSnapshot();
        Assert.Null(OrganismInspectorViewModel.Create(snapshot, organismId: -1));
    }

    [Fact]
    public void Ranking_isOrderedByDescendantScore_namesEveryone_andCoversTheWholeSimulation()
    {
        WorldSnapshot snapshot = BuildSnapshot(ticks: 60, seed: 909090);
        IReadOnlyList<RankingEntry> ranking = RankingBuilder.Build(snapshot);

        Assert.NotEmpty(ranking);
        Assert.Equal(1, ranking[0].Rank);
        for (int i = 1; i < ranking.Count; i++)
        {
            Assert.True(ranking[i].Score <= ranking[i - 1].Score); // most → least successful
        }

        Assert.Contains(ranking, r => !r.IsAlive);            // includes dead organisms
        Assert.All(ranking, r => Assert.False(string.IsNullOrEmpty(r.Name))); // everyone is named (alive or dead)

        // Living entries' names match the name stored on the live organism (both from OrganismNamer).
        var livingNames = snapshot.Organisms.ToDictionary(o => o.OrganismId, o => o.Name);
        foreach (RankingEntry entry in ranking.Where(r => r.IsAlive))
        {
            Assert.Equal(livingNames[entry.OrganismId], entry.Name);
        }
    }

    [Fact]
    public void Ranking_scoreWeightsDescendantsByGeneration()
    {
        // Hand-built lineage: root → child → grandchild → great-grandchild (single chain), plus a
        // second direct child of root. Root score = 2 children*1 + 1 grandchild*0.5 + 1 gg*0.25 = 2.75.
        var lineages = new List<LifeSim.Core.Snapshot.LineageSnapshot>
        {
            new() { OrganismId = 1, ParentId = null, LineageId = 1 },
            new() { OrganismId = 2, ParentId = 1, LineageId = 1 },
            new() { OrganismId = 3, ParentId = 1, LineageId = 1 },
            new() { OrganismId = 4, ParentId = 2, LineageId = 1 },
            new() { OrganismId = 5, ParentId = 4, LineageId = 1 },
        };
        var snapshot = new WorldSnapshot { Lineages = lineages };

        // The weighted-descendant component: 2 children*1 + 1 grandchild*0.5 + 1 gg*0.25 = 2.75.
        (Dictionary<long, double> descendants, Dictionary<long, long> children) = LineageScore.Lineage(snapshot);
        Assert.Equal(2.75, descendants[1], precision: 6);
        Assert.Equal(2, children[1]);

        IReadOnlyList<RankingEntry> ranking = RankingBuilder.Build(snapshot);
        Assert.Equal(2, ranking.Single(r => r.OrganismId == 1).Children);
        Assert.Equal(1, ranking[0].OrganismId); // root (most descendants + highest rate) ranks first
    }

    [Fact]
    public void LineageScore_offspringRate_beatsAHigherButSlowerBrood()
    {
        // 10 offspring in 50 ticks vs 15 offspring in 200 ticks: the faster breeder wins despite fewer
        // total offspring (reproductive rate dominates a larger, slower brood).
        double fast = LineageScore.Score(weightedDescendants: 10, directChildren: 10, lifespan: 50);
        double slow = LineageScore.Score(weightedDescendants: 15, directChildren: 15, lifespan: 200);
        Assert.True(fast > slow, $"fast {fast} should beat slow {slow}");
    }

    [Fact]
    public void LineageScore_longevity_breaksTiesWhenReproductionIsEqual()
    {
        // With reproduction equal (here none), a longer life scores higher.
        Assert.True(
            LineageScore.Score(0, 0, lifespan: 200) > LineageScore.Score(0, 0, lifespan: 100),
            "a longer lifespan should outscore a shorter one when offspring are equal");
    }

    [Fact]
    public void LineageScore_offspring_outweighLongevity()
    {
        // A single child is worth more than a very long childless life.
        double oneChild = LineageScore.Score(weightedDescendants: 1, directChildren: 1, lifespan: 100);
        double longLife = LineageScore.Score(weightedDescendants: 0, directChildren: 0, lifespan: 1000);
        Assert.True(oneChild > longLife, $"one child {oneChild} should outweigh a long childless life {longLife}");
    }

    [Fact]
    public void WorldViewModel_rankingTab_populatesAndSelectionShowsTheRightDetail()
    {
        WorldSnapshot snapshot = BuildSnapshot(ticks: 60, seed: 909090);
        var vm = new WorldViewModel();
        vm.LoadSnapshot(snapshot);

        Assert.Empty(vm.Ranking);   // not computed until the Ranking tab opens
        vm.SidebarTabIndex = 1;
        Assert.NotEmpty(vm.Ranking);

        RankingEntry living = vm.Ranking.First(r => r.IsAlive);
        vm.SelectedRankingEntry = living;
        Assert.IsType<OrganismInspectorViewModel>(vm.RankingDetail); // full stats for a living one
        Assert.Equal(living.OrganismId, vm.SelectedOrganismId);      // and it's selected on the map

        RankingEntry? dead = vm.Ranking.FirstOrDefault(r => !r.IsAlive);
        Assert.NotNull(dead);
        vm.SelectedRankingEntry = dead;
        Assert.IsType<LineageDetailViewModel>(vm.RankingDetail);     // lineage stats for a dead one
    }

    [Fact]
    public void WorldViewModel_ranking_autoRefreshesOnNewFrames_andPreservesSelection()
    {
        // Same run at two points in time (seed fixed): the later snapshot is the earlier advanced on.
        WorldSnapshot early = BuildSnapshot(ticks: 30, seed: 909090);
        WorldSnapshot later = BuildSnapshot(ticks: 60, seed: 909090);

        var vm = new WorldViewModel();
        vm.LoadSnapshot(early);
        vm.SidebarTabIndex = 1;
        RankingEntry pick = vm.Ranking[0];
        vm.SelectedRankingEntry = pick;

        vm.LoadSnapshot(later); // a new frame arrives while the Ranking tab is open

        Assert.Equal(Math.Min(500, later.Lineages.Count), vm.Ranking.Count); // auto-refreshed to the new frame
        Assert.Equal(pick.OrganismId, vm.SelectedRankingEntry!.OrganismId);  // selection preserved by id
    }

    [Fact]
    public void LineageDetail_forADeadOrganism_carriesBirthAndDeathTraits()
    {
        WorldSnapshot snapshot = BuildSnapshot(ticks: 60, seed: 909090);
        var livingIds = snapshot.Organisms.Select(o => o.OrganismId).ToHashSet();
        LifeSim.Core.Snapshot.LineageSnapshot? dead = snapshot.Lineages.FirstOrDefault(
            l => l.DeathTick is not null && !livingIds.Contains(l.OrganismId));
        Assert.NotNull(dead);

        LineageDetailViewModel detail = LineageDetailViewModel.Create(snapshot, dead.OrganismId)!;
        Assert.False(detail.IsAlive);
        Assert.NotNull(detail.DeathTick);
        Assert.Equal(15, detail.BirthTraits.Count);
        Assert.True(detail.HasDeathTraits);
        Assert.Equal(15, detail.DeathTraits.Count);
    }

    [Fact]
    public void WorldViewModel_viewLineageGraph_opensForTheSelectedOrganism()
    {
        WorldSnapshot snapshot = BuildSnapshot(ticks: 40, seed: 909090);
        var vm = new WorldViewModel();
        vm.LoadSnapshot(snapshot);

        Assert.False(vm.CanViewLineageGraph);            // nothing selected yet
        Assert.False(vm.IsLineageGraphVisible);

        long id = snapshot.Organisms[0].OrganismId;
        vm.SelectedOrganismId = id;
        Assert.True(vm.CanViewLineageGraph);

        vm.ViewLineageGraphCommand.Execute(null);
        Assert.True(vm.IsLineageGraphVisible);
        Assert.NotNull(vm.LineageGraph);
        Assert.Contains(vm.LineageGraph!.Nodes, n => n.IsFocus && n.OrganismId == id);
        Assert.Contains("Lineage of", vm.LineageGraphTitle, StringComparison.Ordinal);

        vm.CloseLineageGraphCommand.Execute(null);
        Assert.False(vm.IsLineageGraphVisible);
    }

    [Fact]
    public void WorldViewModel_currentOrganism_followsTheRankingSelectionOnTheRankingTab()
    {
        WorldSnapshot snapshot = BuildSnapshot(ticks: 40, seed: 909090);
        var vm = new WorldViewModel();
        vm.LoadSnapshot(snapshot);
        vm.SidebarTabIndex = 1;

        RankingEntry entry = vm.Ranking[0];
        vm.SelectedRankingEntry = entry;

        Assert.Equal(entry.OrganismId, vm.CurrentOrganismId);
        Assert.True(vm.CanViewLineageGraph);
    }

    [Fact]
    public void WorldViewModel_reactsToSnapshotModeAndSelection()
    {
        WorldSnapshot snapshot = BuildSnapshot();
        var vm = new WorldViewModel();

        vm.LoadSnapshot(snapshot);
        Assert.NotNull(vm.Scene);
        Assert.Equal(snapshot.Tick, vm.Tick);
        Assert.Equal(snapshot.Organisms.Count, vm.Population);

        vm.ColourMode = ColourMode.Lineage;
        Assert.Contains(vm.Legend, s => s.Title.Contains("Lineage", StringComparison.Ordinal));

        vm.SelectedOrganismId = snapshot.Organisms[0].OrganismId;
        Assert.True(vm.HasSelection);
        Assert.NotNull(vm.Inspector);

        vm.SelectedOrganismId = null;
        Assert.False(vm.HasSelection);
        Assert.Null(vm.Inspector);
    }
}
