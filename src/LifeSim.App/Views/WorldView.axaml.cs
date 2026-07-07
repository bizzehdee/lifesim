using Avalonia.Controls;
using Avalonia.Interactivity;

namespace LifeSim.App.Views;

public partial class WorldView : UserControl
{
    public WorldView() => InitializeComponent();

    private void OnZoomInClick(object? sender, RoutedEventArgs e) => MapCanvas.ZoomIn();

    private void OnZoomOutClick(object? sender, RoutedEventArgs e) => MapCanvas.ZoomOut();

    private void OnResetViewClick(object? sender, RoutedEventArgs e) => MapCanvas.ResetView();
}
