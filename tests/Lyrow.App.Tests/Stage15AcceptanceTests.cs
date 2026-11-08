using System.Text.RegularExpressions;
using FluentAssertions;
using Lyrow.Audio.Analysis;
using Lyrow.Core.Contracts;
using Lyrow.Core.Lyrics;
using Lyrow.Core.Models;
using Lyrow.Core.Modules;
using Lyrow.Core.Services;
using Lyrow.Music.Metadata;
using Xunit;

namespace Lyrow.App.Tests;

public class Stage15AcceptanceTests
{
    [Fact]
    public async Task WelcomeWizard_FirstRun_FlowAndSettingsPersistence()
    {
        string tempPath = Path.Combine(Path.GetTempPath(), $"lyrow_wizard_test_{Guid.NewGuid():N}.json");
        try
        {
            var manager = new SettingsManager(tempPath);
            var initial = await manager.LoadAsync();

            // Default fresh install state
            initial.HasCompletedFirstRun.Should().BeFalse();

            // Simulate Wizard selection: Turkish locale, Dark theme, Start with Windows = false
            await manager.UpdateAsync(s =>
            {
                s.Language = "tr";
                s.Theme = ThemeMode.Dark;
                s.StartWithWindows = false;
                s.HasCompletedFirstRun = true;
                s.GlowIntensity = 85;
            });

            // Reload and verify persistence
            var verifyManager = new SettingsManager(tempPath);
            var saved = await verifyManager.LoadAsync();

            saved.HasCompletedFirstRun.Should().BeTrue();
            saved.Language.Should().Be("tr");
            saved.Theme.Should().Be(ThemeMode.Dark);
            saved.StartWithWindows.Should().BeFalse();
            saved.GlowIntensity.Should().Be(85);
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
            if (File.Exists(tempPath + ".tmp")) File.Delete(tempPath + ".tmp");
        }
    }

    [Fact]
    public async Task EndToEnd_MediaSessionToLyricsPipeline()
    {
        var lyricsModule = new LyricsModule();
        await lyricsModule.InitializeAsync();
        await lyricsModule.StartAsync();

        try
        {
            // Simulate track switch
            var testTrack = new MediaTrackInfo("Acceptance Track", "Lyrow Artist", "Acceptance Album", TimeSpan.FromSeconds(180));
            await lyricsModule.HandleTrackChangedAsync(testTrack);

            lyricsModule.CurrentTrack.Title.Should().Be("Acceptance Track");
            lyricsModule.CurrentTrack.Artist.Should().Be("Lyrow Artist");
            lyricsModule.CurrentTimeline.Status.Should().Be(MediaPlaybackStatus.Changing);

            // Simulate timeline progress update
            var activeTimeline = new MediaTimelineInfo(TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(180), MediaPlaybackStatus.Playing, DateTime.UtcNow);
            lyricsModule.UpdateTimelineExternal(activeTimeline);

            lyricsModule.CurrentTimeline.Position.Should().Be(TimeSpan.FromSeconds(30));
            lyricsModule.CurrentTimeline.Status.Should().Be(MediaPlaybackStatus.Playing);

            // Simulate track clear (e.g. player closed)
            await lyricsModule.HandleTrackChangedAsync(MediaTrackInfo.Empty);
            lyricsModule.CurrentTrack.IsEmpty.Should().BeTrue();
            lyricsModule.CurrentLyrics.Lines.Should().BeEmpty();
        }
        finally
        {
            await lyricsModule.StopAsync();
        }
    }

    [Fact]
    public async Task EndToEnd_AmbientGlow_LifecycleAndAcousticProfiles()
    {
        var glowModule = new GlowModule();
        await glowModule.InitializeAsync();
        await glowModule.StartAsync();

        glowModule.State.Should().Be(ModuleState.Running);

        // DSP beat engine test with acoustic profiles
        var beatEngine = new GlowBeatEngine();
        GlowAcousticProfile[] profiles = [
            GlowAcousticProfile.Balanced,
            GlowAcousticProfile.BassBoost,
            GlowAcousticProfile.VocalFocus,
            GlowAcousticProfile.HighFidelity
        ];

        foreach (var profile in profiles)
        {
            beatEngine.SetAcousticProfile(profile);
            beatEngine.AcousticProfile.Should().Be(profile);

            var spectrum = new FrequencySpectrum(
                rawFrequencies: Array.Empty<float>(),
                bands: Array.Empty<float>(),
                subBass: 0.6f,
                bass: 0.7f,
                mid: 0.4f,
                treble: 0.5f,
                levels: new AudioLevels(0.5f, 0.8f, -14f, false));

            beatEngine.Process(spectrum, 1.0f, 16.6);
            beatEngine.OverallGlowIntensity.Should().BeGreaterThanOrEqualTo(0f);
        }

        await glowModule.StopAsync();
        glowModule.State.Should().Be(ModuleState.Stopped);
        glowModule.LatestSpectrum.Should().Be(FrequencySpectrum.Empty);
    }

    [Fact]
    public void SecurityAudit_NetworkEndpoints_StrictlyAllowlisted()
    {
        string root = GetSolutionRoot();
        var files = Directory.GetFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories);

        var urlRegex = new Regex(@"https?://[^\s""'>)]+", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        string[] allowedDomains = [
            "github.com",
            "api.github.com",
            "lrclib.net",
            "apple.com",
            "schemas.microsoft.com",
            "go.microsoft.com",
            "xceed.com" // FluentAssertions license header
        ];

        foreach (var file in files)
        {
            string content = File.ReadAllText(file);
            var matches = urlRegex.Matches(content);
            foreach (Match match in matches)
            {
                string url = match.Value.TrimEnd('.', ';', ',');
                if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
                {
                    bool isAllowed = allowedDomains.Any(domain =>
                        uri.Host.Equals(domain, StringComparison.OrdinalIgnoreCase) ||
                        uri.Host.EndsWith("." + domain, StringComparison.OrdinalIgnoreCase));

                    isAllowed.Should().BeTrue($"File {Path.GetFileName(file)} references unapproved external host: {uri.Host} (URL: {url})");
                }
            }
        }
    }

    [Fact]
    public void SecurityAudit_LogSanitization_ScrubsSensitiveTokensAndPaths()
    {
        string testInput = @"Connection error to C:\Users\AdminUser\AppData\Roaming\Lyrow:
Authorization: Bearer secret_access_token_12345
URL: https://lrclib.net/api/get?token=my_secret_token&client_secret=super_secret_value
password: Password123!";

        string sanitized = LocalDiagnosticLogger.Sanitize(testInput);

        sanitized.Should().NotContain("AdminUser");
        sanitized.Should().NotContain("secret_access_token_12345");
        sanitized.Should().NotContain("my_secret_token");
        sanitized.Should().NotContain("super_secret_value");
        sanitized.Should().NotContain("Password123!");

        sanitized.Should().Contain(@"C:\Users\***\AppData");
        sanitized.Should().Contain("Bearer [REDACTED]");
        sanitized.Should().Contain("token=[REDACTED]");
        sanitized.Should().Contain("client_secret=[REDACTED]");
        sanitized.Should().Contain("password:[REDACTED]");
    }

    [Fact]
    public void ZeroLegacyNames_Stage15()
    {
        string root = GetSolutionRoot();
        string[] searchDirs = { "src", "tests", "installer" };
        string pattern1 = @"\b" + "l" + "u" + "m" + "n" + @"\b";
        string pattern2 = @"\b" + "v" + "e" + "r" + "c" + "i" + @"\b";

        foreach (var dir in searchDirs)
        {
            var dirPath = Path.Combine(root, dir);
            if (!Directory.Exists(dirPath)) continue;

            var files = Directory.GetFiles(dirPath, "*.*", SearchOption.AllDirectories);
            foreach (var file in files)
            {
                if (file.Contains("FeatureVerificationTests") || file.Contains("Stage13ReliabilityTests") ||
                    file.Contains("Stage14PerformanceTests") || file.Contains("Stage15AcceptanceTests"))
                    continue;

                var ext = Path.GetExtension(file).ToLowerInvariant();
                if (ext is not (".cs" or ".xaml" or ".iss" or ".props" or ".csproj" or ".json" or ".xml"))
                    continue;

                string content = File.ReadAllText(file);
                Regex.IsMatch(content, pattern1, RegexOptions.IgnoreCase).Should().BeFalse($"File {file} should not contain legacy whole-word");
                Regex.IsMatch(content, pattern2, RegexOptions.IgnoreCase).Should().BeFalse($"File {file} should not contain legacy whole-word");
            }
        }
    }

    private static string GetSolutionRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(dir))
        {
            if (File.Exists(Path.Combine(dir, "Lyrow.sln")))
            {
                return dir;
            }
            dir = Path.GetDirectoryName(dir);
        }
        return Directory.GetCurrentDirectory();
    }
}
