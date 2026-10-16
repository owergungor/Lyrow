using FluentAssertions;
using Lyrow.App.Platform;
using Xunit;

namespace Lyrow.App.Tests;

public class DisplayChangeTests
{
    [Fact]
    public void DisplayChangeNotifier_FiresEvent_WhenTriggered()
    {
        var manager = new SyntheticGlobalHotkeyManager();
        int eventCount = 0;

        manager.DisplayConfigurationChanged += (_, _) => eventCount++;

        manager.TriggerDisplayChange();
        manager.TriggerDisplayChange();

        eventCount.Should().Be(2);
    }
}
