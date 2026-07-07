using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using LifeSim.App.ViewModels;

namespace LifeSim.App.Views;

public partial class MainView : UserControl
{
    public MainView() => InitializeComponent();

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
