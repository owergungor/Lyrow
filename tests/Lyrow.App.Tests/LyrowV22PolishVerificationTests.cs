using FluentAssertions;
using Xunit;
using Lyrow.Core.Models;
using Lyrow.Core.Services;
using Lyrow.Audio.Control;
using Lyrow.Core.Lyrics;

namespace Lyrow.App.Tests;

public class LyrowV22PolishVerificationTests
{
    [Fact]
    public void ToastDebouncer_SuppressesDuplicatesWithinWindowAndIncrementsCounter()
    {
        var debouncer = new ToastDebouncer(TimeSpan.FromSeconds(2.0));

        var first = debouncer.RegisterToast("Lyrics loaded");
        first.shouldShow.Should().BeTrue();
        first.isDuplicateUpdate.Should().BeFalse();
        first.duplicateCount.Should().Be(1);

        var second = debouncer.RegisterToast("Lyrics loaded");
        second.shouldShow.Should().BeFalse();
        second.isDuplicateUpdate.Should().BeTrue();
        second.duplicateCount.Should().Be(2);

        var different = debouncer.RegisterToast("Update check completed");
        different.shouldShow.Should().BeTrue();
        different.isDuplicateUpdate.Should().BeFalse();
        different.duplicateCount.Should().Be(1);
    }

    [Fact]
    public void UpdateFrequency_CalculatesCorrectly()
    {
        var now = DateTime.UtcNow;

        // OnStartup is always due
        GitHubUpdateService.IsUpdateCheckDue(UpdateCheckFrequency.OnStartup, now.AddMinutes(-5), now).Should().BeTrue();

        // Daily
        GitHubUpdateService.IsUpdateCheckDue(UpdateCheckFrequency.Daily, now.AddHours(-12), now).Should().BeFalse();
        GitHubUpdateService.IsUpdateCheckDue(UpdateCheckFrequency.Daily, now.AddHours(-25), now).Should().BeTrue();

        // Weekly
        GitHubUpdateService.IsUpdateCheckDue(UpdateCheckFrequency.Weekly, now.AddDays(-3), now).Should().BeFalse();
        GitHubUpdateService.IsUpdateCheckDue(UpdateCheckFrequency.Weekly, now.AddDays(-8), now).Should().BeTrue();

        // Monthly
        GitHubUpdateService.IsUpdateCheckDue(UpdateCheckFrequency.Monthly, now.AddDays(-15), now).Should().BeFalse();
        GitHubUpdateService.IsUpdateCheckDue(UpdateCheckFrequency.Monthly, now.AddDays(-32), now).Should().BeTrue();
    }

    [Fact]
    public void FallbackPalettePresets_ContainSixRichPresetsWithValidDotsAndColorLerp()
    {
        PalettePresets.All.Count.Should().Be(6);

        var celestial = PalettePresets.All[0];
        celestial.Name.Should().Be("Celestial");
        celestial.DotHexColors.Count.Should().Be(4);
        celestial.TopColorHex.Should().StartWith("#");

        var aurora = PalettePresets.All[1];
        aurora.Name.Should().Be("Aurora");

        var sunset = PalettePresets.All[2];
        sunset.Name.Should().Be("Sunset");

        var ocean = PalettePresets.All[3];
        ocean.Name.Should().Be("Ocean");

        var ember = PalettePresets.All[4];
        ember.Name.Should().Be("Ember");

        var midnight = PalettePresets.All[5];
        midnight.Name.Should().Be("Midnight");

        // Verify smooth Lerp crossfade
        var blended = GlowPalette.Lerp(celestial.Palette, sunset.Palette, 0.5f);
        blended.TopColor.R.Should().BeGreaterThan(0);
        blended.BottomColor.G.Should().BeGreaterThan(0);
    }

    [Fact]
    public void DualVolumeControls_ManagePlaybackAndSystemMasterIndependently()
    {
        using var volumeService = new SyntheticAudioVolumeService();
        volumeService.SetPreferredAppId("Spotify");

        // Set playback volume to 45%
        volumeService.SetPlaybackVolumePercent(45).Should().BeTrue();
        volumeService.GetPlaybackVolumePercent().Should().Be(45);

        // Set system master volume to 80%
        volumeService.SetSystemVolumePercent(80).Should().BeTrue();
        volumeService.GetSystemVolumePercent().Should().Be(80);

        // Playback volume must remain 45%
        volumeService.GetPlaybackVolumePercent().Should().Be(45);

        // Mute playback independently
        volumeService.SetPlaybackMute(true).Should().BeTrue();
        volumeService.IsPlaybackMuted.Should().BeTrue();
        volumeService.IsSystemMuted.Should().BeFalse();

        // Toggle system mute
        volumeService.ToggleSystemMute().Should().BeTrue();
        volumeService.IsSystemMuted.Should().BeTrue();

        // Target names
        volumeService.PlaybackTargetName.Should().NotBeNullOrWhiteSpace();
        volumeService.SystemTargetName.Should().Be("System Master");
    }

    [Fact]
    public void LyricsViewportCalculator_RespectsCustomFocusedAndUnfocusedFontSizes()
    {
        double customFocused = 34.0;
        double customUnfocused = 22.0;

        // Active line at index 5
        var activeTarget = LyricsViewportCalculator.GetLineVisualTarget(5, 5, customFocused, customUnfocused);
        activeTarget.IsActive.Should().BeTrue();
        activeTarget.FontSize.Should().Be(customFocused);
        activeTarget.TargetOpacity.Should().Be(1.0);

        // Nearby inactive line at index 4
        var nearbyTarget = LyricsViewportCalculator.GetLineVisualTarget(4, 5, customFocused, customUnfocused);
        nearbyTarget.IsActive.Should().BeFalse();
        nearbyTarget.FontSize.Should().Be(customUnfocused);
        nearbyTarget.TargetOpacity.Should().BeLessThan(1.0);
    }

    [Fact]
    public void InactivityShutdownDelay_DefaultsToThreeSeconds()
    {
        var settings = LyrowSettings.CreateDefault();
        settings.GlowShutdownDelaySeconds.Should().Be(3);
        settings.UpdateFrequency.Should().Be(UpdateCheckFrequency.Weekly);
        settings.FocusedLyricFontSize.Should().Be(28.0);
        settings.UnfocusedLyricFontSize.Should().Be(20.0);
        settings.FallbackPalettePresetIndex.Should().Be(0);
    }
}
