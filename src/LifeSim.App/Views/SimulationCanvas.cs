using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Threading;
using LifeSim.App.Presentation;

namespace LifeSim.App.Views;

/// <summary>
/// The full-bleed simulation map — the deliberate exception to the Fluent
/// card/surface rules (see the project <c>lifesim-ui</c> skill). It custom-draws a
/// <see cref="WorldScene"/> through a <see cref="Camera"/> that supports zoom (mouse wheel toward the
/// cursor, or the on-screen buttons) and pan (drag). Biome tiles (with a subtle per-tile dappling so
/// large flat regions don't look like a colour-swatch grid), organisms (soft radial fill + glow,
/// outline = last action, radius ∝ Size, overlays), a day/night ambient tint + edge vignette, a
/// plague hatch, and the selected organism's sensory footprint are drawn; only visible tiles are
/// painted, and organism movement between ticks eases in over a short, bounded animation rather than
/// snapping. Clicking (without dragging) an organism sets <see cref="SelectedOrganismId"/> (two-way).
/// The control holds no engine state beyond the last frame or two, purely for the move animation.
/// </summary>
public sealed class SimulationCanvas : Control
{
    public static readonly StyledProperty<WorldScene?> SceneProperty =
        AvaloniaProperty.Register<SimulationCanvas, WorldScene?>(nameof(Scene));

    public static readonly StyledProperty<long?> SelectedOrganismIdProperty =
        AvaloniaProperty.Register<SimulationCanvas, long?>(nameof(SelectedOrganismId), defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

    private const double DragThreshold = 4.0;

    // Bounded move-animation: a short, low-rate redraw burst after each tick, not a continuous loop —
    // costs nothing while the sim is paused or between ticks.
    private static readonly TimeSpan MoveAnimDuration = TimeSpan.FromMilliseconds(220);
    private static readonly TimeSpan MoveAnimFrameInterval = TimeSpan.FromMilliseconds(33); // ~30fps

    private static readonly ImmutableSolidColorBrush BackgroundBrush = new(Color.FromRgb(0x12, 0x14, 0x18));
    private static readonly ImmutablePen StressPen = new(new ImmutableSolidColorBrush(SimulationPalette.TooHot, 0.9), 2.0);
    private static readonly ImmutablePen EnvFootprintPen = new(new ImmutableSolidColorBrush(Color.FromRgb(0x8A, 0xB4, 0xF8), 0.9), 1.5, new ImmutableDashStyle([3, 3], 0));
    private static readonly ImmutablePen OrgFootprintPen = new(new ImmutableSolidColorBrush(Color.FromRgb(0xF8, 0xB4, 0x8A), 0.9), 1.5, new ImmutableDashStyle([2, 2], 0));
    private static readonly ImmutableSolidColorBrush ReproPipBrush = new(SimulationPalette.Reproduce);
    private static readonly ImmutableSolidColorBrush PredationFlashBrush = new(SimulationPalette.Predation, 0.45);
    private static readonly ImmutablePen SelectionRingPen = new(new ImmutableSolidColorBrush(Colors.White), 1.0);
    private static readonly ImmutablePen SelectionGlowPen = new(new ImmutableSolidColorBrush(Colors.White, 0.35), 3.0);
    private static readonly ImmutablePen PlaguePen = new(new ImmutableSolidColorBrush(Colors.Black, 0.18), 1.0);

    private static readonly Color NightTint = Color.FromRgb(0x0B, 0x10, 0x2A);
    private const double MaxNightTintOpacity = 0.45;

    // Per-tile lightness nudges a dappled tile can land on — bucket 0 (unshaded, the common case) plus
    // two light/dark steps either side, kept subtle so biomes still read as their base colour.
    private static readonly double[] DappleShades = [0.0, -0.025, 0.025, -0.05, 0.05];

    private readonly Dictionary<uint, ImmutablePen> _pens = [];
    private readonly Dictionary<uint, IBrush> _organismBrushes = [];
    private readonly Dictionary<(uint Colour, int Bucket), ImmutableSolidColorBrush> _dappleBrushes = [];

    private readonly Camera _camera = new();
    private (int Width, int Height) _cameraDims;
    private bool _needsFit = true;

    private Point _pressPoint;
    private Point _lastPoint;
    private bool _pointerDown;
    private bool _dragging;

    private Dictionary<long, (double X, double Y, double Radius)> _previousOrganisms = [];
    private Dictionary<long, (double X, double Y, double Radius)> _currentOrganisms = [];
    private readonly DispatcherTimer _moveAnimTimer;
    private DateTime _moveAnimStart;
    private bool _moveAnimRunning;

    static SimulationCanvas()
    {
        AffectsRender<SimulationCanvas>(SceneProperty, SelectedOrganismIdProperty);

        // Custom rendering isn't clipped to the control's bounds by default, so when zoomed in the
        // map's tiles/organisms would spill over the sidebar and toolbar. Clip to our own rect.
        ClipToBoundsProperty.OverrideDefaultValue<SimulationCanvas>(true);
    }

    public SimulationCanvas()
    {
        _moveAnimTimer = new DispatcherTimer { Interval = MoveAnimFrameInterval };
        _moveAnimTimer.Tick += (_, _) => OnMoveAnimTick();
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

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == SceneProperty)
        {
            OnSceneChanged(change.NewValue as WorldScene);
        }
    }

    private void OnSceneChanged(WorldScene? scene)
    {
        _previousOrganisms = _currentOrganisms;
        _currentOrganisms = scene is null
            ? []
            : scene.Organisms.ToDictionary(o => o.Id, o => ((double)o.X, (double)o.Y, o.Radius));

        // Only worth animating if something actually moved (id survives with a changed position) —
        // otherwise skip the timer entirely (e.g. the very first frame, or a reload of the same tick).
        bool anyMoved = _currentOrganisms.Any(kv =>
            _previousOrganisms.TryGetValue(kv.Key, out (double X, double Y, double Radius) prev)
            && (prev.X != kv.Value.X || prev.Y != kv.Value.Y));

        if (!anyMoved)
        {
            _moveAnimTimer.Stop();
            _moveAnimRunning = false;
            return;
        }

        _moveAnimStart = DateTime.UtcNow;
        _moveAnimRunning = true;
        _moveAnimTimer.Start();
    }

    private void OnMoveAnimTick()
    {
        if (DateTime.UtcNow - _moveAnimStart >= MoveAnimDuration)
        {
            _moveAnimTimer.Stop();
            _moveAnimRunning = false;
        }

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
        DrawNightTint(context, scene, bounds);
        if (scene.PlagueHatch)
        {
            DrawPlagueHatch(context, bounds);
        }

        DrawOrganisms(context, scene);
        DrawFootprint(context, scene);
        DrawVignette(context, bounds);
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
                context.FillRectangle(DappleBrush(scene.TileColour(x, y), x, y), new Rect(origin.X, origin.Y, scale + 0.75, scale + 0.75));
            }
        }
    }

    /// <summary>
    /// A day/night wash over the whole map, darkest at <see cref="WorldScene.GlobalLight"/> == 0 and
    /// invisible at full daylight — one full-viewport fill, independent of world size or zoom.
    /// </summary>
    private static void DrawNightTint(DrawingContext context, WorldScene scene, Rect bounds)
    {
        double darkness = 1.0 - Math.Clamp(scene.GlobalLight, 0.0, 1.0);
        double opacity = darkness * MaxNightTintOpacity;
        if (opacity <= 0.002)
        {
            return;
        }

        context.FillRectangle(new ImmutableSolidColorBrush(NightTint, opacity), bounds);
    }

    /// <summary>A soft radial darkening toward the viewport edges — a fixed screen-space overlay, one draw per frame.</summary>
    private static void DrawVignette(DrawingContext context, Rect bounds)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return;
        }

        var vignette = new RadialGradientBrush
        {
            Center = RelativePoint.Center,
            GradientOrigin = RelativePoint.Center,
            RadiusX = new RelativeScalar(0.75, RelativeUnit.Relative),
            RadiusY = new RelativeScalar(0.75, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(Colors.Transparent, 0.65),
                new GradientStop(Color.FromArgb(0x50, 0x00, 0x00, 0x00), 1.0),
            },
        };
        context.FillRectangle(vignette.ToImmutable(), bounds);
    }

    private void DrawOrganisms(DrawingContext context, WorldScene scene)
    {
        double scale = _camera.Scale;
        double t = MoveAnimT();

        foreach (OrganismView organism in scene.Organisms)
        {
            (double x, double y, double radius) = Animated(organism, t);
            Point centre = _camera.WorldToScreen(x + 0.5, y + 0.5);
            double r = Math.Max(1.5, radius * scale);

            if (organism.JustKilled)
            {
                Point tile = _camera.WorldToScreen(x, y);
                context.FillRectangle(PredationFlashBrush, new Rect(tile.X, tile.Y, scale, scale));
            }

            // Soft outer glow behind the marker, in its own fill colour, to lift it off the tile grid.
            context.DrawEllipse(GlowBrush(organism.Fill), null, centre, r * 1.8, r * 1.8);

            bool selected = SelectedOrganismId == organism.Id;
            context.DrawEllipse(OrganismBrush(organism.Fill), Pen(organism.Outline, selected ? 3.0 : 2.0), centre, r, r);

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
                context.DrawEllipse(null, SelectionGlowPen, centre, r + 5.5, r + 5.5);
                context.DrawEllipse(null, SelectionRingPen, centre, r + 4.5, r + 4.5);
            }
        }
    }

    /// <summary>Eased progress [0, 1] through the current move animation (1 = settled, snap to real positions).</summary>
    private double MoveAnimT()
    {
        if (!_moveAnimRunning)
        {
            return 1.0;
        }

        double raw = Math.Clamp((DateTime.UtcNow - _moveAnimStart).TotalMilliseconds / MoveAnimDuration.TotalMilliseconds, 0.0, 1.0);
        return raw * raw * (3.0 - (2.0 * raw)); // smoothstep
    }

    /// <summary>The organism's position/radius eased from its previous frame toward its current one; unanimated (e.g. newborn) organisms render at their real position immediately.</summary>
    private (double X, double Y, double Radius) Animated(OrganismView organism, double t)
    {
        if (t >= 1.0 || !_previousOrganisms.TryGetValue(organism.Id, out (double X, double Y, double Radius) prev))
        {
            return (organism.X, organism.Y, organism.Radius);
        }

        return (
            prev.X + ((organism.X - prev.X) * t),
            prev.Y + ((organism.Y - prev.Y) * t),
            prev.Radius + ((organism.Radius - prev.Radius) * t));
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

    /// <summary>
    /// A tile's fill, nudged a few percent lighter/darker by a deterministic hash of its coordinates —
    /// breaks up flat biome regions into an organic dapple instead of a colour-swatch grid. Bucketed
    /// (not a continuous per-tile value) so the brush cache stays bounded at colours × <see cref="DappleShades"/>.Length
    /// regardless of world size.
    /// </summary>
    private ImmutableSolidColorBrush DappleBrush(Color colour, int x, int y)
    {
        int bucket = TileHash(x, y) % DappleShades.Length;
        var key = (colour.ToUInt32(), bucket);
        if (_dappleBrushes.TryGetValue(key, out ImmutableSolidColorBrush? cached))
        {
            return cached;
        }

        double t = DappleShades[bucket];
        var brush = new ImmutableSolidColorBrush(t == 0.0 ? colour : Shade(colour, t));
        _dappleBrushes[key] = brush;
        return brush;
    }

    /// <summary>Cheap deterministic per-tile hash — same tile always dapples the same way, frame to frame.</summary>
    private static int TileHash(int x, int y)
    {
        unchecked
        {
            int h = (x * 374761393) + (y * 668265263);
            h = (h ^ (h >> 13)) * 1274126177;
            return (h ^ (h >> 16)) & int.MaxValue;
        }
    }

    private static Color Shade(Color colour, double t) => t > 0.0
        ? SimulationPalette.Lerp(colour, Colors.White, t)
        : SimulationPalette.Lerp(colour, Colors.Black, -t);

    /// <summary>An organism's marker fill: a soft radial gradient (light highlight → the fill colour) instead of a flat disc.</summary>
    private IBrush OrganismBrush(Color colour)
    {
        uint key = colour.ToUInt32();
        if (_organismBrushes.TryGetValue(key, out IBrush? cached))
        {
            return cached;
        }

        var brush = new RadialGradientBrush
        {
            Center = new RelativePoint(0.35, 0.32, RelativeUnit.Relative),
            GradientOrigin = new RelativePoint(0.35, 0.32, RelativeUnit.Relative),
            RadiusX = new RelativeScalar(0.85, RelativeUnit.Relative),
            RadiusY = new RelativeScalar(0.85, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(SimulationPalette.Lerp(colour, Colors.White, 0.55), 0.0),
                new GradientStop(colour, 1.0),
            },
        }.ToImmutable();
        _organismBrushes[key] = brush;
        return brush;
    }

    /// <summary>A soft, fading glow in the organism's own colour, drawn oversized behind the marker.</summary>
    private IBrush GlowBrush(Color colour)
    {
        uint key = colour.ToUInt32() ^ 0x9E3779B9;
        if (_organismBrushes.TryGetValue(key, out IBrush? cached))
        {
            return cached;
        }

        var brush = new RadialGradientBrush
        {
            Center = RelativePoint.Center,
            GradientOrigin = RelativePoint.Center,
            RadiusX = new RelativeScalar(0.5, RelativeUnit.Relative),
            RadiusY = new RelativeScalar(0.5, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(Color.FromArgb(0x40, colour.R, colour.G, colour.B), 0.0),
                new GradientStop(Color.FromArgb(0x00, colour.R, colour.G, colour.B), 1.0),
            },
        }.ToImmutable();
        _organismBrushes[key] = brush;
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
