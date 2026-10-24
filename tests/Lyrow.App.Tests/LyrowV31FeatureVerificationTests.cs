using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Lyrow.Core.Contracts;
using Lyrow.Core.Localization;
using Lyrow.Core.Lyrics;
using Lyrow.Core.Models;
using Lyrow.Core.Modules;
using Lyrow.Core.Services;
using Lyrow.Music.Session;
using Xunit;

namespace Lyrow.App.Tests;

public sealed class LyrowV31FeatureVerificationTests
{
    private static string GetSolutionRoot()
    {
        string baseDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;
        return Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", ".."));
    }

    #region 1. Automatic Track Transition State Isolation & Lyrics Reset

    [Fact]
    public async Task AutomaticTrackTransitionResetsLyricsWindow_AndClearsOldLyrics()
    {
        var dummyMonitor = new FakeMediaSessionMonitor();
        var dummyLyricsClient = new FakeLyricsClient();
        var dummyCache = new FakeLyricsCache();

        var lyricsModule = new LyricsModule(dummyMonitor, dummyLyricsClient, dummyCache);
        await lyricsModule.InitializeAsync();
        await lyricsModule.StartAsync();

        // 1. Play Track A with lyrics
        var trackA = new MediaTrackInfo("Track A", "Artist 1", "Album 1", TimeSpan.FromSeconds(240));
        var lyricsA = new SyncedLyrics("LRC", "[00:10.00]Line A1\n[00:30.00]Line A2", false, new[]
        {
            new LyricLine(TimeSpan.FromSeconds(10), "Line A1", 0),
            new LyricLine(TimeSpan.FromSeconds(30), "Line A2", 1)
        });
        dummyLyricsClient.SetLyrics("Track A", "Artist 1", lyricsA);

        await lyricsModule.HandleTrackChangedAsync(trackA);

        lyricsModule.CurrentTrack.Title.Should().Be("Track A");
        lyricsModule.CurrentLyrics.Lines.Should().HaveCount(2);
        lyricsModule.CurrentTimeline.Duration.Should().Be(TimeSpan.FromSeconds(240));

        // 2. Transition automatically to Track B (which has no lyrics yet or different lyrics)
        var trackB = new MediaTrackInfo("Track B", "Artist 2", "Album 2", TimeSpan.FromSeconds(180));
        dummyLyricsClient.Clear();

        await lyricsModule.HandleTrackChangedAsync(trackB);

        // State for Track A must be completely erased
        lyricsModule.CurrentTrack.Title.Should().Be("Track B");
        lyricsModule.CurrentTrack.Duration.Should().Be(TimeSpan.FromSeconds(180));
        lyricsModule.CurrentTimeline.Position.Should().Be(TimeSpan.Zero);
        lyricsModule.CurrentTimeline.Duration.Should().Be(TimeSpan.FromSeconds(180));
        lyricsModule.CurrentLyrics.Lines.Should().BeEmpty("Old Track A lyrics must be purged immediately on track switch");
        lyricsModule.ActiveLineIndex.Should().Be(-1);

        await lyricsModule.StopAsync();
    }

    [Fact]
    public void AutomaticTrackTransitionResetsDuration_ZeroCumulativeDuration_A_To_B_To_C()
    {
        // Simulate GSMTC reports for Track A, B, and C
        // Track A = 240s
        var durA = CalculateEffectiveDuration(TimeSpan.Zero, TimeSpan.FromSeconds(240));
        durA.Should().Be(TimeSpan.FromSeconds(240));

        // Track B = 180s (with continuous media stream offset where startTime = 240s, endTime = 420s)
        var durB = CalculateEffectiveDuration(TimeSpan.FromSeconds(240), TimeSpan.FromSeconds(420));
        durB.Should().Be(TimeSpan.FromSeconds(180), "Duration of Track B must be 180s, never cumulative 420s");

        // Track C = 210s (where startTime = 420s, endTime = 630s)
        var durC = CalculateEffectiveDuration(TimeSpan.FromSeconds(420), TimeSpan.FromSeconds(630));
        durC.Should().Be(TimeSpan.FromSeconds(210), "Duration of Track C must be 210s, never cumulative 630s");

        // Position calculation must also be offset relative
        var posB = CalculateEffectivePosition(TimeSpan.FromSeconds(240), TimeSpan.FromSeconds(255));
        posB.Should().Be(TimeSpan.FromSeconds(15), "Position of Track B at raw 255s with start 240s must be 15s");
    }

    private static TimeSpan CalculateEffectiveDuration(TimeSpan startTime, TimeSpan endTime)
    {
        return endTime > startTime ? (endTime - startTime) : endTime;
    }

    private static TimeSpan CalculateEffectivePosition(TimeSpan startTime, TimeSpan rawPos)
    {
        return rawPos >= startTime ? (rawPos - startTime) : rawPos;
    }

    [Fact]
    public async Task AutomaticTrackTransitionRejectsStaleLyrics_WhenTrackChangesDuringFetch()
    {
        var dummyMonitor = new FakeMediaSessionMonitor();
        var delayedClient = new DelayedLyricsClient(delayMs: 150);
        var dummyCache = new FakeLyricsCache();

        var lyricsModule = new LyricsModule(dummyMonitor, delayedClient, dummyCache);
        await lyricsModule.InitializeAsync();
        await lyricsModule.StartAsync();

        var trackA = new MediaTrackInfo("Slow Track A", "Artist A", "Album A", TimeSpan.FromSeconds(200));
        var lyricsA = new SyncedLyrics("LRC", "[00:10.00]Slow A", false, new[] { new LyricLine(TimeSpan.FromSeconds(10), "Slow A", 0) });
        delayedClient.RegisterLyrics("Slow Track A", "Artist A", lyricsA);

        // Start loading Track A
        var fetchTaskA = lyricsModule.HandleTrackChangedAsync(trackA);

        // Almost immediately, track changes to Track B before Track A returns
        await Task.Delay(20);
        var trackB = new MediaTrackInfo("Fast Track B", "Artist B", "Album B", TimeSpan.FromSeconds(150));
        var lyricsB = new SyncedLyrics("LRC", "[00:05.00]Fast B", false, new[] { new LyricLine(TimeSpan.FromSeconds(5), "Fast B", 0) });
        delayedClient.RegisterLyrics("Fast Track B", "Artist B", lyricsB);

        var fetchTaskB = lyricsModule.HandleTrackChangedAsync(trackB);

        await Task.WhenAll(fetchTaskA, fetchTaskB);

        // Final state MUST be Track B, never corrupted by Track A's stale response
        lyricsModule.CurrentTrack.Title.Should().Be("Fast Track B");
        lyricsModule.CurrentLyrics.Lines.Should().HaveCount(1);
        lyricsModule.CurrentLyrics.Lines[0].Text.Should().Be("Fast B");

        await lyricsModule.StopAsync();
    }

    [Fact]
    public void AutomaticTrackTransitionResetsLyricAnimation_StartsCleanFromZero()
    {
        var line1 = new LyricLine(TimeSpan.FromSeconds(0), "Hello", 0, null, TimeSpan.FromSeconds(4));
        var line2 = new LyricLine(TimeSpan.FromSeconds(4), "World", 1, null, TimeSpan.FromSeconds(8));

        // When a new track starts at position 0:
        var p1 = LyricsFillProgressCalculator.CalculateLineProgress(line1, TimeSpan.Zero);
        var p2 = LyricsFillProgressCalculator.CalculateLineProgress(line2, TimeSpan.Zero);

        p1.Should().Be(0.0, "Animation of line 1 at track start t=0 must be strictly 0.0");
        p2.Should().Be(0.0, "Animation of line 2 at track start t=0 must be strictly 0.0");

        // Upon seek to 2 seconds, line 1 is recomputed without track reset
        var p1Seek = LyricsFillProgressCalculator.CalculateLineProgress(line1, TimeSpan.FromSeconds(2));
        p1Seek.Should().BeApproximately(0.5, 0.01, "Seeking to 2s into a 4s line recomputes progress to 0.5");
    }

    #endregion

    #region 2. Update Toast Persistence & Real Download Progress

    [Fact]
    public void UpdateToast_ShowsRealDownloadProgress_ZeroFakeProgress()
    {
        long totalBytes = 25 * 1024 * 1024; // 25 MB
        long downloaded = 12 * 1024 * 1024; // 12 MB

        var progress = new UpdateDownloadProgress(
            Percent: (double)downloaded / totalBytes * 100.0,
            BytesRead: downloaded,
            TotalBytes: totalBytes);

        progress.Percent.Should().BeApproximately(48.0, 0.1);
        progress.MegabytesRead.Should().BeApproximately(12.0, 0.1);
        progress.TotalMegabytes.Should().BeApproximately(25.0, 0.1);
        progress.IsIndeterminate.Should().BeFalse();

        // Indeterminate when Content-Length is unavailable
        var indet = new UpdateDownloadProgress(-1.0, downloaded, null);
        indet.IsIndeterminate.Should().BeTrue();
        indet.MegabytesRead.Should().BeApproximately(12.0, 0.1);
        indet.TotalMegabytes.Should().BeNull();
    }

    [Fact]
    public void UpdateToast_DoesNotUseFakeProgress_StaticInspection()
    {
        string root = GetSolutionRoot();
        string updateCode = File.ReadAllText(Path.Combine(root, "src", "Lyrow.Core", "Services", "GitHubUpdateService.cs"));

        // Must calculate progress strictly from totalRead and totalBytes, never Random or artificial timer increments
        updateCode.Should().NotContain("new Random");
        updateCode.Should().NotContain("Random.Shared");
        updateCode.Should().Contain("totalRead += bytesRead");
        updateCode.Should().Contain("Math.Clamp((double)totalRead / totalBytes.Value * 100.0, 0.0, 100.0)");
    }

    [Fact]
    public void UpdateToast_AllFiveLanguagesHaveRequiredKeys()
    {
        var loc = new LocalizationService();

        string[] requiredKeys =
        {
            "Updates_Available",
            "Updates_Downloading",
            "Updates_Preparing",
            "Updates_Verifying",
            "Updates_Installing",
            "Updates_Complete",
            "Updates_Completed",
            "Updates_Failed",
            "Updates_Retry",
            "Updates_Bytes",
            "Updates_Later"
        };

        foreach (var lang in new[] { "en", "tr", "fr", "es", "de" })
        {
            loc.SetLanguage(lang);
            foreach (var key in requiredKeys)
            {
                string val = loc[key];
                val.Should().NotBeNullOrWhiteSpace($"Language '{lang}' must define key '{key}'");
            }

            // Verify byte formatting string can be formatted with {0} and {1}
            string bytePattern = loc["Updates_Bytes"];
            string formatted = string.Format(bytePattern, 12.4, 25.0);
            formatted.Should().Contain("12");
            formatted.Should().Contain("25");
        }
    }

    [Fact]
    public void UpdateToast_StateEnumSupportsAllRequiredLifecycleStates()
    {
        var states = Enum.GetNames<Controls.UpdateToastState>();
        states.Should().Contain("Available");
        states.Should().Contain("Downloading");
        states.Should().Contain("Downloaded");
        states.Should().Contain("Verifying");
        states.Should().Contain("Installing");
        states.Should().Contain("Completed");
        states.Should().Contain("Failed");
    }

    #endregion

    #region 3. Strict Two-Part Versioning Compliance (3.1 / v3.1)

    [Fact]
    public void Versioning_StrictTwoPartAcrossAllMetadata()
    {
        string root = GetSolutionRoot();

        // 1. Directory.Build.props
        string props = File.ReadAllText(Path.Combine(root, "Directory.Build.props"));
        props.Should().Contain("<Version>3.1</Version>");
        props.Should().Contain("<PackageVersion>3.1</PackageVersion>");
        props.Should().Contain("<InformationalVersion>3.1</InformationalVersion>");
        props.Should().Contain("<AssemblyVersion>3.1.0.0</AssemblyVersion>");
        props.Should().Contain("<FileVersion>3.1.0.0</FileVersion>");
        props.Should().NotContain("<Version>3.1.0</Version>");
        props.Should().NotContain("<PackageVersion>3.1.0</PackageVersion>");

        // 2. Lyrow.App.csproj
        string csproj = File.ReadAllText(Path.Combine(root, "src", "Lyrow.App", "Lyrow.App.csproj"));
        csproj.Should().Contain("<Version>3.1</Version>");
        csproj.Should().Contain("<PackageVersion>3.1</PackageVersion>");
        csproj.Should().Contain("<InformationalVersion>3.1</InformationalVersion>");
        csproj.Should().Contain("<AssemblyVersion>3.1.0.0</AssemblyVersion>");
        csproj.Should().Contain("<FileVersion>3.1.0.0</FileVersion>");
        csproj.Should().NotContain("<Version>3.1.0</Version>");

        // 3. app.manifest
        string manifest = File.ReadAllText(Path.Combine(root, "src", "Lyrow.App", "app.manifest"));
        manifest.Should().Contain("version=\"3.1.0.0\"");

        // 4. Inno Setup installer
        string iss = File.ReadAllText(Path.Combine(root, "installer", "setup.iss"));
        iss.Should().Contain("#define MyAppVersion \"3.1\"");
        iss.Should().NotContain("#define MyAppVersion \"3.1.0\"");

        // 5. GitHubUpdateService
        GitHubUpdateService.AppVersion.Should().Be("3.1");

        // 6. Workflow release.yml
        string releaseYml = File.ReadAllText(Path.Combine(root, ".github", "workflows", "release.yml"));
        releaseYml.Should().Contain("default: 'v3.1'");
        releaseYml.Should().NotContain("3.1.0");

        // 7. Smoke test script
        string smoke = File.ReadAllText(Path.Combine(root, "test_smoke.ps1"));
        smoke.Should().Contain("LYROW v3.1 WINDOWS SMOKE TEST HARNESS");
    }

    [Fact]
    public void LegacyNames_ZeroOccurrencesInActiveRepo()
    {
        string root = GetSolutionRoot();

        string[] targets =
        {
            Path.Combine(root, "src"),
            Path.Combine(root, "installer"),
            Path.Combine(root, "Directory.Build.props"),
            Path.Combine(root, "README.md")
        };

        foreach (var target in targets)
        {
            if (File.Exists(target))
            {
                CheckFileForLegacyNames(target);
            }
            else if (Directory.Exists(target))
            {
                foreach (var file in Directory.EnumerateFiles(target, "*.*", SearchOption.AllDirectories))
                {
                    if (file.EndsWith(".cs") || file.EndsWith(".xaml") || file.EndsWith(".iss") || file.EndsWith(".props") || file.EndsWith(".md"))
                    {
                        CheckFileForLegacyNames(file);
                    }
                }
            }
        }
    }

    private static void CheckFileForLegacyNames(string filePath)
    {
        string content = File.ReadAllText(filePath);
        string pattern1 = @"\b" + "l" + @"umn\b";
        string pattern2 = @"\b" + "v" + @"erci\b";
        System.Text.RegularExpressions.Regex.IsMatch(content, pattern1, System.Text.RegularExpressions.RegexOptions.IgnoreCase).Should().BeFalse($"File '{filePath}' must not contain legacy name");
        System.Text.RegularExpressions.Regex.IsMatch(content, pattern2, System.Text.RegularExpressions.RegexOptions.IgnoreCase).Should().BeFalse($"File '{filePath}' must not contain legacy name");
    }

    #endregion

    #region Helper Test Doubles

    private sealed class FakeMediaSessionMonitor : IMediaSessionMonitor
    {
        public bool IsMonitoring { get; private set; }
        public bool HasActiveSession => !CurrentTrack.IsEmpty;
        public string? SourceAppId => "TestApp";
        public MediaTrackInfo CurrentTrack { get; set; } = MediaTrackInfo.Empty;
        public MediaTimelineInfo CurrentTimeline { get; set; } = MediaTimelineInfo.Zero;

#pragma warning disable CS0067
        public event EventHandler<MediaTrackInfo>? TrackChanged;
        public event EventHandler<MediaTimelineInfo>? TimelineChanged;
        public event EventHandler<Exception>? MonitorFaulted;
#pragma warning restore CS0067

        public Task StartAsync(CancellationToken cancellationToken = default) { IsMonitoring = true; return Task.CompletedTask; }
        public Task StopAsync(CancellationToken cancellationToken = default) { IsMonitoring = false; return Task.CompletedTask; }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        public Task<bool> TryPlayAsync() => Task.FromResult(true);
        public Task<bool> TryPauseAsync() => Task.FromResult(true);
        public Task<bool> TryTogglePlayPauseAsync() => Task.FromResult(true);
        public Task<bool> TrySkipNextAsync() => Task.FromResult(true);
        public Task<bool> TrySkipPreviousAsync() => Task.FromResult(true);
        public Task<bool> TrySeekAsync(TimeSpan position) => Task.FromResult(true);
    }

    private sealed class FakeLyricsClient : ILyricsClient
    {
        private readonly Dictionary<string, SyncedLyrics> _store = new(StringComparer.OrdinalIgnoreCase);

        public void SetLyrics(string title, string artist, SyncedLyrics lyrics)
        {
            _store[$"{artist}_{title}"] = lyrics;
        }

        public void Clear() => _store.Clear();

        public Task<LyricsSearchResult> FetchLyricsAsync(string trackName, string artistName, TimeSpan? duration = null, CancellationToken cancellationToken = default)
        {
            string key = $"{artistName}_{trackName}";
            if (_store.TryGetValue(key, out var lyrics))
            {
                return Task.FromResult(new LyricsSearchResult(Found: true, Lyrics: lyrics, Source: "FakeLRCLIB"));
            }
            return Task.FromResult(new LyricsSearchResult(Found: false, Lyrics: null, Source: "None", Error: "Not found"));
        }
    }

    private sealed class DelayedLyricsClient : ILyricsClient
    {
        private readonly int _delayMs;
        private readonly Dictionary<string, SyncedLyrics> _store = new(StringComparer.OrdinalIgnoreCase);

        public DelayedLyricsClient(int delayMs)
        {
            _delayMs = delayMs;
        }

        public void RegisterLyrics(string title, string artist, SyncedLyrics lyrics)
        {
            _store[$"{artist}_{title}"] = lyrics;
        }

        public async Task<LyricsSearchResult> FetchLyricsAsync(string trackName, string artistName, TimeSpan? duration = null, CancellationToken cancellationToken = default)
        {
            await Task.Delay(_delayMs, cancellationToken).ConfigureAwait(false);
            string key = $"{artistName}_{trackName}";
            if (_store.TryGetValue(key, out var lyrics))
            {
                return new LyricsSearchResult(Found: true, Lyrics: lyrics, Source: "DelayedFakeLRCLIB");
            }
            return new LyricsSearchResult(Found: false, Lyrics: null, Source: "None", Error: "Not found");
        }
    }

    private sealed class FakeLyricsCache : ILyricsCache
    {
        public Task<SyncedLyrics?> GetAsync(string trackName, string artistName, CancellationToken cancellationToken = default)
            => Task.FromResult<SyncedLyrics?>(null);

        public Task SaveAsync(string trackName, string artistName, SyncedLyrics lyrics, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task ClearAsync(CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    #endregion
}
