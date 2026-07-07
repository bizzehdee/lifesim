using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using LifeSim.App.Presentation;

namespace LifeSim.App.Views;

/// <summary>
/// Renders a lineage/family tree: founder at the top, generations descending,
/// parent→child edges, living organisms green and dead ones grey, with the focused organism enlarged
/// and ringed. Pure render of a laid-out <see cref="LineageGraph"/>.
/// </summary>
public sealed class LineageGraphControl : Control
{
    public static readonly StyledProperty<LineageGraph?> GraphProperty =
        AvaloniaProperty.Register<LineageGraphControl, LineageGraph?>(nameof(Graph));

    private static readonly ImmutablePen EdgePen = new(new ImmutableSolidColorBrush(Color.FromRgb(0x8A, 0x8F, 0x98), 0.55), 1.0);
    private static readonly ImmutableSolidColorBrush AliveBrush = new(Color.FromRgb(0x3F, 0xB9, 0x50));
    private static readonly ImmutableSolidColorBrush DeadBrush = new(Color.FromRgb(0x6B, 0x70, 0x78));
    private static readonly ImmutableSolidColorBrush FocusBrush = new(Color.FromRgb(0xF2, 0xB1, 0x3A));
    private static readonly ImmutablePen FocusRing = new(new ImmutableSolidColorBrush(Colors.White), 2.0);

    static LineageGraphControl()
    {
        AffectsRender<LineageGraphControl>(GraphProperty);
    }

    public LineageGraph? Graph
    {
        get => GetValue(GraphProperty);
        set => SetValue(GraphProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        LineageGraph? graph = Graph;
        if (graph is null || graph.Nodes.Count == 0)
        {
            return;
        }

        const double pad = 20.0;
        double w = Math.Max(1.0, Bounds.Width - (2 * pad));
        double h = Math.Max(1.0, Bounds.Height - (2 * pad));
        Point Map(double nx, double ny) => new(pad + (nx * w), pad + (ny * h));

        foreach (LineageGraphEdge edge in graph.Edges)
        {
            context.DrawLine(EdgePen, Map(edge.FromX, edge.FromY), Map(edge.ToX, edge.ToY));
        }

        foreach (LineageGraphNode node in graph.Nodes)
        {
            double radius = node.IsFocus ? 7.0 : 4.0;
            ImmutableSolidColorBrush brush = node.IsFocus ? FocusBrush : node.IsAlive ? AliveBrush : DeadBrush;
            Point centre = Map(node.X, node.Y);
            context.DrawEllipse(brush, node.IsFocus ? FocusRing : null, centre, radius, radius);
        }
    }
}
