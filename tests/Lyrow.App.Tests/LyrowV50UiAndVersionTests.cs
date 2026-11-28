using Lyrow.Core.Localization;
using Lyrow.Core.Lyrics;
using Lyrow.Core.Models;
using Xunit;

namespace Lyrow.App.Tests;

public class LyrowV50UiAndVersionTests
{
    private static string RepoRoot => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    [Fact]
    public void SettingsModel_DefaultSyncModeIsAutomaticAndPersistsAllModes()
    {
        var defaults = LyrowSettings.CreateDefault();
        Assert.Equal(LyricsSyncMode.Automatic, defaults.Lyrics.SyncMode);

        defaults.Lyrics.SyncMode = LyricsSyncMode.Syllable;
        Assert.Equal(LyricsSyncMode.Syllable, defaults.Lyrics.SyncMode);
    }

    [Fact]
    public void SettingsModal_UsesAutomaticSyncWithoutManualSyncModeDropdown()
    {
        string xaml = File.ReadAllText(Path.Combine(RepoRoot, "src", "Lyrow.App", "LyricsWindow.xaml"));
        Assert.DoesNotContain("x:Name=\"ComboModalLyricsSyncMode\"", xaml);
        Assert.Contains("x:Name=\"BtnModalTabOverview\"", xaml);
        Assert.Contains("x:Name=\"ModalSectionOverview\"", xaml);
        Assert.Contains("x:Name=\"BtnOffsetMinus\"", xaml);
        Assert.Contains("x:Name=\"BtnOffsetPlus\"", xaml);
    }

    [Fact]
    public void Localization_ContainsEnglishAndTurkishLyricsSyncStrings()
    {
        var loc = LocalizationService.Instance;
        loc.SetLanguage("en");
        Assert.Equal("Lyrics Synchronization", loc["Settings_LyricsSyncMode"]);
        Assert.Equal("Automatic", loc["LyricsSync_Automatic"]);
        Assert.Equal("Syllable", loc["LyricsSync_Syllable"]);
        Assert.Equal("Word", loc["LyricsSync_Word"]);
        Assert.Equal("Line", loc["LyricsSync_Line"]);
        Assert.Equal("Asynchronous", loc["LyricsSync_Asynchronous"]);

        loc.SetLanguage("tr");
        Assert.Equal("Şarkı Sözü Senkronizasyonu", loc["Settings_LyricsSyncMode"]);
        Assert.Equal("Otomatik", loc["LyricsSync_Automatic"]);
        Assert.Equal("Hece", loc["LyricsSync_Syllable"]);
        Assert.Equal("Kelime", loc["LyricsSync_Word"]);
        Assert.Equal("Satır", loc["LyricsSync_Line"]);
        Assert.Equal("Asenkron", loc["LyricsSync_Asynchronous"]);

        loc.SetLanguage("en");
    }

    [Fact]
    public void VersionAlignment_Is50AcrossAllProjectAndPackagingFiles()
    {
        Assert.Contains("<Version>5.0</Version>", File.ReadAllText(Path.Combine(RepoRoot, "Directory.Build.props")));
        Assert.Contains("<AssemblyVersion>5.0.0.0</AssemblyVersion>", File.ReadAllText(Path.Combine(RepoRoot, "Directory.Build.props")));
        Assert.Contains("<Version>5.0</Version>", File.ReadAllText(Path.Combine(RepoRoot, "src", "Lyrow.App", "Lyrow.App.csproj")));
        Assert.Contains("CurrentReleaseVersion = \"5.0\"", File.ReadAllText(Path.Combine(RepoRoot, "src", "Lyrow.App", "App.xaml.cs")));
        Assert.Contains("AppVersion = \"5.0\"", File.ReadAllText(Path.Combine(RepoRoot, "src", "Lyrow.Core", "Services", "GitHubUpdateService.cs")));
        Assert.Contains("MyAppVersion \"5.0\"", File.ReadAllText(Path.Combine(RepoRoot, "installer", "setup.iss")));
        Assert.Contains("$Version = \"5.0\"", File.ReadAllText(Path.Combine(RepoRoot, "installer", "build_packages.ps1")));
    }
}
