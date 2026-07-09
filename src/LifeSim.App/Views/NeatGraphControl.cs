using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using LifeSim.App.Presentation;
using LifeSim.Core.Neat;
using LifeSim.Core.Sensing;

namespace LifeSim.App.Views;

/// <summary>
/// Renders a NEAT brain graph for the inspector: nodes coloured by live activation
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

    private const double Pad = 14.0;
    private const double HiddenRadius = 5.0;
    private const double IoRadius = 6.5;

    private long? _hoveredNodeId;

    static NeatGraphControl()
    {
        AffectsRender<NeatGraphControl>(GraphProperty);
    }

    public NeatGraphControl()
    {
        // Show the input tooltips right at the cursor, without the usual hover delay.
        ToolTip.SetPlacement(this, PlacementMode.Pointer);
        ToolTip.SetShowDelay(this, 0);
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

        // Paint the whole area (transparent) so the control is hit-testable end-to-end for tooltips.
        context.FillRectangle(Brushes.Transparent, new Rect(Bounds.Size));

        double w = Math.Max(1.0, Bounds.Width - (2 * Pad));
        double h = Math.Max(1.0, Bounds.Height - (2 * Pad));
        Point Map(double nx, double ny) => new(Pad + (nx * w), Pad + (ny * h));

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
            double radius = node.Type == NodeType.Hidden ? HiddenRadius : IoRadius;
            context.DrawEllipse(brush, NodeOutline, Map(node.X, node.Y), radius, radius);
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        UpdateHover(e.GetPosition(this));
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        ClearHover();
    }

    // Show a tooltip naming the input and its live value when the cursor is over an input node.
    private void UpdateHover(Point pointer)
    {
        NeatGraph? graph = Graph;
        if (graph is null)
        {
            ClearHover();
            return;
        }

        double w = Math.Max(1.0, Bounds.Width - (2 * Pad));
        double h = Math.Max(1.0, Bounds.Height - (2 * Pad));
        const double hitRadius = IoRadius + 4.0; // a little slop so the target isn't pixel-precise

        foreach (NeatNodeLayout node in graph.Nodes)
        {
            if (node.Type != NodeType.Input || !SensoryFieldLabels.TryForInputNode(node.Id, out SensoryField field))
            {
                continue;
            }

            var centre = new Point(Pad + (node.X * w), Pad + (node.Y * h));
            if (Distance(centre, pointer) > hitRadius)
            {
                continue;
            }

            if (_hoveredNodeId != node.Id)
            {
                _hoveredNodeId = node.Id;
                string value = node.Activation.ToString("F2", CultureInfo.InvariantCulture);
                ToolTip.SetTip(this, $"{SensoryFieldLabels.Describe(field)}: {value}");
                // Reopen so the tooltip repositions to the newly hovered node.
                ToolTip.SetIsOpen(this, false);
                ToolTip.SetIsOpen(this, true);
            }

            return;
        }

        ClearHover();
    }

    private void ClearHover()
    {
        if (_hoveredNodeId is null)
        {
            return;
        }

        _hoveredNodeId = null;
        ToolTip.SetIsOpen(this, false);
        ToolTip.SetTip(this, null);
    }

    private static double Distance(Point a, Point b)
    {
        double dx = a.X - b.X;
        double dy = a.Y - b.Y;
        return Math.Sqrt((dx * dx) + (dy * dy));
    }
}
