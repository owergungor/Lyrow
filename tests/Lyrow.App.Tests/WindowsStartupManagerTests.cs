using FluentAssertions;
using Lyrow.App.Platform;
using Xunit;

namespace Lyrow.App.Tests;

public class WindowsStartupManagerTests
{
    [Fact]
    public void SyntheticStartupManager_EnablesAndDisablesStartup()
    {
        var manager = new SyntheticStartupManager();

        manager.IsStartupEnabled().Should().BeFalse();

        manager.SetStartupEnabled(true).Should().BeTrue();
        manager.IsStartupEnabled().Should().BeTrue();

        manager.SetStartupEnabled(false).Should().BeTrue();
        manager.IsStartupEnabled().Should().BeFalse();
    }

    [Fact]
    public void WindowsStartupManager_IsSupported_MatchesOperatingSystem()
    {
        var manager = new WindowsStartupManager();
        manager.IsSupported.Should().Be(OperatingSystem.IsWindows());
    }
}
