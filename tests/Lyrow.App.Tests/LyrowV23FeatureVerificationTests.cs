using System.Net;
using System.Reflection;
using Lyrow.Audio.Control;
using Lyrow.Core.Contracts;
using Lyrow.Core.Localization;
using Lyrow.Core.Models;
using Lyrow.Core.Services;
using Lyrow.Music.Session;
using Xunit;

namespace Lyrow.App.Tests;

public sealed class LyrowV23FeatureVerificationTests
{
    [Fact]
    public void Versioning_IsStrictlyTwoPart_V23()
    {
        Assert.Equal("2.7", GitHubUpdateService.AppVersion);
        Assert.True(GitHubUpdateService.IsValidTwoPartVersionTag("v2.6"));
        Assert.False(GitHubUpdateService.IsValidTwoPartVersionTag("v2.6.0"));
        Assert.False(GitHubUpdateService.IsValidTwoPartVersionTag("2.6.0"));
        Assert.False(GitHubUpdateService.IsValidTwoPartVersionTag("v2.6.1"));
        Assert.True(GitHubUpdateService.IsNewerVersion("2.5", "2.6"));
        Assert.False(GitHubUpdateService.IsNewerVersion("2.6", "2.6"));
    }

    [Fact]
    public void AutoCenter_Property_RemovedFromLyricsModuleSettings()
    {
        var prop = typeof(LyricsModuleSettings).GetProperty("AutoScroll", BindingFlags.Public | BindingFlags.Instance);
        Assert.Null(prop);
    }

    [Fact]
    public void Localization_AllFiveLanguages_HaveNoMissingKeys()
    {
        var loc = new LocalizationService();
        var supported = LocalizationService.SupportedLanguages.Select(l => l.Code).ToList();
        Assert.Contains("en", supported);
        Assert.Contains("fr", supported);
        Assert.Contains("es", supported);
        Assert.Contains("de", supported);
        Assert.Contains("tr", supported);
        Assert.Equal(5, supported.Count);

        string[] coreKeys =
        {
            "Nav_Home", "Nav_Playback", "Nav_Glow", "Nav_Lyrics", "Nav_Settings",
            "Settings_Title", "Tab_General", "Tab_Playback", "Tab_Lyrics", "Tab_Glow", "Tab_Updates", "Tab_Shortcuts",
            "General_AppTheme", "General_Language", "General_Startup", "General_SystemTray",
            "Playback_Title", "Playback_ActiveSession", "Playback_SystemEndpoint",
            "Lyrics_Title", "Lyrics_SyncOffset", "Lyrics_FocusedFontSize", "Lyrics_UnfocusedFontSize", "Lyrics_AutoCenter_Notice", "Lyrics_SeekFailed",
            "Glow_Title", "Glow_AmbientGlow", "Glow_Intensity", "Glow_ArtworkSync", "Glow_ShutdownDelay",
            "Updates_Title", "Updates_AutoChecks", "Updates_Frequency", "Updates_CurrentVersion", "Updates_CheckNow", "Updates_DownloadAndInstall",
            "Shortcuts_Title", "Shortcuts_ToggleGlow", "Shortcuts_ToggleLyrics", "Shortcuts_Apply",
            "Diag_ResetSettings", "Diag_OpenLogs"
        };

        foreach (var lang in supported)
        {
            loc.SetLanguage(lang);
            foreach (var key in coreKeys)
            {
                string text = loc.GetString(key);
                Assert.False(string.IsNullOrWhiteSpace(text), $"Key '{key}' is missing or empty in language '{lang}'");
                Assert.NotEqual(key, text); // Must not return raw key
            }
        }
    }

    [Fact]
    public void Localization_FallbackToEnglish_ForUnknownLanguageOrKey()
    {
        var loc = new LocalizationService();
        loc.SetLanguage("unknown_lang");
        Assert.Equal("en", loc.CurrentLanguage);

        string homeText = loc.GetString("Nav_Home");
        Assert.Equal("Home", homeText);
    }

    [Fact]
    public void Localization_RuntimeLanguageChange_RaisesEvent()
    {
        var loc = new LocalizationService();
        string target1 = loc.CurrentLanguage == "tr" ? "de" : "tr";
        string target2 = target1 == "de" ? "fr" : "de";

        string? changedTo = null;
        loc.LanguageChanged += (_, lang) => changedTo = lang;

        bool changed = loc.SetLanguage(target1);
        Assert.True(changed);
        Assert.Equal(target1, changedTo);
        Assert.Equal(target1, loc.CurrentLanguage);

        loc.SetLanguage(target2);
        Assert.Equal(target2, loc.CurrentLanguage);
    }

    [Fact]
    public async Task MediaSession_Seek_UpdatesTimelinePosition()
    {
        var monitor = new SyntheticMediaSessionMonitor();
        monitor.SimulateTrack("Test Title", "Test Artist", "Test Album", TimeSpan.FromMinutes(4));
        monitor.SimulatePlaybackState(MediaPlaybackStatus.Playing, TimeSpan.FromSeconds(10));

        var targetSeek = TimeSpan.FromSeconds(75);
        bool success = await monitor.TrySeekAsync(targetSeek);

        Assert.True(success);
        var currentPos = monitor.CurrentTimeline.GetInterpolatedPosition();
        Assert.True(Math.Abs((currentPos - targetSeek).TotalSeconds) < 2.0);
    }

    [Fact]
    public void DualVolume_Sync_BothSessionAndMaster()
    {
        var volumeService = new SyntheticAudioVolumeService();
        int lastReported = -1;
        volumeService.VolumeChanged += (_, val) => lastReported = val;

        volumeService.SetPlaybackVolumePercent(65);
        Assert.Equal(65, volumeService.GetPlaybackVolumePercent());
        Assert.Equal(65, lastReported);

        volumeService.SetSystemVolumePercent(42);
        Assert.Equal(42, volumeService.GetSystemVolumePercent());
        Assert.Equal(42, lastReported);
    }

    [Fact]
    public async Task InAppUpdate_DownloadPackage_WritesFileAndReportsProgress()
    {
        byte[] fakeExePayload = new byte[1024 * 64];
        new Random(42).NextBytes(fakeExePayload);

        var handler = new MockHttpMessageHandler(fakeExePayload);
        var httpClient = new HttpClient(handler);
        var updateService = new GitHubUpdateService(httpClient, currentVersion: "2.4");

        string tempFile = Path.Combine(Path.GetTempPath(), "Lyrow_Test_Update", "Lyrow-2.4-Setup-x64.exe");
        try
        {
            var progressList = new List<double>();
            var progress = new Progress<double>(pct => progressList.Add(pct));

            string downloaded = await updateService.DownloadUpdatePackageAsync(
                "https://github.com/owergungor/Lyrow/releases/download/v2.4/Lyrow-2.4-Setup-x64.exe",
                tempFile,
                progress);

            Assert.Equal(tempFile, downloaded);
            Assert.True(File.Exists(tempFile));
            Assert.Equal(fakeExePayload.Length, new FileInfo(tempFile).Length);
            Assert.NotEmpty(progressList);
            Assert.Contains(progressList, p => p >= 100.0);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    private sealed class MockHttpMessageHandler : HttpMessageHandler
    {
        private readonly byte[] _content;

        public MockHttpMessageHandler(byte[] content)
        {
            _content = content;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(_content)
            };
            response.Content.Headers.ContentLength = _content.Length;
            return Task.FromResult(response);
        }
    }
}
