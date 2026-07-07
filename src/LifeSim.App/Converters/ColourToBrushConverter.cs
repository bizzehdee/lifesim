using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace LifeSim.App.Converters;

/// <summary>Binds an <see cref="Color"/> (legend swatches, activations) to an <see cref="IBrush"/> for shape fills.</summary>
public sealed class ColourToBrushConverter : IValueConverter
{
    public static readonly ColourToBrushConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is Color colour ? new ImmutableSolidColorBrush(colour) : null;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        (value as ISolidColorBrush)?.Color;
}
