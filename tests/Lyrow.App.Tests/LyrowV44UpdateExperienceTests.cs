using FluentAssertions;
using Xunit;

namespace Lyrow.App.Tests;

public sealed class LyrowV44UpdateExperienceTests
{
    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Lyrow.sln")))
        {
            dir = dir.Parent;
        }
        return dir?.FullName ?? throw new DirectoryNotFoundException("Lyrow.sln not found.");
    }

    [Fact]
    public void TopRightToastContainer_IsAnchoredTopRightAcrossNormalFullscreenAndCompactModes()
    {
        string root = FindRepoRoot();
        string xaml = File.ReadAllText(Path.Combine(root, "src", "Lyrow.App", "LyricsWindow.xaml"));

        int viewportIdx = xaml.IndexOf("x:Name=\"MainViewportArea\"", StringComparison.Ordinal);
        int compactIdx = xaml.IndexOf("x:Name=\"CompactPlayerView\"", StringComparison.Ordinal);
        int toastIdx = xaml.IndexOf("x:Name=\"LyricsToastContainer\"", StringComparison.Ordinal);

        viewportIdx.Should().BeGreaterThan(0);
        compactIdx.Should().BeGreaterThan(viewportIdx);
        toastIdx.Should().BeGreaterThan(compactIdx, "LyricsToastContainer must sit above both HorizontalView and CompactPlayerView inside MainViewportArea");

        string toastTag = xaml.Substring(toastIdx, Math.Min(350, xaml.Length - toastIdx));
        toastTag.Should().Contain("VerticalAlignment=\"Top\"");
        toastTag.Should().Contain("HorizontalAlignment=\"Right\"");
        toastTag.Should().Contain("Canvas.ZIndex=\"1000\"");
    }

    [Fact]
    public void UpdateToast_UsesFluentThemeSurfaceShadowAccessibilityAndKeyboardDismiss()
    {
        string root = FindRepoRoot();
        string toastCs = File.ReadAllText(Path.Combine(root, "src", "Lyrow.App", "Controls", "ToastNotificationManager.cs"));

        toastCs.Should().Contain("LyrowModalSurfaceBrush");
        toastCs.Should().Contain("LyrowModalBorderBrush");
        toastCs.Should().Contain("_container.ActualTheme == ElementTheme.Light");
        toastCs.Should().Contain("new ThemeShadow()");
        toastCs.Should().Contain("MaxWidth = 340");
        toastCs.Should().Contain("HorizontalAlignment = HorizontalAlignment.Right");
        toastCs.Should().Contain("AutomationProperties.SetName(updateBtn");
        toastCs.Should().Contain("AutomationProperties.SetName(dismissBtn");
        toastCs.Should().Contain("Windows.System.VirtualKey.Escape");
        toastCs.Should().Contain("Action? onDismiss = null");
    }

    [Fact]
    public void SettingsUpdatesTab_HasCheckingIndicatorDetailsCardInstallRetryAndReleaseLink()
    {
        string root = FindRepoRoot();
        string xaml = File.ReadAllText(Path.Combine(root, "src", "Lyrow.App", "LyricsWindow.xaml"));

        xaml.Should().Contain("x:Name=\"RingModalUpdateChecking\"");
        xaml.Should().Contain("x:Name=\"BorderModalUpdateDetails\"");
        xaml.Should().Contain("x:Name=\"TxtModalUpdateVersionTitle\"");
        xaml.Should().Contain("x:Name=\"TxtModalUpdatePublishedDate\"");
        xaml.Should().Contain("x:Name=\"TxtModalUpdateReleaseNotes\"");
        xaml.Should().Contain("x:Name=\"ProgressModalUpdate\"");
        xaml.Should().Contain("x:Name=\"TxtModalUpdateProgressDetails\"");
        xaml.Should().Contain("x:Name=\"TxtModalUpdateError\"");
        xaml.Should().Contain("x:Name=\"BtnModalInstallUpdate\"");
        xaml.Should().Contain("x:Name=\"BtnModalRetryUpdate\"");
        xaml.Should().Contain("x:Name=\"BtnModalViewRelease\"");
    }

    [Fact]
    public void ToastAndSettings_ShareSingleUpdateCoordinatorFlow_AndCurrentVersion44()
    {
        string root = FindRepoRoot();
        string appCs = File.ReadAllText(Path.Combine(root, "src", "Lyrow.App", "App.xaml.cs"));
        string v4Cs = File.ReadAllText(Path.Combine(root, "src", "Lyrow.App", "LyricsWindow.V4Features.cs"));

        appCs.Should().Contain("public const string CurrentReleaseVersion = \"5.0\";");
        appCs.Should().Contain("new GitHubUpdateService(currentVersion: CurrentReleaseVersion)");
        appCs.Should().Contain("services.AddSingleton<UpdateCoordinator>");

        v4Cs.Should().Contain("InitializeUpdateWorkflow();");
        v4Cs.Should().Contain("_updateCoordinator.UpdateAvailableToastRequested +=");
        v4Cs.Should().Contain("onStartDownload: async _ => await StartSharedUpdateInstallAsync()");
        v4Cs.Should().Contain("BtnModalInstallUpdate.Click += async (_, _) =>");
        v4Cs.Should().Contain("_updateCoordinator?.DismissToastForSession();");
        v4Cs.Should().Contain("BtnModalInstallUpdate.Content = loc[\"Updates_InstallNewVersion\"];");
    }
}
