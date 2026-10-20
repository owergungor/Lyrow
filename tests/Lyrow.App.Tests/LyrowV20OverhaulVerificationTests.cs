using FluentAssertions;
using Lyrow.Core.Lyrics;
using Xunit;

namespace Lyrow.App.Tests;

public class LyrowV20OverhaulVerificationTests
{
    private static string GetSolutionRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Lyrow.sln")))
        {
            dir = dir.Parent;
        }
        return dir?.FullName ?? throw new InvalidOperationException("Solution root not found.");
    }

    [Theory]
    [InlineData(0.0, 600.0, 300.0, 50.0, 25.0)]    // Line at Y=300 in viewport, height=50 -> center is 325 -> shift +25 to put at 300
    [InlineData(400.0, 600.0, 150.0, 40.0, 270.0)] // Line at Y=150 in viewport, current scroll=400 -> target=270
    [InlineData(50.0, 500.0, 100.0, 30.0, 0.0)]    // Clamped to 0 when near top
    public void LyricsViewportCalculator_CalculateTargetCenterOffsetFromViewportPoint_PlacesActiveLineExactlyAtVerticalCenter(
        double currentVerticalOffset,
        double viewportHeight,
        double lineTopInViewport,
        double lineHeight,
        double expectedTargetOffset)
    {
        double target = LyricsViewportCalculator.CalculateTargetCenterOffsetFromViewportPoint(
            currentVerticalOffset,
            viewportHeight,
            lineTopInViewport,
            lineHeight);

        target.Should().BeApproximately(expectedTargetOffset, 0.01);
    }

    [Fact]
    public void LyricsViewportCalculator_InterpolateScrollOffset_InterpolatesSmoothlyToTargetWithoutSuddenJump()
    {
        double current = 0.0;
        double target = 300.0;

        // First step should move toward target smoothly, not jump directly to 300
        double firstStep = LyricsViewportCalculator.InterpolateScrollOffset(current, target);
        firstStep.Should().BeGreaterThan(0.0);
        firstStep.Should().BeLessThan(target);

        // Over 45 frames (~750ms at 60 FPS), it should smoothly converge to target
        for (int i = 0; i < 45; i++)
        {
            current = LyricsViewportCalculator.InterpolateScrollOffset(current, target);
        }

        current.Should().BeApproximately(target, 0.5);
    }

    [Fact]
    public void LyricsViewportCalculator_GetLineVisualTarget_ProducesAppleMusicHierarchy()
    {
        var active = LyricsViewportCalculator.GetLineVisualTarget(lineIndex: 10, activeIndex: 10, fontScalePreset: 0);
        var near1 = LyricsViewportCalculator.GetLineVisualTarget(lineIndex: 11, activeIndex: 10, fontScalePreset: 0);
        var near2 = LyricsViewportCalculator.GetLineVisualTarget(lineIndex: 12, activeIndex: 10, fontScalePreset: 0);
        var far = LyricsViewportCalculator.GetLineVisualTarget(lineIndex: 16, activeIndex: 10, fontScalePreset: 0);

        active.IsActive.Should().BeTrue();
        active.TargetOpacity.Should().Be(1.0);
        active.TargetScale.Should().BeGreaterThan(1.0);

        // Hierarchy: Active > Near1 > Near2 > Far in Opacity, Scale, and FontSize
        near1.TargetOpacity.Should().BeLessThan(active.TargetOpacity);
        near1.TargetOpacity.Should().BeGreaterThan(near2.TargetOpacity);
        near2.TargetOpacity.Should().BeGreaterThan(far.TargetOpacity);

        active.FontSize.Should().BeGreaterThan(near1.FontSize);
        near1.FontSize.Should().BeGreaterThan(near2.FontSize);
        near2.FontSize.Should().BeGreaterThanOrEqualTo(far.FontSize);
    }

    [Fact]
    public void MainWindowXaml_AudioSignalBoxIsCompletelyRemoved_AndNoDbfsPresent()
    {
        string root = GetSolutionRoot();
        string xaml = File.ReadAllText(Path.Combine(root, "src", "Lyrow.App", "MainWindow.xaml"));
        string codeBehind = File.ReadAllText(Path.Combine(root, "src", "Lyrow.App", "MainWindow.xaml.cs"));

        xaml.Should().NotContain("Audio Signal");
        xaml.Should().NotContain("TxtAudioStatus");
        xaml.Should().NotContain("dBFS");

        codeBehind.Should().NotContain("TxtAudioStatus");
        codeBehind.Should().NotContain("OnAudioLevelUpdated");
        codeBehind.Should().NotContain("dBFS");
    }

    [Fact]
    public void MainWindowXaml_HasCompactSidebar_WorkingScrollViewer_AndAutoUpdateSection()
    {
        string root = GetSolutionRoot();
        string xaml = File.ReadAllText(Path.Combine(root, "src", "Lyrow.App", "MainWindow.xaml"));
        string codeBehind = File.ReadAllText(Path.Combine(root, "src", "Lyrow.App", "MainWindow.xaml.cs"));

        // Compact sidebar (< 200px open pane)
        xaml.Should().Contain("OpenPaneLength=\"188\"");
        xaml.Should().Contain("CompactPaneLength=\"48\"");

        // ScrollViewer mouse wheel + touchpad + keyboard support
        xaml.Should().Contain("x:Name=\"MainScrollViewer\"");
        xaml.Should().Contain("VerticalScrollBarVisibility=\"Auto\"");
        xaml.Should().Contain("VerticalScrollMode=\"Enabled\"");
        codeBehind.Should().Contain("OnMainScrollViewerPointerWheelChanged");
        codeBehind.Should().Contain("OnShellKeyDown");

        // Auto Update section in Settings
        xaml.Should().Contain("x:Name=\"SwitchAutoUpdate\"");
        xaml.Should().Contain("x:Name=\"BtnCheckUpdates\"");
        xaml.Should().Contain("x:Name=\"UpdateStatusInfoBar\"");
    }

    [Fact]
    public void AppManifest_DeclaresPerMonitorV2DpiAwareness()
    {
        string root = GetSolutionRoot();
        string manifest = File.ReadAllText(Path.Combine(root, "src", "Lyrow.App", "app.manifest"));

        manifest.Should().Contain("PerMonitorV2");
    }

    [Fact]
    public void Versioning_StrictTwoPartVersion20EnforcedAcrossProjectInstallerWorkflowAndDocs()
    {
        string root = GetSolutionRoot();

        string buildProps = File.ReadAllText(Path.Combine(root, "Directory.Build.props"));
        buildProps.Should().Contain("<Version>2.0</Version>");
        buildProps.Should().Contain("<PackageVersion>2.0</PackageVersion>");
        buildProps.Should().Contain("<InformationalVersion>2.0</InformationalVersion>");
        buildProps.Should().NotContain("2.0.0</Version>");

        string setupIss = File.ReadAllText(Path.Combine(root, "installer", "setup.iss"));
        setupIss.Should().Contain("#define MyAppVersion \"2.0\"");
        setupIss.Should().NotContain("2.0.0");

        string releaseYml = File.ReadAllText(Path.Combine(root, ".github", "workflows", "release.yml"));
        releaseYml.Should().NotContain("2.0.0");
        releaseYml.Should().NotContain("v1.3.0");

        string readme = File.ReadAllText(Path.Combine(root, "README.md"));
        readme.Should().Contain("Lyrow v2.0");
        readme.Should().Contain("Lyrow-2.0-Setup-x64.exe");
        readme.Should().Contain("Lyrow-v2.0-win-x64-portable.zip");
        readme.Should().NotContain("v2.0.0");
    }
}
