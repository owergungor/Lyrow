using System;
using System.IO;
using System.Text.RegularExpressions;
using FluentAssertions;
using Lyrow.Audio.Control;
using Lyrow.Core.Models;
using Lyrow.Core.Services;
using Xunit;

namespace Lyrow.App.Tests;

public class LyrowV33FeatureVerificationTests
{
    [Fact]
    public void HorizontalLyricsWindow_Enforces5050Split_And_LeftPlayerHierarchy()
    {
        string repoRoot = FindRepoRoot();
        string xaml = File.ReadAllText(Path.Combine(repoRoot, "src", "Lyrow.App", "LyricsWindow.xaml"));

        // 50/50 split columns
        xaml.Should().Contain("x:Name=\"ColPlayer\" Width=\"*\"");
        xaml.Should().Contain("x:Name=\"ColLyrics\" Width=\"*\"");

        // Player column container is horizontally centered (so when ColLyrics is 0, Player centers across entire window)
        xaml.Should().Contain("x:Name=\"PlayerColumnContainer\" Grid.Column=\"0\" HorizontalAlignment=\"Center\"");
        xaml.Should().Contain("x:Name=\"PlayerInnerPanel\" Width=\"300\"");

        // Artwork & Left-aligned metadata matching artwork width
        xaml.Should().Contain("x:Name=\"ImgTrackArtwork\"");
        xaml.Should().Contain("x:Name=\"TxtSongTitle\"");
        xaml.Should().Contain("x:Name=\"TxtArtistAlbum\"");
        xaml.Should().Contain("x:Name=\"ProgressTimeline\" Width=\"300\"");
        xaml.Should().Contain("x:Name=\"TxtElapsed\"");
        xaml.Should().Contain("x:Name=\"TxtRemaining\"");

        // Centered playback device with dynamic icon
        xaml.Should().Contain("x:Name=\"IconDevice\"");
        xaml.Should().Contain("x:Name=\"TxtDevice\"");

        // Transport controls flanked by Dar Mod (Left) and Fullscreen (Right)
        xaml.Should().Contain("x:Name=\"BtnDarMod\" HorizontalAlignment=\"Left\"");
        xaml.Should().Contain("x:Name=\"BtnPrev\"");
        xaml.Should().Contain("x:Name=\"BtnPlayPause\"");
        xaml.Should().Contain("x:Name=\"BtnNext\"");
        xaml.Should().Contain("x:Name=\"BtnFullscreen\" HorizontalAlignment=\"Right\"");

        // Volume slider and Lyrics toggle button below volume bar at right edge
        xaml.Should().Contain("x:Name=\"SliderVolume\"");
        xaml.Should().Contain("x:Name=\"BtnToggleLyrics\" HorizontalAlignment=\"Right\"");
    }

    [Fact]
    public void CompactDarMod_FollowsAppleMusicMiniPlayerReference()
    {
        string repoRoot = FindRepoRoot();
        string xaml = File.ReadAllText(Path.Combine(repoRoot, "src", "Lyrow.App", "LyricsWindow.xaml"));

        xaml.Should().Contain("x:Name=\"CompactPlayerView\"");
        xaml.Should().Contain("x:Name=\"ImgTrackArtworkCompact\"");
        xaml.Should().Contain("x:Name=\"TxtSongTitleCompact\"");
        xaml.Should().Contain("x:Name=\"TxtArtistAlbumCompact\"");
        xaml.Should().Contain("x:Name=\"ProgressTimelineCompact\"");
        xaml.Should().Contain("x:Name=\"TxtElapsedCompact\"");
        xaml.Should().Contain("x:Name=\"TxtRemainingCompact\"");
        xaml.Should().Contain("x:Name=\"IconDeviceCompact\"");
        xaml.Should().Contain("x:Name=\"TxtDeviceCompact\"");
        xaml.Should().Contain("x:Name=\"BtnPrevCompact\"");
        xaml.Should().Contain("x:Name=\"BtnPlayPauseCompact\"");
        xaml.Should().Contain("x:Name=\"BtnNextCompact\"");
        xaml.Should().Contain("x:Name=\"BtnNormalModeCompact\"");
        xaml.Should().Contain("x:Name=\"BtnExpandFromCompact\"");
    }

    [Fact]
    public void LyricsToggle_CollapsesRightColumn_ToCenterPlayer_And_Restores5050Split()
    {
        string repoRoot = FindRepoRoot();
        string code = File.ReadAllText(Path.Combine(repoRoot, "src", "Lyrow.App", "LyricsWindow.xaml.cs"));

        code.Should().Contain("public void ToggleLyricsPanel()");
        code.Should().Contain("ColLyrics.Width = new GridLength(1, GridUnitType.Star);");
        code.Should().Contain("ColLyrics.Width = new GridLength(0);");
        code.Should().Contain("LyricsColumnContainer.Visibility = Visibility.Collapsed;");
        code.Should().Contain("LyricsColumnContainer.Visibility = Visibility.Visible;");
    }

    [Fact]
    public void BackgroundEngine_V32_IsPreservedCompletelyIntact()
    {
        string repoRoot = FindRepoRoot();
        string xaml = File.ReadAllText(Path.Combine(repoRoot, "src", "Lyrow.App", "LyricsWindow.xaml"));
        string code = File.ReadAllText(Path.Combine(repoRoot, "src", "Lyrow.App", "LyricsWindow.xaml.cs"));

        // XAML background layers preserved
        xaml.Should().Contain("x:Name=\"AmbientBackgroundLayer\"");
        xaml.Should().Contain("x:Name=\"AmbientBrush\"");
        xaml.Should().Contain("x:Name=\"StopTopLeft\"");
        xaml.Should().Contain("x:Name=\"StopCenter\"");
        xaml.Should().Contain("x:Name=\"StopBottomRight\"");
        xaml.Should().Contain("<Rectangle Fill=\"#50000000\" />");
        xaml.Should().Contain("<RadialGradientBrush Center=\"0.5,0.5\" RadiusX=\"0.8\" RadiusY=\"0.8\">");

        // Code-behind crossfade preserved
        code.Should().Contain("AmbientBackgroundPalette.Lerp(_currentAmbientPalette, _targetAmbientPalette, 0.065f)");

        // Extractor luminance bound preserved
        ArtworkPaletteExtractor.MaxSafeLuminance.Should().Be(55.0);
    }

    [Fact]
    public void AudioVolumeService_ExposesOutputDeviceFriendlyName_And_Category()
    {
        using var service = new SyntheticAudioVolumeService();
        service.OutputDeviceFriendlyName.Should().NotBeNullOrWhiteSpace();
        service.OutputDeviceCategory.Should().Be(AudioDeviceCategory.Speakers);

        service.OutputDeviceFriendlyName = "Headphones (Realtek HD Audio)";
        service.OutputDeviceCategory = AudioDeviceCategory.Headphones;
        service.OutputDeviceFriendlyName.Should().Be("Headphones (Realtek HD Audio)");
        service.OutputDeviceCategory.Should().Be(AudioDeviceCategory.Headphones);
    }

    [Fact]
    public void DuplicateGhostPrevention_SingleCanonicalVisualText_PreservedInV33()
    {
        string repoRoot = FindRepoRoot();
        string code = File.ReadAllText(Path.Combine(repoRoot, "src", "Lyrow.App", "LyricsWindow.xaml.cs"));

        // Inactive lines collapse overlay and zero clip rect
        code.Should().Contain("overlay.Visibility = Visibility.Collapsed;");
        code.Should().Contain("new Windows.Foundation.Rect(0, 0, 0, 0)");

        // Post-layout measurement centering snap present
        code.Should().Contain("if (_snapToCenterOnNextLayout && _lineContainers[0].ActualHeight > 0)");
    }

    [Fact]
    public void Versioning_V33_StrictlyEnforcedAcrossRepository()
    {
        GitHubUpdateService.AppVersion.Should().Be("3.5");
        GitHubUpdateService.IsValidTwoPartVersionTag("v3.5").Should().BeTrue();
        GitHubUpdateService.IsValidTwoPartVersionTag("v3.5.0").Should().BeFalse();
        GitHubUpdateService.IsNewerVersion("3.4", "3.5").Should().BeTrue();

        string repoRoot = FindRepoRoot();
        File.ReadAllText(Path.Combine(repoRoot, "Directory.Build.props")).Should().Contain("<Version>3.5</Version>");
        File.ReadAllText(Path.Combine(repoRoot, "Directory.Build.props")).Should().Contain("<FileVersion>3.5.0.0</FileVersion>");
        File.ReadAllText(Path.Combine(repoRoot, "installer", "setup.iss")).Should().Contain("#define MyAppVersion \"3.5\"");
        File.ReadAllText(Path.Combine(repoRoot, "src", "Lyrow.App", "app.manifest")).Should().Contain("version=\"3.5.0.0\"");
        File.ReadAllText(Path.Combine(repoRoot, ".github", "workflows", "release.yml")).Should().Contain("default: 'v3.5'");
    }

    private static string FindRepoRoot()
    {
        string current = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(current))
        {
            if (File.Exists(Path.Combine(current, "Lyrow.sln")))
            {
                return current;
            }
            var parent = Directory.GetParent(current);
            if (parent == null) break;
            current = parent.FullName;
        }
        return Directory.GetCurrentDirectory();
    }
}
