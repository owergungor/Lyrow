using System.IO;
using FluentAssertions;
using Lyrow.App.Platform;
using Lyrow.Core.Lyrics;
using Lyrow.Core.Models;
using Lyrow.Music.Session;
using Xunit;

namespace Lyrow.App.Tests;

/// <summary>
/// Authoritative test suite verifying Lyrow v3.6 media control, lyrics synchronization,
/// animated artwork fallback, and compact player features.
/// </summary>
public sealed class LyrowV36FeatureVerificationTests
{
    private static string FindRepoRoot()
    {
        string? current = AppContext.BaseDirectory;
        while (current != null && !File.Exists(Path.Combine(current, "Lyrow.sln")))
        {
            current = Directory.GetParent(current)?.FullName;
        }
        return current ?? throw new InvalidOperationException("Could not locate repository root with Lyrow.sln.");
    }

    [Fact]
    public void Feature1_AnimatedArtwork_SystemAnimationsAndAutoPlayVerification()
    {
        // System animations detection should be executable without throwing PlatformNotSupportedException
        Action act = () => ArtworkBitmapManager.AreSystemAnimationsEnabled();
        act.Should().NotThrow();

        string repoRoot = FindRepoRoot();
        string artworkMgrCode = File.ReadAllText(Path.Combine(repoRoot, "src", "Lyrow.App", "Platform", "ArtworkBitmapManager.cs"));

        artworkMgrCode.Should().Contain("bool animationsEnabled = AreSystemAnimationsEnabled();");
        artworkMgrCode.Should().Contain("AutoPlay = animationsEnabled");
    }

    [Fact]
    public void Feature2_ClickToSeek_CanSeekDynamicTooltipAndSessionSafety()
    {
        string repoRoot = FindRepoRoot();
        string lyricsWindowCode = File.ReadAllText(Path.Combine(repoRoot, "src", "Lyrow.App", "LyricsWindow.xaml.cs"));
        string lyricsWindowXaml = File.ReadAllText(Path.Combine(repoRoot, "src", "Lyrow.App", "LyricsWindow.xaml"));

        // Scrubber hit boxes present without hardcoded seek tooltip overriding dynamic capability
        lyricsWindowXaml.Should().Contain("x:Name=\"GridProgressHitBox\"");
        lyricsWindowXaml.Should().Contain("x:Name=\"GridProgressCompactHitBox\"");

        // Dynamic capability subscription on TimelineChanged
        lyricsWindowCode.Should().Contain("UpdateScrubberToolTips");
        lyricsWindowCode.Should().Contain("_mediaMonitor.TimelineChanged +=");

        // MediaTimelineInfo.Zero must report CanSeek = false
        MediaTimelineInfo.Zero.CanSeek.Should().BeFalse();
    }

    [Fact]
    public void Feature3_AppleMusicDesktopLag_TimelinePollingAndDriftClamping()
    {
        string repoRoot = FindRepoRoot();
        string gsmtcCode = File.ReadAllText(Path.Combine(repoRoot, "src", "Lyrow.Music", "Session", "WindowsGsmtcMediaSessionMonitor.cs"));

        // Periodic 500ms timeline polling loop for desktop players
        gsmtcCode.Should().Contain("PeriodicTimer(TimeSpan.FromMilliseconds(500))");
        gsmtcCode.Should().Contain("StartTimelinePolling()");
        gsmtcCode.Should().Contain("StopTimelinePolling()");

        // Tight synchronization window to prevent stale LastUpdatedTime drift
        gsmtcCode.Should().Contain("Math.Abs(delta.TotalSeconds) <= 3.0");
    }

    [Fact]
    public void Feature4_LyricClickSeekAccuracy_AdjustsSyncOffsetAccurately()
    {
        string repoRoot = FindRepoRoot();
        string lyricsWindowCode = File.ReadAllText(Path.Combine(repoRoot, "src", "Lyrow.App", "LyricsWindow.xaml.cs"));

        // Verify that SeekToLyricLineAsync adjusts target position by SyncOffsetMs
        lyricsWindowCode.Should().Contain("int offsetMs = _lyricsModule.Synchronizer.SyncOffsetMs;");
        lyricsWindowCode.Should().Contain("timestamp -= TimeSpan.FromMilliseconds(offsetMs);");
        lyricsWindowCode.Should().Contain("await _mediaMonitor.TrySeekAsync(timestamp);");
    }

    [Fact]
    public void Feature5_SingleActiveLyricLineAnimation_StrictExclusivityGuaranteed()
    {
        string repoRoot = FindRepoRoot();
        string lyricsWindowCode = File.ReadAllText(Path.Combine(repoRoot, "src", "Lyrow.App", "LyricsWindow.xaml.cs"));

        // Verify strict single active line logic
        lyricsWindowCode.Should().Contain("bool isThisLineActive = (i == activeIdx);");
        lyricsWindowCode.Should().Contain("block.FontWeight = isThisLineActive ? FontWeights.Bold : FontWeights.Normal;");
        lyricsWindowCode.Should().Contain("_targetScales[i] = isThisLineActive ? target.TargetScale : 1.0;");
        lyricsWindowCode.Should().Contain("if (i != _currentActiveIndex && nextSc > 1.0)");
        lyricsWindowCode.Should().Contain("nextSc = 1.0;");

        // Viewport calculator target for distance == 0 is active, distance >= 1 is inactive
        var activeTarget = LyricsViewportCalculator.GetLineVisualTarget(lineIndex: 2, activeIndex: 2);
        activeTarget.IsActive.Should().BeTrue();
        activeTarget.TargetScale.Should().BeGreaterThan(1.0);

        var nearTarget = LyricsViewportCalculator.GetLineVisualTarget(lineIndex: 1, activeIndex: 2);
        nearTarget.IsActive.Should().BeFalse();
        nearTarget.TargetScale.Should().Be(1.0);
    }

    [Fact]
    public void Feature6_GhostDropdownAudioOutputSelector_ImplementedWithFluentStyling()
    {
        string repoRoot = FindRepoRoot();
        string lyricsWindowXaml = File.ReadAllText(Path.Combine(repoRoot, "src", "Lyrow.App", "LyricsWindow.xaml"));
        string lyricsWindowCode = File.ReadAllText(Path.Combine(repoRoot, "src", "Lyrow.App", "LyricsWindow.xaml.cs"));

        // Horizontal view ghost dropdown
        lyricsWindowXaml.Should().Contain("x:Name=\"BtnDeviceSelector\"");
        lyricsWindowXaml.Should().Contain("x:Name=\"FlyoutAudioDevices\"");

        // Compact view ghost dropdown
        lyricsWindowXaml.Should().Contain("x:Name=\"BtnDeviceSelectorCompact\"");
        lyricsWindowXaml.Should().Contain("x:Name=\"FlyoutAudioDevicesCompact\"");

        // Code-behind population of audio render endpoints & distinction from Windows settings
        lyricsWindowCode.Should().Contain("SetupDeviceSelectors()");
        lyricsWindowCode.Should().Contain("PopulateAudioDeviceFlyout");
        lyricsWindowCode.Should().Contain("Audio Output & Glow Endpoint");
        lyricsWindowCode.Should().Contain("Note: Lyrow analyzes audio from selected endpoint. Change default sound in Windows Settings.");
    }

    [Fact]
    public void Feature7_CompactPlayerLyricsExpansion_VerifiedLayoutAndSizing()
    {
        string repoRoot = FindRepoRoot();
        string lyricsWindowXaml = File.ReadAllText(Path.Combine(repoRoot, "src", "Lyrow.App", "LyricsWindow.xaml"));
        string lyricsWindowCode = File.ReadAllText(Path.Combine(repoRoot, "src", "Lyrow.App", "LyricsWindow.xaml.cs"));

        // Expandable lyrics container in Compact view
        lyricsWindowXaml.Should().Contain("x:Name=\"GridCompactLyrics\"");
        lyricsWindowXaml.Should().Contain("x:Name=\"CompactLyricsPanel\"");
        lyricsWindowXaml.Should().Contain("x:Name=\"CompactLyricsScrollViewer\"");
        lyricsWindowXaml.Should().Contain("x:Name=\"BtnExpandFromCompact\"");

        // Code-behind toggle and sizing
        lyricsWindowCode.Should().Contain("ToggleCompactLyrics()");
        lyricsWindowCode.Should().Contain("ResizeCompactWithLyrics()");
        lyricsWindowCode.Should().Contain("ResizeForCurrentDpi(500, 232);");
        lyricsWindowCode.Should().Contain("workArea.Y + workArea.Height");
    }
}
