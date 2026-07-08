using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using LifeSim.App.ViewModels;
using LifeSim.App.Views;

namespace LifeSim.App;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        // Engine frames arrive on a background thread; marshal them onto the UI thread.
        static void Post(Action action) => Dispatcher.UIThread.Post(action);

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Desktop: full live engine, running immediately.
            desktop.MainWindow = new MainWindow
            {
                DataContext = new MainViewModel(liveEngine: true, autoStart: true, Post),
            };
        }
        else if (ApplicationLifetime is ISingleViewApplicationLifetime singleView)
        {
            // Browser (WASM) demo: a small live world is available but not auto-started; the user can
            // Play a small local world, load a snapshot, or connect to a sim serve stream.
            singleView.MainView = new MainView
            {
                DataContext = new MainViewModel(liveEngine: true, autoStart: false, Post, constrained: true),
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
