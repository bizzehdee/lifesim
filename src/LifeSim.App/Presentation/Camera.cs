using Avalonia;

namespace LifeSim.App.Presentation;

/// <summary>
/// The map viewport camera: a scale (pixels per world tile) and the screen position of world origin
/// (0,0). Pure transform math (no Avalonia controls), so it is unit-testable. "Fit" is the
/// most-zoomed-out state — the whole world visible and centred — and doubles as the minimum zoom, so
/// the world can never be zoomed out past full view.
/// </summary>
public sealed class Camera
{
    private const double MaxScale = 400.0;

    private double _minScale;

    public double Scale { get; private set; }

    public double OriginX { get; private set; }

    public double OriginY { get; private set; }

    public bool IsInitialized => Scale > 0.0;

    /// <summary>Fits the whole world into the viewport, centred; this scale becomes the zoom-out floor.</summary>
    public void Fit(double viewWidth, double viewHeight, int worldWidth, int worldHeight)
    {
        if (worldWidth <= 0 || worldHeight <= 0 || viewWidth <= 0 || viewHeight <= 0)
        {
            return;
        }

        double fit = Math.Min(viewWidth / worldWidth, viewHeight / worldHeight);
        _minScale = fit;
        Scale = fit;
        OriginX = (viewWidth - (worldWidth * fit)) / 2.0;
        OriginY = (viewHeight - (worldHeight * fit)) / 2.0;
    }

    /// <summary>Zooms by <paramref name="factor"/> while keeping the world point under (<paramref name="px"/>, <paramref name="py"/>) fixed on screen.</summary>
    public void ZoomAt(double px, double py, double factor)
    {
        if (!IsInitialized)
        {
            return;
        }

        double target = Math.Clamp(Scale * factor, _minScale, MaxScale);
        if (Math.Abs(target - Scale) < double.Epsilon)
        {
            return;
        }

        (double worldX, double worldY) = ScreenToWorld(px, py);
        Scale = target;
        OriginX = px - (worldX * Scale);
        OriginY = py - (worldY * Scale);
    }

    public void Pan(double dx, double dy)
    {
        OriginX += dx;
        OriginY += dy;
    }

    public Point WorldToScreen(double worldX, double worldY) =>
        new(OriginX + (worldX * Scale), OriginY + (worldY * Scale));

    public (double WorldX, double WorldY) ScreenToWorld(double px, double py) =>
        Scale <= 0.0 ? (0.0, 0.0) : ((px - OriginX) / Scale, (py - OriginY) / Scale);
}
