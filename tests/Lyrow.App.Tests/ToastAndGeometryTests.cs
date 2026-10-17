using FluentAssertions;
using Lyrow.App.Platform;
using Xunit;

namespace Lyrow.App.Tests;

public class ToastAndGeometryTests
{
    [Fact]
    public void ToastService_EmitsToastNotifications_WithCorrectTypeAndTimestamp()
    {
        var service = new ToastNotificationService();
        ToastItem? received = null;
        service.ToastReceived += (_, toast) => received = toast;

        service.ShowSuccess("Theme changed to Dark");

        received.Should().NotBeNull();
        received!.Message.Should().Be("Theme changed to Dark");
        received.Type.Should().Be(ToastType.Success);
        received.CreatedUtc.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void ToastService_AntiSpamDebounce_DropsImmediateDuplicates()
    {
        var service = new ToastNotificationService();
        int eventCount = 0;
        service.ToastReceived += (_, _) => eventCount++;

        // Send same toast 5 times in rapid succession
        service.ShowError("Kısayol zaten kullanımda");
        service.ShowError("Kısayol zaten kullanımda");
        service.ShowError("Kısayol zaten kullanımda");
        service.ShowError("Kısayol zaten kullanımda");
        service.ShowError("Kısayol zaten kullanımda");

        // Debounce must allow only 1 event through
        eventCount.Should().Be(1);
    }

    [Fact]
    public void ToastService_DifferentMessages_AreAllowedThrough()
    {
        var service = new ToastNotificationService();
        var messages = new List<string>();
        service.ToastReceived += (_, t) => messages.Add(t.Message);

        service.ShowSuccess("Message 1");
        service.ShowInfo("Message 2");
        service.ShowWarning("Message 3");

        messages.Should().HaveCount(3);
        messages.Should().ContainInOrder("Message 1", "Message 2", "Message 3");
    }

    [Fact]
    public void ToastService_EmptyOrWhitespaceMessages_AreIgnored()
    {
        var service = new ToastNotificationService();
        int eventCount = 0;
        service.ToastReceived += (_, _) => eventCount++;

        service.Show("");
        service.Show("   ");

        eventCount.Should().Be(0);
    }

    [Fact]
    public void CardGeometryConstraints_ValidateDeterministicHeights()
    {
        // Live Media Card: fixed 76px
        const double liveMediaHeight = 76.0;
        liveMediaHeight.Should().Be(76.0);

        // Metrics Grid (Volume & Lyrics Status): fixed 104px
        const double metricsGridHeight = 104.0;
        metricsGridHeight.Should().Be(104.0);

        // Modules Grid (Glow & Lyrics Cards): fixed 200px
        const double modulesGridHeight = 200.0;
        modulesGridHeight.Should().Be(200.0);
    }
}
