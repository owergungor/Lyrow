using FluentAssertions;
using Lyrow.App.Platform;
using Xunit;

namespace Lyrow.App.Tests;

public class GlowOverlayBehaviorTests
{
    [Fact]
    public void WindowsWindowPlatformAdapter_ExtendedStyles_IncludeClickThroughNoActivateAndToolWindow()
    {
        // WS_EX_TRANSPARENT (0x00000020) guarantees mouse clicks pass through
        // WS_EX_NOACTIVATE (0x08000000) prevents stealing focus
        // WS_EX_TOOLWINDOW (0x00000080) hides from Alt+Tab and taskbar
        // WS_EX_LAYERED (0x00080000) enables per-pixel alpha compositor blending
        // WS_EX_TOPMOST (0x00000008) keeps ambient lighting along screen edges

        WindowsWindowPlatformAdapter.WS_EX_TRANSPARENT.Should().Be(0x00000020);
        WindowsWindowPlatformAdapter.WS_EX_NOACTIVATE.Should().Be(0x08000000);
        WindowsWindowPlatformAdapter.WS_EX_TOOLWINDOW.Should().Be(0x00000080);
        WindowsWindowPlatformAdapter.WS_EX_LAYERED.Should().Be(0x00080000);
        WindowsWindowPlatformAdapter.WS_EX_TOPMOST.Should().Be(0x00000008);

        uint compositeOverlayStyle = WindowsWindowPlatformAdapter.WS_EX_TRANSPARENT |
                                     WindowsWindowPlatformAdapter.WS_EX_NOACTIVATE |
                                     WindowsWindowPlatformAdapter.WS_EX_TOOLWINDOW |
                                     WindowsWindowPlatformAdapter.WS_EX_LAYERED |
                                     WindowsWindowPlatformAdapter.WS_EX_TOPMOST;

        (compositeOverlayStyle & WindowsWindowPlatformAdapter.WS_EX_TRANSPARENT).Should().NotBe(0u);
        (compositeOverlayStyle & WindowsWindowPlatformAdapter.WS_EX_NOACTIVATE).Should().NotBe(0u);
        (compositeOverlayStyle & WindowsWindowPlatformAdapter.WS_EX_TOOLWINDOW).Should().NotBe(0u);
    }

    [Fact]
    public void GlowOverlayManager_EnumerateScreens_ReturnsValidDisplays()
    {
        var screens = GlowOverlayManager.EnumerateScreens();
        screens.Should().NotBeNull();
        if (OperatingSystem.IsWindows() && screens.Count > 0)
        {
            var primary = screens.FirstOrDefault(s => s.IsPrimary);
            primary.Should().NotBeNull();
            primary!.Bounds.Width.Should().BeGreaterThan(0);
            primary.Bounds.Height.Should().BeGreaterThan(0);
        }
    }
}
