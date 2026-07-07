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

    /// <summary>Labels for any active world events (blight, plague, ice age / heatwave), for the status bar.</summary>
    public IReadOnlyList<string> ActiveEvents =>
        Snapshot is null ? [] : [.. Snapshot.EnvironmentModifiers.Select(EventLabel)];

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

    private readonly EventWatcher _eventWatcher = new();

    /// <summary>Whether in-app notifications are shown; toggleable live during a run. When off, frames are still folded into the watcher (so no backlog piles up), but nothing is raised.</summary>
    [ObservableProperty]
    private bool _notificationsEnabled = true;

    /// <summary>Raised (on the UI thread) when a frame reveals a notable event — blight, population explosion, a new multicellular milestone.</summary>
    public event Action<SimNotification>? NotificationRaised;

    /// <summary>Forget notification history so the next frame re-seeds baselines silently (call when a new world is adopted).</summary>
    public void ResetNotifications() => _eventWatcher.Reset();

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

    private void RebuildStatistics() => Statistics = Snapshot is null ? [] : GlobalStatistics.Build(Snapshot);

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
        if (SidebarTabIndex == 1)
        {
            RefreshRankingPreservingSelection(); // auto-refresh the leaderboard while the tab is open
        }

        if (SidebarTabIndex == 2)
        {
            RefreshOrganismList(); // auto-refresh the live-organisms list while the tab is open
        }

        if (IsLineageGraphVisible)
        {
            RebuildLineageGraph(); // keep the open lineage graph live as the sim advances
        }

        if (IsStatisticsVisible)
        {
            RebuildStatistics(); // keep the open statistics panel live as the sim advances
        }

        OnPropertyChanged(nameof(Tick));
        OnPropertyChanged(nameof(Population));
        OnPropertyChanged(nameof(Extinct));
        OnPropertyChanged(nameof(ActiveEvents));

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
