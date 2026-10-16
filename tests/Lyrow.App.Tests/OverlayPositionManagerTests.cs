using Avalonia;
using FluentAssertions;
using Lyrow.App.Platform;
using Lyrow.Core.Models;
using Xunit;

namespace Lyrow.App.Tests;

public class OverlayPositionManagerTests
{
    [Fact]
    public void CalculatePosition_Center_PlacesWindowInCenterOfWorkingArea()
    {
        // Arrange: 1920x1080 display, window 400x200
        var workingArea = new PixelRect(0, 0, 1920, 1080);
        var windowSize = new PixelSize(400, 200);

        // Act
        var pos = OverlayPositionManager.CalculatePosition(workingArea, windowSize, OverlayPosition.Center);

        // Assert: X = (1920-400)/2 = 760, Y = (1080-200)/2 = 440
        pos.X.Should().Be(760);
        pos.Y.Should().Be(440);
    }

    [Fact]
    public void CalculatePosition_TopLeft_AppliesMargin()
    {
        var workingArea = new PixelRect(100, 50, 1920, 1080);
        var windowSize = new PixelSize(300, 150);
        int margin = 30;

        var pos = OverlayPositionManager.CalculatePosition(workingArea, windowSize, OverlayPosition.TopLeft, margin);

        pos.X.Should().Be(100 + margin);
        pos.Y.Should().Be(50 + margin);
    }

    [Fact]
    public void CalculatePosition_BottomRight_AppliesMargin()
    {
        var workingArea = new PixelRect(0, 0, 1920, 1080);
        var windowSize = new PixelSize(400, 200);
        int margin = 20;

        var pos = OverlayPositionManager.CalculatePosition(workingArea, windowSize, OverlayPosition.BottomRight, margin);

        pos.X.Should().Be(1920 - 400 - margin);
        pos.Y.Should().Be(1080 - 200 - margin);
    }

    [Fact]
    public void CalculatePosition_TopCenter_CentersHorizontallyAndPinsTop()
    {
        var workingArea = new PixelRect(0, 0, 1920, 1080);
        var windowSize = new PixelSize(600, 100);
        int margin = 25;

        var pos = OverlayPositionManager.CalculatePosition(workingArea, windowSize, OverlayPosition.TopCenter, margin);

        pos.X.Should().Be((1920 - 600) / 2);
        pos.Y.Should().Be(margin);
    }

    [Fact]
    public void CalculatePosition_BottomCenter_CentersHorizontallyAndPinsBottom()
    {
        var workingArea = new PixelRect(0, 0, 1920, 1080);
        var windowSize = new PixelSize(600, 100);
        int margin = 25;

        var pos = OverlayPositionManager.CalculatePosition(workingArea, windowSize, OverlayPosition.BottomCenter, margin);

        pos.X.Should().Be((1920 - 600) / 2);
        pos.Y.Should().Be(1080 - 100 - margin);
    }

    [Fact]
    public void CalculatePosition_ClampsWithinWorkingArea_WhenWindowExceedsWorkingArea()
    {
        // Oversized window on smaller screen
        var workingArea = new PixelRect(0, 0, 800, 600);
        var windowSize = new PixelSize(1000, 800);

        var pos = OverlayPositionManager.CalculatePosition(workingArea, windowSize, OverlayPosition.BottomRight, 20);

        pos.X.Should().Be(0);
        pos.Y.Should().Be(0);
    }

    [Fact]
    public void ResolveTargetScreen_ReturnsNull_WhenScreensListIsEmptyOrNull()
    {
        var res1 = OverlayPositionManager.ResolveTargetScreen(null, 0);
        res1.Should().BeNull();

        var res2 = OverlayPositionManager.ResolveTargetScreen(Array.Empty<Avalonia.Platform.Screen>(), 0);
        res2.Should().BeNull();
    }
}
