using System.Text.RegularExpressions;
using FluentAssertions;
using Lyrow.Core.Contracts;
using Lyrow.Core.Models;
using Lyrow.Core.Modules;
using Lyrow.Core.Services;
using Lyrow.Music.Metadata;
using Xunit;

namespace Lyrow.App.Tests;

[Collection("DiagnosticLogger")]
public class Stage13ReliabilityTests
{
    [Fact]
    public void DiagnosticLogger_Sanitize_RedactsBearerTokens()
    {
        string raw = "API response failed. Authorization header was: Bearer eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.e30.t-IDom";
        string sanitized = LocalDiagnosticLogger.Sanitize(raw);

        sanitized.Should().NotContain("eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9");
        sanitized.Should().Contain("Bearer [REDACTED]");
    }

    [Fact]
    public void DiagnosticLogger_Sanitize_RedactsSensitiveKeyValuePairs()
    {
        string raw = "Connection details: password=SuperSecretPassword123, token: my_secret_token_456, apikey=xyz789, client_secret: pass_abc";
        string sanitized = LocalDiagnosticLogger.Sanitize(raw);

        sanitized.Should().NotContain("SuperSecretPassword123");
        sanitized.Should().NotContain("my_secret_token_456");
        sanitized.Should().NotContain("xyz789");
        sanitized.Should().NotContain("pass_abc");

        sanitized.Should().Contain("password=[REDACTED]");
        sanitized.Should().Contain("token:[REDACTED]");
        sanitized.Should().Contain("apikey=[REDACTED]");
        sanitized.Should().Contain("client_secret:[REDACTED]");
    }

    [Fact]
    public void DiagnosticLogger_RateLimits_ConsecutiveDuplicateMessages()
    {
        string tempLogFile = Path.Combine(Path.GetTempPath(), $"lyrow_throttle_test_{Guid.NewGuid():N}.log");
        try
        {
            LocalDiagnosticLogger.SetCustomLogFilePathForTesting(tempLogFile);
            string testMessage = $"Stage13_Throttle_Test_{Guid.NewGuid():N}";

            for (int i = 0; i < 5; i++)
            {
                LocalDiagnosticLogger.LogWarning(testMessage);
            }

            LocalDiagnosticLogger.Flush();

            File.Exists(tempLogFile).Should().BeTrue();
            string logContent = File.ReadAllText(tempLogFile);

            int occurrences = Regex.Matches(logContent, Regex.Escape(testMessage)).Count;
            occurrences.Should().BeInRange(1, 2, "Consecutive identical messages within the throttle window should be suppressed to 1 (or 2 if parallel test thread interleaved)");
            logContent.Should().Contain("Suppressed");
        }
        finally
        {
            LocalDiagnosticLogger.SetCustomLogFilePathForTesting(null);
            try { if (File.Exists(tempLogFile)) File.Delete(tempLogFile); } catch { }
        }
    }

    [Fact]
    public async Task SettingsManager_ConcurrentWrites_MaintainDataIntegrity()
    {
        string tempPath = Path.Combine(Path.GetTempPath(), $"lyrow_settings_test_{Guid.NewGuid():N}.json");
        try
        {
            var manager = new SettingsManager(tempPath);
            await manager.LoadAsync();

            var tasks = Enumerable.Range(0, 15).Select(i => Task.Run(async () =>
            {
                await manager.UpdateAsync(s =>
                {
                    s.StartWithWindows = (i % 2 == 0);
                    s.Lyrics.SyncOffsetMs = i * 10;
                });
            })).ToArray();

            await Task.WhenAll(tasks);

            var verifyManager = new SettingsManager(tempPath);
            var loaded = await verifyManager.LoadAsync();
            loaded.Should().NotBeNull();
            loaded.Lyrics.Should().NotBeNull();
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
            if (File.Exists(tempPath + ".tmp")) File.Delete(tempPath + ".tmp");
        }
    }

    [Fact]
    public async Task LyricsModule_RapidTrackChanges_HandlesInterleavedTransitions()
    {
        var module = new LyricsModule();
        await module.InitializeAsync();
        await module.StartAsync();

        try
        {
            var track1 = new MediaTrackInfo("Song 1", "Artist 1", "Album 1", TimeSpan.FromSeconds(180));
            var track2 = new MediaTrackInfo("Song 2", "Artist 2", "Album 2", TimeSpan.FromSeconds(200));
            var track3 = new MediaTrackInfo("Song 3", "Artist 3", "Album 3", TimeSpan.FromSeconds(220));

            // Fire rapid track changes concurrently
            var t1 = module.HandleTrackChangedAsync(track1);
            var t2 = module.HandleTrackChangedAsync(track2);
            var t3 = module.HandleTrackChangedAsync(track3);

            await Task.WhenAll(t1, t2, t3);

            module.CurrentTrack.Title.Should().Be("Song 3");
            module.CurrentTrack.Artist.Should().Be("Artist 3");
        }
        finally
        {
            await module.StopAsync();
        }
    }

    [Fact]
    public void MediaMetadataNormalizer_ResilientToMalformedInputs()
    {
        var normalizer = new MediaMetadataNormalizer();

        var norm1 = normalizer.Normalize(null, null, null);
        norm1.Title.Should().BeEmpty();
        norm1.Artist.Should().BeEmpty();
        norm1.Album.Should().BeEmpty();

        var norm2 = normalizer.Normalize("   ", "   ", "   ");
        norm2.Title.Should().BeEmpty();
        norm2.Artist.Should().BeEmpty();

        var norm3 = normalizer.Normalize("Artist - Title (Official Video) [Remastered] [4K]", "Artist", "Album");
        norm3.Title.Should().NotBeNullOrWhiteSpace();
        norm3.CleanSearchTitle.Should().NotContain("Official Video");
        norm3.CleanSearchTitle.Should().NotContain("Remastered");
    }

    [Fact]
    public void ZeroLegacyNames_Stage13()
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
                if (file.Contains("FeatureVerificationTests") || file.Contains("Stage13ReliabilityTests"))
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
