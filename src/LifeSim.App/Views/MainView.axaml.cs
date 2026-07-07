using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using LifeSim.App.Presentation;
using LifeSim.App.ViewModels;

namespace LifeSim.App.Views;

public partial class MainView : UserControl
{
    private WindowNotificationManager? _notifications;
    private WorldViewModel? _subscribedWorld;

    public MainView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Resubscribe();
    }

    // In-app toasts (lifesim.md §18): a WindowNotificationManager renders Fluent toast cards inside
    // this window (not native OS notifications). Frames publish on the UI thread, so the World's
    // NotificationRaised fires here directly.
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (TopLevel.GetTopLevel(this) is { } top)
        {
            _notifications = new WindowNotificationManager(top)
            {
                Position = NotificationPosition.BottomRight,
                MaxItems = 4,
            };
        }

        Resubscribe();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        Unsubscribe();
        _notifications = null;
        base.OnDetachedFromVisualTree(e);
    }

    private void Resubscribe()
    {
        WorldViewModel? world = (DataContext as MainViewModel)?.World;
        if (ReferenceEquals(world, _subscribedWorld))
        {
            return;
        }

        Unsubscribe();
        _subscribedWorld = world;
        if (_subscribedWorld is not null)
        {
            _subscribedWorld.NotificationRaised += OnNotification;
        }
    }

    private void Unsubscribe()
    {
        if (_subscribedWorld is not null)
        {
            _subscribedWorld.NotificationRaised -= OnNotification;
            _subscribedWorld = null;
        }
    }

    private void OnNotification(SimNotification notification)
    {
        // A notification about a specific organism is click-to-select: clicking the toast focuses
        // that organism exactly like a map click (lifesim.md §18).
        Action? onClick = notification.OrganismId is { } id
            ? () => _subscribedWorld?.FocusOrganism(id)
            : null;

        _notifications?.Show(new Notification(notification.Title, notification.Detail, Map(notification.Kind), onClick: onClick));
    }

    private static NotificationType Map(SimNotificationKind kind) => kind switch
    {
        SimNotificationKind.Warning => NotificationType.Warning,
        SimNotificationKind.Success => NotificationType.Success,
        _ => NotificationType.Information,
    };

    // Save/Load go through the picked file's stream (not a path) so they work on both the desktop
    // and the browser (WASM) target, which has no local filesystem (lifesim.md §1, §12).
    private async void OnSaveClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm || TopLevel.GetTopLevel(this) is not { } top || vm.CurrentJson() is not { } json)
        {
            return;
        }

        IStorageFile? file = await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save snapshot",
            SuggestedFileName = "world.json",
            FileTypeChoices = [new FilePickerFileType("Snapshot JSON") { Patterns = ["*.json"] }],
        });

        if (file is null)
        {
            return;
        }

        await using Stream stream = await file.OpenWriteAsync();
        await using var writer = new StreamWriter(stream);
        await writer.WriteAsync(json);
    }

    // Starting-options save/load: the whole setup (run parameters, toggles, and full config) as one
    // JSON file, via the picked file's stream so it works on desktop and the browser target alike.
    private async void OnSaveOptionsClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm || TopLevel.GetTopLevel(this) is not { } top)
        {
            return;
        }

        IStorageFile? file = await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save starting options",
            SuggestedFileName = "options.json",
            FileTypeChoices = [new FilePickerFileType("Options JSON") { Patterns = ["*.json"] }],
        });

        if (file is null)
        {
            return;
        }

        await using Stream stream = await file.OpenWriteAsync();
        await using var writer = new StreamWriter(stream);
        await writer.WriteAsync(vm.SaveOptionsJson());
    }

    private async void OnLoadOptionsClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm || TopLevel.GetTopLevel(this) is not { } top)
        {
            return;
        }

        IReadOnlyList<IStorageFile> files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Load starting options",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("Options JSON") { Patterns = ["*.json"] }],
        });

        if (files.Count == 0)
        {
            return;
        }

        await using Stream stream = await files[0].OpenReadAsync();
        using var reader = new StreamReader(stream);
        vm.LoadOptionsFromJson(await reader.ReadToEndAsync());
    }

    private async void OnLoadClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm || TopLevel.GetTopLevel(this) is not { } top)
        {
            return;
        }

        IReadOnlyList<IStorageFile> files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Load snapshot",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("Snapshot JSON") { Patterns = ["*.json"] }],
        });

        if (files.Count == 0)
        {
            return;
        }

        await using Stream stream = await files[0].OpenReadAsync();
        using var reader = new StreamReader(stream);
        vm.LoadFromJson(await reader.ReadToEndAsync());
    }
}
