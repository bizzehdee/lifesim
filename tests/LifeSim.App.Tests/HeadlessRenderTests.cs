using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LifeSim.App.ViewModels;
using LifeSim.App.Views;
using LifeSim.Core.Configuration;

namespace LifeSim.App.Tests;

/// <summary>Boots the real <see cref="App"/> on Avalonia's headless platform so views actually render in tests.</summary>
public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>().UseHeadless(new AvaloniaHeadlessPlatformOptions());
}

/// <summary>
/// Headless render smoke tests — they instantiate the real view tree so bugs that only surface at
/// render time (a data-template failing to resolve) are caught. Regression cover for the inspector
/// falling back to showing the view-model's type name instead of the inspector view.
/// </summary>
public class HeadlessRenderTests
{
    private static async Task InSession(Action body)
    {
        // Must await the dispatched work *before* disposing the session — otherwise the session's
        // Avalonia thread is torn down while the work is still queued and the test hangs.
        using var session = HeadlessUnitTestSession.StartNew(typeof(TestAppBuilder));
        await session.Dispatch(body, CancellationToken.None);
    }

    [Fact]
    public Task MainView_showsSetupBeforeAWorld_thenTheMapAfterCreating() => InSession(() =>
    {
        var vm = new MainViewModel(liveEngine: true, autoStart: false, post: a => a());
        var window = new Window { Content = new MainView { DataContext = vm }, Width = 1000, Height = 700 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        Assert.False(vm.HasWorld);
        Assert.DoesNotContain(window.GetVisualDescendants().OfType<SimulationCanvas>(), c => c.IsEffectivelyVisible);

        Assert.True(vm.TryCreateWorld(42, 48, 48, SimulationConfig.Default with { InitialPopulation = 20 }, out _));
        Dispatcher.UIThread.RunJobs();

        Assert.Contains(window.GetVisualDescendants().OfType<SimulationCanvas>(), c => c.IsEffectivelyVisible);
        vm.Dispose();
    });

    [Fact]
    public Task Map_clipsToItsBounds_soItDoesNotOverflowTheSidebarOrToolbar() => InSession(() =>
    {
        // Regression: zoomed-in map tiles/organisms must not spill over sibling chrome.
        Assert.True(new SimulationCanvas().ClipToBounds);
    });

    [Fact]
    public Task SelectingAnOrganism_realizesTheInspectorView_notItsTypeName() => InSession(() =>
    {
        var vm = new MainViewModel(liveEngine: true, autoStart: false, post: a => a());
        Assert.True(vm.TryCreateWorld(42, 48, 48, SimulationConfig.Default with { InitialPopulation = 20 }, out _));
        var window = new Window { Content = new MainView { DataContext = vm }, Width = 1000, Height = 700 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        vm.World.SelectedOrganismId = vm.World.Snapshot!.Organisms[0].OrganismId;
        Dispatcher.UIThread.RunJobs();

        // The bug: the inspector ContentControl rendered the VM's type name instead of resolving the view.
        Assert.NotEmpty(window.GetVisualDescendants().OfType<OrganismInspectorView>());
        vm.Dispose();
    });
}
