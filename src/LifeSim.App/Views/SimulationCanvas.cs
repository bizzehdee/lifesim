using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using LifeSim.App.Presentation;

namespace LifeSim.App.Views;

/// <summary>
/// The full-bleed simulation map (lifesim.md §18) — the deliberate exception to the Fluent
/// card/surface rules (see the project <c>lifesim-ui</c> skill). It custom-draws a
/// <see cref="WorldScene"/> through a <see cref="Camera"/> that supports zoom (mouse wheel toward the
/// cursor, or the on-screen buttons) and pan (drag). Biome tiles, organisms (fill = colour mode,
/// outline = last action, radius ∝ Size, overlays), a plague hatch, and the selected organism's
/// sensory footprint are drawn; only visible tiles are painted. Clicking (without dragging) an
/// organism sets <see cref="SelectedOrganismId"/> (two-way). The control holds no engine state.
/// </summary>
public sealed class SimulationCanvas : Control
{
    public static readonly StyledProperty<WorldScene?> SceneProperty =
        AvaloniaProperty.Register<SimulationCanvas, WorldScene?>(nameof(Scene));

    public static readonly StyledProperty<long?> SelectedOrganismIdProperty =
        AvaloniaProperty.Register<SimulationCanvas, long?>(nameof(SelectedOrganismId), defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

    private const double DragThreshold = 4.0;

    private static readonly ImmutableSolidColorBrush BackgroundBrush = new(Color.FromRgb(0x12, 0x14, 0x18));
    private static readonly ImmutablePen StressPen = new(new ImmutableSolidColorBrush(SimulationPalette.TooHot, 0.9), 2.0);
    private static readonly ImmutablePen EnvFootprintPen = new(new ImmutableSolidColorBrush(Color.FromRgb(0x8A, 0xB4, 0xF8), 0.9), 1.5, new ImmutableDashStyle([3, 3], 0));
    private static readonly ImmutablePen OrgFootprintPen = new(new ImmutableSolidColorBrush(Color.FromRgb(0xF8, 0xB4, 0x8A), 0.9), 1.5, new ImmutableDashStyle([2, 2], 0));
    private static readonly ImmutableSolidColorBrush ReproPipBrush = new(SimulationPalette.Reproduce);
    private static readonly ImmutableSolidColorBrush PredationFlashBrush = new(SimulationPalette.Predation, 0.45);
    private static readonly ImmutablePen SelectionRingPen = new(new ImmutableSolidColorBrush(Colors.White), 1.0);
    private static readonly ImmutablePen PlaguePen = new(new ImmutableSolidColorBrush(Colors.Black, 0.18), 1.0);

    private readonly Dictionary<uint, ImmutableSolidColorBrush> _brushes = [];
    private readonly Dictionary<uint, ImmutablePen> _pens = [];

    private readonly Camera _camera = new();
    private (int Width, int Height) _cameraDims;
    private bool _needsFit = true;

    private Point _pressPoint;
    private Point _lastPoint;
    private bool _pointerDown;
    private bool _dragging;

    static SimulationCanvas()
    {
        AffectsRender<SimulationCanvas>(SceneProperty, SelectedOrganismIdProperty);

        // Custom rendering isn't clipped to the control's bounds by default, so when zoomed in the
        // map's tiles/organisms would spill over the sidebar and toolbar. Clip to our own rect.
        ClipToBoundsProperty.OverrideDefaultValue<SimulationCanvas>(true);
    }

    public WorldScene? Scene
    {
        get => GetValue(SceneProperty);
        set => SetValue(SceneProperty, value);
    }

    public long? SelectedOrganismId
    {
        get => GetValue(SelectedOrganismIdProperty);
        set => SetValue(SelectedOrganismIdProperty, value);
    }

    /// <summary>Zooms in one step, about the viewport centre.</summary>
    public void ZoomIn() => ZoomAboutCentre(1.25);

    /// <summary>Zooms out one step, about the viewport centre.</summary>
    public void ZoomOut() => ZoomAboutCentre(1.0 / 1.25);

    /// <summary>Resets the view to fit the whole world.</summary>
    public void ResetView()
    {
        _needsFit = true;
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        Rect bounds = new(Bounds.Size);
        context.FillRectangle(BackgroundBrush, bounds);

        WorldScene? scene = Scene;
        if (scene is null || scene.Width <= 0 || scene.Height <= 0)
        {
            return;
        }

        EnsureCamera(bounds, scene);
        if (!_camera.IsInitialized)
        {
            return;
        }

        DrawBiomes(context, scene, bounds);
        if (scene.PlagueHatch)
        {
            DrawPlagueHatch(context, bounds);
        }

        DrawOrganisms(context, scene);
        DrawFootprint(context, scene);
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (!_camera.IsInitialized)
        {
            return;
        }

        Point p = e.GetPosition(this);
        _camera.ZoomAt(p.X, p.Y, Math.Pow(1.2, e.Delta.Y));
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        if (e.ClickCount == 2)
        {
            ResetView();
            return;
        }

        _pointerDown = true;
        _dragging = false;
        _pressPoint = e.GetPosition(this);
        _lastPoint = _pressPoint;
        e.Pointer.Capture(this);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (!_pointerDown)
        {
            return;
        }

        Point p = e.GetPosition(this);
        if (!_dragging && Distance(p, _pressPoint) > DragThreshold)
        {
            _dragging = true;
        }

        if (_dragging)
        {
            _camera.Pan(p.X - _lastPoint.X, p.Y - _lastPoint.Y);
            InvalidateVisual();
        }

        _lastPoint = p;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (!_pointerDown)
        {
            return;
        }

        _pointerDown = false;
        e.Pointer.Capture(null);

        if (!_dragging)
        {
            SelectedOrganismId = HitTest(e.GetPosition(this)); // a click, not a pan → (de)select
        }
    }

    private void ZoomAboutCentre(double factor)
    {
        if (!_camera.IsInitialized)
        {
            return;
        }

        _camera.ZoomAt(Bounds.Width / 2.0, Bounds.Height / 2.0, factor);
        InvalidateVisual();
    }

    private void EnsureCamera(Rect bounds, WorldScene scene)
    {
        if (_needsFit || !_camera.IsInitialized || _cameraDims != (scene.Width, scene.Height))
        {
            _camera.Fit(bounds.Width, bounds.Height, scene.Width, scene.Height);
            _cameraDims = (scene.Width, scene.Height);
            _needsFit = false;
        }
    }

    private long? HitTest(Point p)
    {
        WorldScene? scene = Scene;
        if (scene is null || !_camera.IsInitialized)
        {
            return null;
        }

        long? hit = null;
        double best = double.MaxValue;
        foreach (OrganismView organism in scene.Organisms)
        {
            Point centre = _camera.WorldToScreen(organism.X + 0.5, organism.Y + 0.5);
            double reach = Math.Max(organism.Radius * _camera.Scale, _camera.Scale * 0.5);
            double dsq = ((p.X - centre.X) * (p.X - centre.X)) + ((p.Y - centre.Y) * (p.Y - centre.Y));
            if (dsq <= reach * reach && dsq < best)
            {
                best = dsq;
                hit = organism.Id;
            }
        }

        return hit;
    }

    private void DrawBiomes(DrawingContext context, WorldScene scene, Rect bounds)
    {
        double scale = _camera.Scale;

        // Viewport culling: only paint tiles that intersect the visible area.
        int minX = Math.Max(0, (int)Math.Floor((0 - _camera.OriginX) / scale));
        int maxX = Math.Min(scene.Width, (int)Math.Ceiling((bounds.Width - _camera.OriginX) / scale));
        int minY = Math.Max(0, (int)Math.Floor((0 - _camera.OriginY) / scale));
        int maxY = Math.Min(scene.Height, (int)Math.Ceiling((bounds.Height - _camera.OriginY) / scale));

        for (int y = minY; y < maxY; y++)
        {
            for (int x = minX; x < maxX; x++)
            {
                Point origin = _camera.WorldToScreen(x, y);
                context.FillRectangle(Brush(scene.TileColour(x, y)), new Rect(origin.X, origin.Y, scale + 0.75, scale + 0.75));
            }
        }
    }

    private void DrawOrganisms(DrawingContext context, WorldScene scene)
    {
        double scale = _camera.Scale;
        foreach (OrganismView organism in scene.Organisms)
        {
            Point centre = _camera.WorldToScreen(organism.X + 0.5, organism.Y + 0.5);
            double r = Math.Max(1.5, organism.Radius * scale);

            if (organism.JustKilled)
            {
                Point tile = _camera.WorldToScreen(organism.X, organism.Y);
                context.FillRectangle(PredationFlashBrush, new Rect(tile.X, tile.Y, scale, scale));
            }

            bool selected = SelectedOrganismId == organism.Id;
            context.DrawEllipse(Brush(organism.Fill), Pen(organism.Outline, selected ? 3.0 : 2.0), centre, r, r);

            if (organism.Stressed)
            {
                context.DrawEllipse(null, StressPen, centre, r + 2.5, r + 2.5);
            }

            if (organism.ReproductiveReady)
            {
                context.DrawEllipse(ReproPipBrush, null, new Point(centre.X, centre.Y - r - 3.0), 2.0, 2.0);
            }

            if (selected)
            {
                context.DrawEllipse(null, SelectionRingPen, centre, r + 4.5, r + 4.5);
            }
        }
    }

    private void DrawFootprint(DrawingContext context, WorldScene scene)
    {
        if (scene.SelectedFootprint is not { } footprint)
        {
            return;
        }

        Point centre = _camera.WorldToScreen(footprint.CentreX, footprint.CentreY);
        double scale = _camera.Scale;
        if (footprint.EnvRadius > 0.0)
        {
            context.DrawEllipse(null, EnvFootprintPen, centre, footprint.EnvRadius * scale, footprint.EnvRadius * scale);
        }

        if (footprint.OrgRadius > 0.0)
        {
            context.DrawEllipse(null, OrgFootprintPen, centre, footprint.OrgRadius * scale, footprint.OrgRadius * scale);
        }
    }

    private static void DrawPlagueHatch(DrawingContext context, Rect bounds)
    {
        const double step = 12.0;
        for (double d = -bounds.Height; d < bounds.Width; d += step)
        {
            context.DrawLine(PlaguePen, new Point(d, 0), new Point(d + bounds.Height, bounds.Height));
        }
    }

    private static double Distance(Point a, Point b)
    {
        double dx = a.X - b.X;
        double dy = a.Y - b.Y;
        return Math.Sqrt((dx * dx) + (dy * dy));
    }

    private ImmutableSolidColorBrush Brush(Color colour)
    {
        uint key = colour.ToUInt32();
        if (!_brushes.TryGetValue(key, out ImmutableSolidColorBrush? brush))
        {
            brush = new ImmutableSolidColorBrush(colour);
            _brushes[key] = brush;
        }

        return brush;
    }

    private ImmutablePen Pen(Color colour, double thickness)
    {
        uint key = colour.ToUInt32() ^ (uint)(thickness * 7);
        if (!_pens.TryGetValue(key, out ImmutablePen? pen))
        {
            pen = new ImmutablePen(new ImmutableSolidColorBrush(colour), thickness);
            _pens[key] = pen;
        }

        return pen;
    }
}
