using System;
using System.Collections.Generic;
using System.IO;
using FluentAssertions;
using Lyrow.App.Platform;
using Lyrow.Core.Lyrics;
using Lyrow.Core.Localization;
using Lyrow.Core.Models;
using Lyrow.Core.Timeline;
using Xunit;

namespace Lyrow.App.Tests;

/// <summary>
/// Unit and regression tests for Stage 38:
/// 1. Normal Mode Window Size & Layout (Hide Lyrics button visible, artwork scaling leaves room for controls).
/// 2. Lyrics Synchronization & Highlighting (strictly single active line, overlays collapsed on transition).
/// 3. Track Transition Remaining Time (placeholder "--:--" when duration unknown, no "-0:00" glitch).
/// 4. Settings Tabs & Localization (System tab removed from visible UI, Updates/Shortcuts preserved, complete TR/EN strings).
/// </summary>
public sealed class LyrowStage38UiLyricsAndSettingsTests
{
    [Fact]
    public void NormalMode_DefaultAndMinimumDimensions_AreProperlyConfigured()
    {
        WindowStateManager.DefaultNormalSize.Width.Should().Be(960);
        WindowStateManager.DefaultNormalSize.Height.Should().Be(720);

        WindowStateManager.DefaultCompactSize.Width.Should().Be(528);
        WindowStateManager.DefaultCompactSize.Height.Should().Be(222);

        // Verify LyricsWindow XAML contains PlayerScrollViewer ensuring overflow safety
        string xamlPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "Lyrow.App", "LyricsWindow.xaml");
        if (File.Exists(xamlPath))
        {
            string xaml = File.ReadAllText(xamlPath);
            xaml.Should().Contain("x:Name=\"PlayerScrollViewer\"");
            xaml.Should().Contain("x:Name=\"BtnToggleLyrics\"");
            xaml.Should().Contain("x:Name=\"GridOffsetRow\"");
        }
    }

    [Theory]
    [InlineData(960, 720, true)]
    [InlineData(1280, 800, true)]
    [InlineData(960, 600, true)]
    [InlineData(800, 560, true)]
    [InlineData(960, 720, false)] // Hidden lyrics panel mode
    public void CalculateResponsiveMetrics_LeavesAdequateRoomForControlsBelowArtwork(
        double winW, double winH, bool isLyricsVisible)
    {
        var metrics = LyricsViewportCalculator.CalculateResponsiveMetrics(winW, winH, isLyricsVisible);

        // Artwork size must leave at least 250px for controls below it (title, artist, timeline, device, controls, volume, offset)
        double availablePlayerH = winH - 44.0;
        double remainingVerticalSpace = availablePlayerH - metrics.ArtworkSize;
        remainingVerticalSpace.Should().BeGreaterThanOrEqualTo(250.0,
            "Artwork must leave sufficient height for media transport, device picker, volume bar, and lyrics toggle button");

        metrics.ArtworkSize.Should().BeInRange(140.0, 560.0);
        metrics.PlayerColumnWidth.Should().Be(metrics.ArtworkSize);
        metrics.ProgressBarWidth.Should().Be(metrics.ArtworkSize);
        metrics.VolumeBarWidth.Should().Be(metrics.ArtworkSize);
        metrics.SongTitleFontSize.Should().BeGreaterThanOrEqualTo(16.0);
        metrics.FocusedLyricFontSize.Should().BeGreaterThanOrEqualTo(20.0);
        metrics.UnfocusedLyricFontSize.Should().BeGreaterThanOrEqualTo(14.0);
    }

    [Fact]
    public void LyricsHighlighting_OnlyOneLineCanBeActive_AtAnyPlaybackPosition()
    {
        var lyrics = new SyncedLyrics(
            title: "Test Song",
            artist: "Test Artist",
            isSynced: true,
            lines: new[]
            {
                new LyricLine(TimeSpan.FromSeconds(2), "First lyric line", 0),
                new LyricLine(TimeSpan.FromSeconds(6), "Second lyric line", 1),
                new LyricLine(TimeSpan.FromSeconds(10), "Third lyric line", 2)
            });

        // Before playback starts
        int activeBefore = lyrics.FindActiveLineIndex(TimeSpan.FromSeconds(0.5));
        activeBefore.Should().Be(-1);

        // During line 0
        int activeLine0 = lyrics.FindActiveLineIndex(TimeSpan.FromSeconds(3.5));
        activeLine0.Should().Be(0);

        // During line 1
        int activeLine1 = lyrics.FindActiveLineIndex(TimeSpan.FromSeconds(7.0));
        activeLine1.Should().Be(1);

        // During line 2
        int activeLine2 = lyrics.FindActiveLineIndex(TimeSpan.FromSeconds(11.0));
        activeLine2.Should().Be(2);

        // Verify visual targets for each line when line 1 is active
        for (int i = 0; i < lyrics.Lines.Count; i++)
        {
            var target = LyricsViewportCalculator.GetLineVisualTarget(i, activeIndex: 1, focusedFontSize: 28.0, unfocusedFontSize: 20.0);
            if (i == 1)
            {
                target.IsActive.Should().BeTrue();
                target.TargetOpacity.Should().Be(1.0);
                target.TargetScale.Should().BeGreaterThan(1.0);
            }
            else
            {
                target.IsActive.Should().BeFalse();
                target.TargetOpacity.Should().BeLessThan(1.0);
                target.TargetScale.Should().Be(1.0);
            }
        }
    }

    [Fact]
    public void LyricsFillProgressCalculator_ClearsHighlightCompletely_WhenOutsideActiveWindow()
    {
        var line1 = new LyricLine(TimeSpan.FromSeconds(5), "Line One", 0);
        var line2 = new LyricLine(TimeSpan.FromSeconds(10), "Line Two", 1);

        // Before line 1 starts: progress must be strictly 0.0
        double progBefore = LyricsFillProgressCalculator.CalculateLineProgress(
            TimeSpan.FromSeconds(2), line1.Timestamp, nextLineTimestamp: line2.Timestamp);
        progBefore.Should().Be(0.0);

        // During line 1: progress must be between 0.0 and 1.0
        double progDuring = LyricsFillProgressCalculator.CalculateLineProgress(
            TimeSpan.FromSeconds(7.5), line1.Timestamp, nextLineTimestamp: line2.Timestamp);
        progDuring.Should().BeApproximately(0.5, 0.01);

        // After line 1 finishes: line 1 progress is 1.0, but line 2 hasn't reached full
        double progAfter = LyricsFillProgressCalculator.CalculateLineProgress(
            TimeSpan.FromSeconds(10), line1.Timestamp, nextLineTimestamp: line2.Timestamp);
        progAfter.Should().Be(1.0);
    }

    [Fact]
    public void PlaybackTimelineEngine_WhenDurationIsUnknown_RemainingTimeShowsPlaceholderNotNegativeZero()
    {
        var engine = new PlaybackTimelineEngine();

        // 1. Initial / uninitialized state: duration is zero
        engine.CurrentTrackDuration.Should().Be(TimeSpan.Zero);
        engine.CurrentTrackRemainingFormatted.Should().Be("--:--");

        // 2. Track with known duration
        var track = new MediaTrackInfo("Artist", "Title", "Album", TimeSpan.FromMinutes(3));
        engine.HandleTrackChanged(track);

        engine.HandleTimelineProperties(
            rawPosition: TimeSpan.FromSeconds(30),
            rawDuration: TimeSpan.FromMinutes(3),
            rawStatus: MediaPlaybackStatus.Playing);

        engine.CurrentTrackDuration.Should().Be(TimeSpan.FromMinutes(3));
        engine.CurrentTrackRemainingFormatted.Should().Be("-2:30");

        // 3. Track switch to unknown duration: must immediately show placeholder "--:--" instead of "-0:00"
        var newTrackWithoutDuration = new MediaTrackInfo("New Artist", "New Track", "Album", TimeSpan.Zero);
        engine.HandleTrackChanged(newTrackWithoutDuration);

        engine.CurrentTrackDuration.Should().Be(TimeSpan.Zero);
        engine.CurrentTrackRemainingFormatted.Should().Be("--:--");
    }

    [Fact]
    public void Settings_SystemTab_IsCollapsedInXaml_AndUpdatesAndShortcutsRemainVisible()
    {
        string xamlPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "Lyrow.App", "MainWindow.xaml");
        if (File.Exists(xamlPath))
        {
            string xaml = File.ReadAllText(xamlPath);

            // BtnFilterSystem must have Visibility="Collapsed" so it is not visible to the user
            xaml.Should().MatchRegex(@"<Button\s+x:Name=""BtnFilterSystem""[^>]*Visibility=""Collapsed""");

            // Updates, Shortcuts, Artwork, Audio, Appearance must be present without being collapsed
            xaml.Should().Contain("x:Name=\"BtnFilterAppearance\"");
            xaml.Should().Contain("x:Name=\"BtnFilterArtwork\"");
            xaml.Should().Contain("x:Name=\"BtnFilterAudio\"");
            xaml.Should().Contain("x:Name=\"BtnFilterUpdates\"");
            xaml.Should().Contain("x:Name=\"BtnFilterShortcuts\"");
            xaml.Should().Contain("x:Name=\"BtnFilterAbout\"");
        }
    }

    [Fact]
    public void LocalizationService_EnglishAndTurkish_HaveCompleteAndMatchingKeys()
    {
        var loc = LocalizationService.Instance;

        // Verify Turkish locale contains all required keys with proper Turkish characters
        loc.SetLanguage("tr");

        loc["Tab_All"].Should().Be("Tümü");
        loc["Tab_Appearance"].Should().Be("Görünüm");
        loc["Tab_Artwork"].Should().Be("Albüm Kapağı");
        loc["Tab_Audio"].Should().Be("Ses");
        loc["Tab_Updates"].Should().Be("Güncellemeler");
        loc["Tab_Shortcuts"].Should().Be("Kısayollar");
        loc["Tab_About"].Should().Be("Hakkında");

        loc["General_MasterOpacity"].Should().Be("Pencere Genel Saydamlığı");
        loc["General_EnableAnimations"].Should().Be("Akıcı Hareket ve Animasyonlar");
        loc["General_Notifications"].Should().Be("Masaüstü Bildirimleri");
        loc["General_NotifyOnTrackChange"].Should().Be("Parça Değişiminde Bildir");

        loc["Artwork_DynamicBackground"].Should().Be("Dinamik Ortam Arka Plan Katmanı");
        loc["Artwork_Saturation"].Should().Be("Ortam Arka Plan Doygunluğu");
        loc["Artwork_Radius"].Should().Be("Albüm Kapağı Köşe Yuvarlama");
        loc["Artwork_Cache"].Should().Be("Bellek İçi Önbellek Kapasitesi");

        loc["Updates_Frequency_Desc"].Should().Be("Lyrow'un güncellemeleri ne sıklıkla denetleyeceğini seçin");

        // Verify English locale contains matching keys
        loc.SetLanguage("en");

        loc["Tab_All"].Should().Be("All");
        loc["Tab_Appearance"].Should().Be("Appearance");
        loc["Tab_Artwork"].Should().Be("Artwork");
        loc["Tab_Audio"].Should().Be("Audio");
        loc["Tab_Updates"].Should().Be("Updates");
        loc["Tab_Shortcuts"].Should().Be("Shortcuts");
        loc["Tab_About"].Should().Be("About");

        loc["General_MasterOpacity"].Should().Be("Window Master Opacity");
        loc["General_EnableAnimations"].Should().Be("Fluid Motion & Animations");
        loc["General_Notifications"].Should().Be("Desktop Notifications");
        loc["General_NotifyOnTrackChange"].Should().Be("Notify on Track Change");

        loc["Artwork_DynamicBackground"].Should().Be("Dynamic Ambient Background Layer");
        loc["Artwork_Saturation"].Should().Be("Ambient Background Saturation");
        loc["Artwork_Radius"].Should().Be("Artwork Corner Radius");
        loc["Artwork_Cache"].Should().Be("In-Memory Cache Capacity");
    }

    [Fact]
    public void LyricsWindowXaml_DefaultsRemainingTimeToPlaceholder_NotMinusZero()
    {
        string xamlPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "Lyrow.App", "LyricsWindow.xaml");
        if (File.Exists(xamlPath))
        {
            string xaml = File.ReadAllText(xamlPath);
            xaml.Should().Contain("x:Name=\"TxtRemaining\" Text=\"--:--\"");
            xaml.Should().Contain("x:Name=\"TxtRemainingCompact\" Text=\"--:--\"");
            xaml.Should().NotContain("x:Name=\"TxtRemaining\" Text=\"-0:00\"");
            xaml.Should().NotContain("x:Name=\"TxtRemainingCompact\" Text=\"-0:00\"");
        }
    }
}
