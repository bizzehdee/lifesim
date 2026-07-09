using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LifeSim.App.Presentation;
using LifeSim.Core.Events;
using LifeSim.Core.Naming;
using LifeSim.Core.Snapshot;

namespace LifeSim.App.ViewModels;

/// <summary>
/// The shared world view-model. It is agnostic to its data source: feed it a
/// snapshot from a live in-process Core (<c>world.ToSnapshot()</c>) or a deserialized file and it
/// renders identically. Holds the active colour mode and selection, and rebuilds the render
/// <see cref="Scene"/>, <see cref="Legend"/>, and <see cref="Inspector"/> as those change.
/// </summary>
public partial class WorldViewModel : ViewModelBase
{
    [ObservableProperty]
    private WorldSnapshot? _snapshot;

    [ObservableProperty]
    private ColourMode _colourMode = ColourMode.Energy;

    [ObservableProperty]
    private long? _selectedOrganismId;

    [ObservableProperty]
    private WorldScene? _scene;

    [ObservableProperty]
    private OrganismInspectorViewModel? _inspector;

    [ObservableProperty]
    private IReadOnlyList<LegendSection> _legend = LegendBuilder.Build(ColourMode.Energy);

    // --- Ranking (whole-simulation leaderboard, alive or dead). ---
    [ObservableProperty]
    private IReadOnlyList<RankingEntry> _ranking = [];

    [ObservableProperty]
    private RankingEntry? _selectedRankingEntry;

    /// <summary>Detail for the selected ranking entry: the full inspector (alive) or lineage stats (dead).</summary>
    [ObservableProperty]
    private ViewModelBase? _rankingDetail;

    // --- Organisms tab (currently-alive organisms, sortable). ---
    [ObservableProperty]
    private IReadOnlyList<OrganismRow> _organismList = [];

    [ObservableProperty]
    private OrganismSortKey _organismSortKey = OrganismSortKey.Score;

    [ObservableProperty]
    private bool _organismSortDescending = true;

    [ObservableProperty]
    private OrganismRow? _selectedOrganismRow;

    /// <summary>The sort options offered by the Organisms tab.</summary>
    public IReadOnlyList<OrganismSortKey> OrganismSortKeys { get; } = Enum.GetValues<OrganismSortKey>();

    /// <summary>0 = Info tab, 1 = Ranking tab, 2 = Organisms tab. Each list is (re)built when its tab is open, to avoid per-frame cost.</summary>
    [ObservableProperty]
    private int _sidebarTabIndex;

    // --- Lineage graph overlay for the currently selected organism (map or ranking). ---
    [ObservableProperty]
    private LineageGraph? _lineageGraph;

    [ObservableProperty]
    private bool _isLineageGraphVisible;

    [ObservableProperty]
    private string _lineageGraphTitle = "";

    /// <summary>Whether the global-statistics panel is open.</summary>
    [ObservableProperty]
    private bool _isStatisticsVisible;

    /// <summary>World-level statistics for the open panel, rebuilt from each frame.</summary>
    [ObservableProperty]
    private IReadOnlyList<StatSection> _statistics = [];

    /// <summary>Whether the colour-mode + legend panel is open (behind a button, like statistics).</summary>
    [ObservableProperty]
    private bool _isLegendVisible;

    /// <summary>Compact "at a glance" headline stats shown in the Info sidebar, refreshed every frame.</summary>
    [ObservableProperty]
    private IReadOnlyList<StatRow> _glance = [];

    /// <summary>How many ancestor generations to show above the focus organism (default 3).</summary>
    [ObservableProperty]
    private decimal _maxParentGens = 3;

    /// <summary>How many descendant generations to show below the focus organism (default 3).</summary>
    [ObservableProperty]
    private decimal _maxChildGens = 3;

    private long? _lineageFocusId;

    /// <summary>All colour modes, for the mode selector.</summary>
    public IReadOnlyList<ColourMode> ColourModes { get; } = Enum.GetValues<ColourMode>();

    public long Tick => Snapshot?.Tick ?? 0;

    public int Population => Snapshot?.Organisms.Count ?? 0;

    public bool Extinct => Snapshot?.Metrics?.Extinct ?? false;

    /// <summary>Labels for any active world events (blight, plague, ice age / heatwave), shown in the Info panel.</summary>
    public IReadOnlyList<string> ActiveEvents =>
        Snapshot is null ? [] : [.. Snapshot.EnvironmentModifiers.Select(EventLabel)];

    /// <summary>Whether any world event is active — gates the Info panel's events block.</summary>
    public bool HasActiveEvents => ActiveEvents.Count > 0;

    private static string EventLabel(EnvironmentModifier modifier)
    {
        string name = modifier.Type switch
        {
            EventType.ResourceBlight => "🥀 Blight",
            EventType.DensityPlague => "☣ Plague",
            EventType.ClimaticAnomaly => modifier.Magnitude < 0 ? "❄ Ice age" : "🔥 Heatwave",
            _ => modifier.Type.ToString(),
        };
        return $"{name} · {modifier.RemainingTicks}t";
    }

    public bool HasSelection => Inspector is not null;

    // Founding-type composition-over-time: accumulated every frame (independent of the stats overlay
    // being open) so opening it shows the trend, and reset when a new/loaded world rewinds the tick.
    private const int MaxFoundingHistory = 300;
    private readonly List<FoundingTypeSample> _foundingHistory = [];
    private long _lastFoundingTick = long.MinValue;

    /// <summary>The live scoreboard: each seeded brain type's current headcount and share, biggest first.</summary>
    [ObservableProperty]
    private IReadOnlyList<FoundingTypeStanding> _foundingTypeStandings = [];

    /// <summary>Stacked-area chart of each type's population share over recent frames (null until there's a trend).</summary>
    [ObservableProperty]
    private CompositionChart? _foundingTypeChart;

    /// <summary>True when the breakdown is worth showing — more than one type, or a single non-generic type.</summary>
    [ObservableProperty]
    private bool _hasFoundingTypeBreakdown;

    // Kin-selection trend: cumulative kin-directed vs non-kin shares over time. Reuses the composition
    // chart (two bands) — a rising kin-directed band means sharing is becoming kin-biased (kin selection).
    private readonly List<FoundingTypeSample> _shareHistory = [];
    private long _cumulativeKinShares;
    private long _cumulativeNonKinShares;
    private long _lastShareTick = long.MinValue;

    /// <summary>Stacked-area chart of cumulative kin-directed vs non-kin shares over time (null until sharing happens).</summary>
    [ObservableProperty]
    private CompositionChart? _sharingTrendChart;

    /// <summary>True once any sharing has occurred — gates the kin-selection trend view.</summary>
    [ObservableProperty]
    private bool _hasSharingTrend;

    /// <summary>Legend swatches for the sharing-trend chart, from the same colour map as its bands.</summary>
    public IBrush KinShareSwatch { get; } = FoundingTypeComposition.Brush("kin-directed");

    public IBrush NonKinShareSwatch { get; } = FoundingTypeComposition.Brush("non-kin");

    private readonly EventWatcher _eventWatcher = new();

    /// <summary>Whether in-app notifications are shown; toggleable live during a run. When off, frames are still folded into the watcher (so no backlog piles up), but nothing is raised.</summary>
    [ObservableProperty]
    private bool _notificationsEnabled = true;

    /// <summary>Raised (on the UI thread) when a frame reveals a notable event — blight, population explosion, a new multicellular milestone.</summary>
    public event Action<SimNotification>? NotificationRaised;

    /// <summary>Forget notification history so the next frame re-seeds baselines silently (call when a new world is adopted).</summary>
    public void ResetNotifications() => _eventWatcher.Reset();

    // Heavy sidebar lists (Ranking, Organisms) rebuild the whole collection and force the ListBox to
    // recreate its item containers — far costlier than the map scene. At a fast tick rate, doing that
    // every frame saturates the UI thread and starves input. While the sim is playing we therefore cap
    // these rebuilds to a few per second; when paused/stepping (ThrottleLiveLists = false) every frame
    // refreshes immediately, and user actions (tab switch, sort change, manual refresh) always do too.
    private const long ListRefreshIntervalMs = 250;
    private long _lastRankingRefreshMs = long.MinValue;
    private long _lastOrganismRefreshMs = long.MinValue;

    /// <summary>
    /// Set by the session while the sim is playing. When true, the Ranking/Organisms lists refresh at a
    /// capped rate rather than every frame so a fast tick rate can't monopolise the UI thread; when false
    /// (paused, stepping, a static loaded snapshot) each frame refreshes them immediately.
    /// </summary>
    public bool ThrottleLiveLists { get; set; }

    private static bool DueForRefresh(ref long lastMs)
    {
        long now = Environment.TickCount64;
        if (now - lastMs < ListRefreshIntervalMs)
        {
            return false;
        }

        lastMs = now;
        return true;
    }

    /// <summary>Feed a new frame from either a live Core or a loaded snapshot.</summary>
    public void LoadSnapshot(WorldSnapshot snapshot) => Snapshot = snapshot;

    [RelayCommand]
    private void SelectOrganism(long? organismId) => SelectedOrganismId = organismId;

    /// <summary>Select an organism as if it were clicked on the map — used by clickable notifications. Switches to the Info tab so its inspector is visible.</summary>
    public void FocusOrganism(long organismId)
    {
        SidebarTabIndex = 0; // the Info tab hosts the inspector
        SelectedOrganismId = organismId;
    }

    [RelayCommand]
    private void ClearSelection() => SelectedOrganismId = null;

    /// <summary>The organism the sidebar is focused on: the ranking selection when the Ranking tab is open, else the map selection.</summary>
    public long? CurrentOrganismId => SidebarTabIndex == 1 && SelectedRankingEntry is { } r ? r.OrganismId : SelectedOrganismId;

    public bool CanViewLineageGraph => CurrentOrganismId is not null;

    [RelayCommand]
    private void ViewLineageGraph()
    {
        if (Snapshot is null || CurrentOrganismId is not { } id)
        {
            return;
        }

        _lineageFocusId = id;
        LineageGraphTitle = $"Lineage of {OrganismNamer.Name(id, Snapshot.Configuration.Naming)}";
        IsLineageGraphVisible = true;
        RebuildLineageGraph();
    }

    [RelayCommand]
    private void CloseLineageGraph() => IsLineageGraphVisible = false;

    /// <summary>Open/close the global-statistics panel; refreshes it from the current frame on open.</summary>
    [RelayCommand]
    private void ToggleStatistics()
    {
        IsStatisticsVisible = !IsStatisticsVisible;
        if (IsStatisticsVisible)
        {
            RebuildStatistics();
        }
    }

    [RelayCommand]
    private void CloseStatistics() => IsStatisticsVisible = false;

    /// <summary>Open/close the colour-mode + legend panel.</summary>
    [RelayCommand]
    private void ToggleLegend() => IsLegendVisible = !IsLegendVisible;

    [RelayCommand]
    private void CloseLegend() => IsLegendVisible = false;

    private void RebuildStatistics() => Statistics = Snapshot is null ? [] : GlobalStatistics.Build(Snapshot);

    // Accumulate the founding-type breakdown into a bounded history and refresh the scoreboard + chart.
    private void UpdateFoundingTypes(WorldSnapshot? value)
    {
        IReadOnlyList<FoundingTypeStanding> standings = value is null ? [] : FoundingTypeComposition.Standings(value);
        FoundingTypeStandings = standings;
        HasFoundingTypeBreakdown = standings.Count > 1 || (standings.Count == 1 && standings[0].Name != "Generic");

        if (value?.Metrics is null)
        {
            _foundingHistory.Clear();
            _lastFoundingTick = long.MinValue;
            FoundingTypeChart = null;
            return;
        }

        // A rewound tick (new world or a loaded file) starts a fresh trend.
        if (value.Tick < _lastFoundingTick)
        {
            _foundingHistory.Clear();
        }

        // One sample per distinct tick, so repeated frames of the same tick don't pile up.
        if (value.Tick != _lastFoundingTick)
        {
            _foundingHistory.Add(new FoundingTypeSample(
                value.Tick,
                value.Metrics.PopulationByFoundingType.ToDictionary(p => p.Name, p => p.Count)));
            if (_foundingHistory.Count > MaxFoundingHistory)
            {
                _foundingHistory.RemoveAt(0);
            }

            _lastFoundingTick = value.Tick;
        }

        FoundingTypeChart = FoundingTypeComposition.Chart(_foundingHistory);
    }

    // Accumulate cumulative kin-directed vs non-kin shares into a two-band composition over time. A
    // rising kin-directed band = sharing is becoming kin-biased, i.e. kin selection at work.
    private void UpdateSharingTrend(WorldSnapshot? value)
    {
        if (value?.Metrics is null)
        {
            _shareHistory.Clear();
            _cumulativeKinShares = 0;
            _cumulativeNonKinShares = 0;
            _lastShareTick = long.MinValue;
            SharingTrendChart = null;
            HasSharingTrend = false;
            return;
        }

        if (value.Tick < _lastShareTick)
        {
            _shareHistory.Clear();
            _cumulativeKinShares = 0;
            _cumulativeNonKinShares = 0;
        }

        if (value.Tick != _lastShareTick)
        {
            _cumulativeKinShares += value.Metrics.KinDirectedShares;
            _cumulativeNonKinShares += value.Metrics.NonKinShares;
            _shareHistory.Add(new FoundingTypeSample(value.Tick, new Dictionary<string, long>
            {
                ["kin-directed"] = _cumulativeKinShares,
                ["non-kin"] = _cumulativeNonKinShares,
            }));
            if (_shareHistory.Count > MaxFoundingHistory)
            {
                _shareHistory.RemoveAt(0);
            }

            _lastShareTick = value.Tick;
        }

        HasSharingTrend = _cumulativeKinShares + _cumulativeNonKinShares > 0;
        SharingTrendChart = FoundingTypeComposition.Chart(_shareHistory);
    }

    private void RebuildLineageGraph()
    {
        LineageGraph = Snapshot is null || _lineageFocusId is not { } id
            ? null
            : LineageGraphLayout.Build(Snapshot, id, (int)MaxParentGens, (int)MaxChildGens);
    }

    partial void OnMaxParentGensChanged(decimal value)
    {
        if (IsLineageGraphVisible)
        {
            RebuildLineageGraph();
        }
    }

    partial void OnMaxChildGensChanged(decimal value)
    {
        if (IsLineageGraphVisible)
        {
            RebuildLineageGraph();
        }
    }

    /// <summary>Recompute the leaderboard from the current snapshot (also runs automatically as the sim advances).</summary>
    [RelayCommand]
    public void RefreshRanking() => RefreshRankingPreservingSelection();

    partial void OnSnapshotChanged(WorldSnapshot? value)
    {
        RebuildScene();
        RebuildInspector();
        // Auto-refresh the open list, but rate-cap it while playing (see ThrottleLiveLists) so a fast tick
        // rate rebuilding the whole list every frame can't starve the UI thread of input.
        if (SidebarTabIndex == 1 && (!ThrottleLiveLists || DueForRefresh(ref _lastRankingRefreshMs)))
        {
            RefreshRankingPreservingSelection(); // leaderboard
        }

        if (SidebarTabIndex == 2 && (!ThrottleLiveLists || DueForRefresh(ref _lastOrganismRefreshMs)))
        {
            RefreshOrganismList(); // live-organisms list
        }

        if (IsLineageGraphVisible)
        {
            RebuildLineageGraph(); // keep the open lineage graph live as the sim advances
        }

        if (IsStatisticsVisible)
        {
            RebuildStatistics(); // keep the open statistics panel live as the sim advances
        }

        UpdateFoundingTypes(value);
        UpdateSharingTrend(value);
        Glance = value is null ? [] : GlobalStatistics.Glance(value); // cheap headline summary, always live

        OnPropertyChanged(nameof(Tick));
        OnPropertyChanged(nameof(Population));
        OnPropertyChanged(nameof(Extinct));
        OnPropertyChanged(nameof(ActiveEvents));
        OnPropertyChanged(nameof(HasActiveEvents));

        if (value is not null)
        {
            // Always fold the frame in (keeps baselines/latches current, so toggling on doesn't dump a
            // backlog); only surface notifications when they're enabled.
            IReadOnlyList<SimNotification> notifications = _eventWatcher.Observe(value);
            if (NotificationsEnabled && NotificationRaised is { } handler)
            {
                foreach (SimNotification notification in notifications)
                {
                    handler(notification);
                }
            }
        }
    }

    partial void OnSidebarTabIndexChanged(int value)
    {
        if (value == 1)
        {
            RefreshRankingPreservingSelection();
        }
        else if (value == 2)
        {
            RefreshOrganismList();
        }

        NotifyCurrentOrganismChanged();
    }

    partial void OnSelectedRankingEntryChanged(RankingEntry? value)
    {
        // Clicking a living entry also selects it on the map (footprint + Info inspector).
        if (value is { IsAlive: true })
        {
            SelectedOrganismId = value.OrganismId;
        }

        RebuildRankingDetail();
        NotifyCurrentOrganismChanged();
    }

    partial void OnOrganismSortKeyChanged(OrganismSortKey value) => RefreshOrganismList();

    partial void OnOrganismSortDescendingChanged(bool value) => RefreshOrganismList();

    partial void OnSelectedOrganismRowChanged(OrganismRow? value)
    {
        // Clicking an organism jumps to its full stats (selects it and switches to the Info tab).
        if (value is not null)
        {
            FocusOrganism(value.OrganismId);
        }
    }

    private void RefreshOrganismList() =>
        OrganismList = Snapshot is null ? [] : OrganismListBuilder.Build(Snapshot, OrganismSortKey, ascending: !OrganismSortDescending);

    partial void OnColourModeChanged(ColourMode value)
    {
        Legend = LegendBuilder.Build(value);
        RebuildScene();
    }

    partial void OnSelectedOrganismIdChanged(long? value)
    {
        RebuildScene();      // refresh the selected-organism footprint highlight
        RebuildInspector();
        NotifyCurrentOrganismChanged();
    }

    private void NotifyCurrentOrganismChanged()
    {
        OnPropertyChanged(nameof(CurrentOrganismId));
        OnPropertyChanged(nameof(CanViewLineageGraph));
        ViewLineageGraphCommand.NotifyCanExecuteChanged();
    }

    partial void OnInspectorChanged(OrganismInspectorViewModel? value) => OnPropertyChanged(nameof(HasSelection));

    private void RebuildScene() =>
        Scene = Snapshot is null ? null : WorldScene.FromSnapshot(Snapshot, ColourMode, SelectedOrganismId);

    private void RebuildInspector() =>
        Inspector = Snapshot is null || SelectedOrganismId is null
            ? null
            : OrganismInspectorViewModel.Create(Snapshot, SelectedOrganismId.Value);

    private void RefreshRankingPreservingSelection()
    {
        long? keep = SelectedRankingEntry?.OrganismId;
        Ranking = Snapshot is null ? [] : RankingBuilder.Build(Snapshot);
        SelectedRankingEntry = keep is { } id ? Ranking.FirstOrDefault(r => r.OrganismId == id) : null;
        RebuildRankingDetail(); // refresh detail too (covers the case where re-selection was value-equal)
    }

    private void RebuildRankingDetail()
    {
        if (Snapshot is null || SelectedRankingEntry is null)
        {
            RankingDetail = null;
            return;
        }

        long id = SelectedRankingEntry.OrganismId;

        // Living → the full inspector (brain, softmax, live economy); dead → its lineage record.
        RankingDetail = (ViewModelBase?)OrganismInspectorViewModel.Create(Snapshot, id)
            ?? LineageDetailViewModel.Create(Snapshot, id);
    }
}
