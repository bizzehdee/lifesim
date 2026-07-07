using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using LifeSim.App.Presentation;
using LifeSim.Core.Neat;

namespace LifeSim.App.Views;

/// <summary>
/// Renders a NEAT brain graph for the inspector (lifesim.md §18): nodes coloured by live activation
/// (node <c>state</c>), weighted edges (green = excitatory, red = inhibitory, opacity ∝ |weight|),
/// disabled edges faint, and recurrent (cycle-creating) links dashed to distinguish them from
/// feed-forward ones. Pure render of a laid-out <see cref="NeatGraph"/>.
/// </summary>
public sealed class NeatGraphControl : Control
{
    public static readonly StyledProperty<NeatGraph?> GraphProperty =
        AvaloniaProperty.Register<NeatGraphControl, NeatGraph?>(nameof(Graph));

    private static readonly Color Excitatory = Color.FromRgb(0x3F, 0xB9, 0x50);
    private static readonly Color Inhibitory = Color.FromRgb(0xE5, 0x48, 0x4D);
    private static readonly Color ActivationLow = Color.FromRgb(0x3B, 0x82, 0xF6);
    private static readonly Color ActivationHigh = Color.FromRgb(0x3F, 0xB9, 0x50);
    private static readonly ImmutablePen NodeOutline = new(new ImmutableSolidColorBrush(Colors.White, 0.85), 1.0);

    static NeatGraphControl()
    {
        AffectsRender<NeatGraphControl>(GraphProperty);
    }

    public NeatGraph? Graph
    {
        get => GetValue(GraphProperty);
        set => SetValue(GraphProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        NeatGraph? graph = Graph;
        if (graph is null || graph.Nodes.Count == 0)
        {
            return;
        }

        const double pad = 14.0;
        double w = Math.Max(1.0, Bounds.Width - (2 * pad));
        double h = Math.Max(1.0, Bounds.Height - (2 * pad));
        Point Map(double nx, double ny) => new(pad + (nx * w), pad + (ny * h));

        foreach (NeatEdgeLayout edge in graph.Edges)
        {
            Color baseColour = edge.Weight >= 0 ? Excitatory : Inhibitory;
            double opacity = edge.Enabled ? Math.Clamp(Math.Abs(edge.Weight), 0.15, 1.0) : 0.08;
            double thickness = edge.Enabled ? 1.0 + Math.Clamp(Math.Abs(edge.Weight), 0.0, 2.0) : 1.0;
            var pen = new ImmutablePen(
                new ImmutableSolidColorBrush(baseColour, opacity),
                thickness,
                edge.Recurrent ? new ImmutableDashStyle([3, 3], 0) : null);
            context.DrawLine(pen, Map(edge.FromX, edge.FromY), Map(edge.ToX, edge.ToY));
        }

        foreach (NeatNodeLayout node in graph.Nodes)
        {
            double t = Math.Clamp((node.Activation + 1.0) / 2.0, 0.0, 1.0);
            var brush = new ImmutableSolidColorBrush(SimulationPalette.Lerp(ActivationLow, ActivationHigh, t));
            double radius = node.Type == NodeType.Hidden ? 5.0 : 6.5;
            context.DrawEllipse(brush, NodeOutline, Map(node.X, node.Y), radius, radius);
        }
    }
}
