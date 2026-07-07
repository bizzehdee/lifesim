using Avalonia;
using LifeSim.App.Presentation;

namespace LifeSim.App.Tests;

public class CameraTests
{
    [Fact]
    public void Fit_scalesToShowTheWholeWorld_andCentresIt()
    {
        var camera = new Camera();
        camera.Fit(viewWidth: 800, viewHeight: 600, worldWidth: 100, worldHeight: 100);

        Assert.Equal(6.0, camera.Scale, precision: 6);      // min(800/100, 600/100)
        Assert.Equal(100.0, camera.OriginX, precision: 6);  // (800 - 100*6) / 2
        Assert.Equal(0.0, camera.OriginY, precision: 6);    // (600 - 100*6) / 2
    }

    [Fact]
    public void ZoomAt_zoomsInAndKeepsThePointUnderTheCursorFixed()
    {
        var camera = new Camera();
        camera.Fit(800, 600, 100, 100);
        (double WorldX, double WorldY) before = camera.ScreenToWorld(300, 220);

        camera.ZoomAt(300, 220, 2.0);

        Assert.True(camera.Scale > 6.0);
        (double WorldX, double WorldY) after = camera.ScreenToWorld(300, 220);
        Assert.Equal(before.WorldX, after.WorldX, precision: 6);
        Assert.Equal(before.WorldY, after.WorldY, precision: 6);
    }

    [Fact]
    public void ZoomAt_cannotZoomOutBelowFit()
    {
        var camera = new Camera();
        camera.Fit(800, 600, 100, 100);
        double fit = camera.Scale;

        camera.ZoomAt(400, 300, 0.05); // try to zoom far out

        Assert.Equal(fit, camera.Scale, precision: 6); // clamped at the fit scale
    }

    [Fact]
    public void WorldToScreen_andScreenToWorld_areInverse_afterZoomAndPan()
    {
        var camera = new Camera();
        camera.Fit(800, 600, 100, 100);
        camera.ZoomAt(200, 150, 3.0);
        camera.Pan(20, -10);

        Point screen = camera.WorldToScreen(12.5, 7.25);
        (double WorldX, double WorldY) world = camera.ScreenToWorld(screen.X, screen.Y);

        Assert.Equal(12.5, world.WorldX, precision: 6);
        Assert.Equal(7.25, world.WorldY, precision: 6);
    }

    [Fact]
    public void Pan_shiftsTheOrigin()
    {
        var camera = new Camera();
        camera.Fit(800, 600, 100, 100);
        double originX = camera.OriginX;

        camera.Pan(15, -5);

        Assert.Equal(originX + 15, camera.OriginX, precision: 6);
    }

    [Fact]
    public void UninitializedCamera_isInert()
    {
        var camera = new Camera();
        Assert.False(camera.IsInitialized);
        camera.ZoomAt(10, 10, 2.0); // no-op before Fit
        Assert.False(camera.IsInitialized);
    }
}
