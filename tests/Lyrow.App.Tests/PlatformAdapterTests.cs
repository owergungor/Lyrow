using FluentAssertions;
using Lyrow.App.Platform;
using Xunit;

namespace Lyrow.App.Tests;

public class PlatformAdapterTests
{
    [Fact]
    public void SyntheticWindowPlatformAdapter_ZeroHandle_FailsGracefully()
    {
        var adapter = new SyntheticWindowPlatformAdapter();

        adapter.SetClickThrough(IntPtr.Zero, true).Should().BeFalse();
        adapter.IsClickThroughEnabled(IntPtr.Zero).Should().BeFalse();
    }

    [Fact]
    public void SyntheticWindowPlatformAdapter_SetsAndGetsClickThroughState()
    {
        var adapter = new SyntheticWindowPlatformAdapter();
        var fakeHandle = new IntPtr(12345);

        adapter.IsClickThroughEnabled(fakeHandle).Should().BeFalse();

        adapter.SetClickThrough(fakeHandle, true).Should().BeTrue();
        adapter.IsClickThroughEnabled(fakeHandle).Should().BeTrue();

        adapter.SetClickThrough(fakeHandle, false).Should().BeTrue();
        adapter.IsClickThroughEnabled(fakeHandle).Should().BeFalse();
    }

    [Fact]
    public void WindowsWindowPlatformAdapter_ZeroHandle_ReturnsFalseWithoutThrowing()
    {
        var adapter = new WindowsWindowPlatformAdapter();

        adapter.SetClickThrough(IntPtr.Zero, true).Should().BeFalse();
        adapter.IsClickThroughEnabled(IntPtr.Zero).Should().BeFalse();
    }

    [Fact]
    public void WindowsWindowPlatformAdapter_IsSupported_ReturnsTrueOnWindows()
    {
        var adapter = new WindowsWindowPlatformAdapter();
        adapter.IsSupported.Should().Be(OperatingSystem.IsWindows());
    }
}
