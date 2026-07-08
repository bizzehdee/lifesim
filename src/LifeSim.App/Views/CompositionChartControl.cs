using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using LifeSim.App.Presentation;

namespace LifeSim.App.Views;

/// <summary>
/// A stacked-area chart of founding-type composition over time: each band is a type's share of the
/// population, oldest sample on the left, newest on the right. Pure render of a prepared
/// <see cref="CompositionChart"/>; colours come from the same deterministic map as the scoreboard, so
/// bands and legend swatches match.
/// </summary>
public sealed class CompositionChartControl : Control
{
    public static readonly StyledProperty<CompositionChart?> ChartProperty =
        AvaloniaProperty.Register<CompositionChartControl, CompositionChart?>(nameof(Chart));

    static CompositionChartControl()
    {
        AffectsRender<CompositionChartControl>(ChartProperty);
    }

    public CompositionChart? Chart
    {
        get => GetValue(ChartProperty);
        set => SetValue(ChartProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        CompositionChart? chart = Chart;
        if (chart is null || chart.Types.Count == 0 || chart.Shares.Count < 2)
        {
            return;
        }

        double width = Bounds.Width;
        double height = Bounds.Height;
        if (width <= 1.0 || height <= 1.0)
        {
            return;
        }

        int samples = chart.Shares.Count;
        double X(int sample) => width * sample / (samples - 1);
        double Y(double cumulativeShare) => height - (cumulativeShare * height); // 0 share at bottom

        // Running cumulative top per sample as bands stack upward; each band fills between the previous
        // cumulative (bottom) and the new cumulative (top).
        var cumulative = new double[samples];
        for (int t = 0; t < chart.Types.Count; t++)
        {
            var geometry = new StreamGeometry();
            using (StreamGeometryContext geo = geometry.Open())
            {
                // Top edge (this band's cumulative), left → right...
                geo.BeginFigure(new Point(X(0), Y(cumulative[0] + chart.Shares[0][t])), isFilled: true);
                for (int s = 1; s < samples; s++)
                {
                    geo.LineTo(new Point(X(s), Y(cumulative[s] + chart.Shares[s][t])));
                }

                // ...then the bottom edge (the previous cumulative), right → left, and close.
                for (int s = samples - 1; s >= 0; s--)
                {
                    geo.LineTo(new Point(X(s), Y(cumulative[s])));
                }

                geo.EndFigure(isClosed: true);
            }

            context.DrawGeometry(chart.Colours[t], null, geometry);

            for (int s = 0; s < samples; s++)
            {
                cumulative[s] += chart.Shares[s][t];
            }
        }
    }
}
