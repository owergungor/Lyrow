using Lyrow.Core.Contracts;
using Lyrow.Core.Localization;
using Lyrow.Core.Lyrics;
using Lyrow.Core.Models;
using Lyrow.Core.Services;
using Lyrow.Music.Lyrics;
using Xunit;

namespace Lyrow.App.Tests;

public class LyrowV50SettingsAndSourcesTests
{
    private static string RepoRoot => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    [Fact]
    public async Task FreshInstall_DefaultsMusicReactivityToExpressive1024_WhilePreservingExistingUserPreference()
    {
        // 1. Brand-new install defaults to 1024 (Belirgin / Expressive)
        var freshDefaults = LyrowSettings.CreateDefault();
        Assert.Equal(1024, freshDefaults.Glow.FftSize);

        string freshPath = Path.Combine(Path.GetTempPath(), $"lyrow_fresh_{Guid.NewGuid():N}.json");
        var freshSm = new SettingsManager(freshPath);
        await freshSm.LoadAsync();
        var freshCoord = new WelcomeOnboardingCoordinator(freshSm);
        Assert.Equal(1024, freshCoord.SelectedFftSize);

        // 2. Existing user who previously saved Balanced (2048) or Calm (4096) keeps their choice untouched
        string existingPath = Path.Combine(Path.GetTempPath(), $"lyrow_existing_{Guid.NewGuid():N}.json");
        try
        {
            var existingSm = new SettingsManager(existingPath);
            existingSm.Current.Glow.FftSize = 2048;
            existingSm.Current.HasCompletedWelcome = true;
            await existingSm.SaveAsync(existingSm.Current);

            var reloadedSm = new SettingsManager(existingPath);
            var reloaded = await reloadedSm.LoadAsync();
            Assert.Equal(2048, reloaded.Glow.FftSize);

            var reopenCoord = new WelcomeOnboardingCoordinator(reloadedSm, isManualReopen: true);
            Assert.Equal(2048, reopenCoord.SelectedFftSize);
        }
        finally
        {
            if (File.Exists(existingPath)) File.Delete(existingPath);
        }
    }

    [Fact]
    public void AboutSection_RemovesDeprecatedPromotionalCopy_AndPreservesVersionLicenseUpdates()
    {
        string lyricsXaml = File.ReadAllText(Path.Combine(RepoRoot, "src", "Lyrow.App", "LyricsWindow.xaml"));
        var loc = LocalizationService.Instance;

        loc.SetLanguage("tr");
        string trAbout = loc["About_Subtitle"];
        Assert.DoesNotContain("yerel ortam kenar aydınlatması", trAbout, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Apple Music", trAbout, StringComparison.OrdinalIgnoreCase);

        loc.SetLanguage("en");
        string enAbout = loc["About_Subtitle"];
        Assert.DoesNotContain("Native Ambient Edge Lighting", enAbout, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Apple Music", enAbout, StringComparison.OrdinalIgnoreCase);

        int aboutStart = lyricsXaml.IndexOf("x:Name=\"ModalSectionAbout\"", StringComparison.Ordinal);
        string aboutSlice = lyricsXaml[aboutStart..];
        Assert.DoesNotContain("Native Windows Ambient Edge Lighting", aboutSlice, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Apple Music", aboutSlice, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Text=\"Lyrow v5.0\"", aboutSlice);
        Assert.Contains("x:Name=\"TxtModalAboutLicense\"", aboutSlice);
        Assert.Contains("x:Name=\"BtnModalCheckUpdates\"", aboutSlice);
    }

    [Fact]
    public async Task LyricsSettings_RemovesMsOffset_KeepsPreferredSourceAndDragReorder_AndIgnoresLegacyOffsetJson()
    {
        string lyricsXaml = File.ReadAllText(Path.Combine(RepoRoot, "src", "Lyrow.App", "LyricsWindow.xaml"));
        string lyricsCs = File.ReadAllText(Path.Combine(RepoRoot, "src", "Lyrow.App", "LyricsWindow.xaml.cs"));
        string lyricsV4Cs = File.ReadAllText(Path.Combine(RepoRoot, "src", "Lyrow.App", "LyricsWindow.V4Features.cs"));

        int lyricsStart = lyricsXaml.IndexOf("x:Name=\"ModalSectionLyrics\"", StringComparison.Ordinal);
        int systemStart = lyricsXaml.IndexOf("x:Name=\"ModalSectionSystem\"", lyricsStart, StringComparison.Ordinal);
        string lyricsSlice = lyricsXaml[lyricsStart..systemStart];

        // Manual ms timing offset controls and model property must be completely removed
        Assert.DoesNotContain("BorderModalLyricsOffset", lyricsSlice);
        Assert.DoesNotContain("BtnModalOffsetMinus", lyricsSlice);
        Assert.DoesNotContain("TxtModalOffsetValue", lyricsSlice);
        Assert.DoesNotContain("BtnModalOffsetPlus", lyricsSlice);
        Assert.DoesNotContain("AdjustOffset", lyricsCs);
        Assert.DoesNotContain("BtnModalOffset", lyricsV4Cs);
        Assert.Null(typeof(LyricsModuleSettings).GetProperty("SyncOffsetMs"));

        // Preferred source dropdown AND 6-dot drag-reorder list must remain intact
        Assert.Contains("x:Name=\"BorderModalLyricsSources\"", lyricsSlice);
        Assert.Contains("x:Name=\"ComboModalPreferredLyricsSource\"", lyricsSlice);
        Assert.Contains("x:Name=\"ListModalLyricsSources\"", lyricsSlice);
        Assert.Contains("CanDragItems=\"True\"", lyricsSlice);
        Assert.Contains("CanReorderItems=\"True\"", lyricsSlice);
        Assert.Contains("AllowDrop=\"True\"", lyricsSlice);

        // 6-dot drag handle (2x3 Ellipse grip) present on source row template
        int ellipseCount = lyricsSlice.Split("<Ellipse ").Length - 1;
        Assert.Equal(6, ellipseCount);

        // Track-change logic in LyricsWindow.xaml.cs must NEVER hide or mutate the Settings Lyrics Sources panel
        Assert.DoesNotContain("BorderModalLyricsSources", lyricsCs);
        Assert.DoesNotContain("ListModalLyricsSources", lyricsCs);

        // Legacy settings.json containing obsolete SyncOffsetMs loads without error and persists PreferredProvider
        string tempPath = Path.Combine(Path.GetTempPath(), $"lyrow_lsrc_{Guid.NewGuid():N}.json");
        try
        {
            await File.WriteAllTextAsync(tempPath, """
                {
                  "Lyrics": {
                    "SyncOffsetMs": -500,
                    "PreferredProvider": "AppleMusic"
                  }
                }
                """);

            var smLegacy = new SettingsManager(tempPath);
            var loadedLegacy = await smLegacy.LoadAsync();
            Assert.Equal("AppleMusic", loadedLegacy.Lyrics.PreferredProvider);

            loadedLegacy.Lyrics.PreferredProvider = "Lrclib";
            await smLegacy.SaveAsync(loadedLegacy);
            string savedJson = await File.ReadAllTextAsync(tempPath);
            Assert.DoesNotContain("SyncOffsetMs", savedJson);

            var sm2 = new SettingsManager(tempPath);
            var reloaded = await sm2.LoadAsync();
            Assert.Equal("Lrclib", reloaded.Lyrics.PreferredProvider);
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }

    [Fact]
    public void RemainingTime_CalculatesAccurately_HandlesUnknownNegativeTrackSwitchPauseResumeAndSeek()
    {
        var engine = new Lyrow.Core.Timeline.PlaybackTimelineEngine();

        // 1. Unknown duration -> "--:--" (never 0:00)
        var trackNoDur = new MediaTrackInfo("Song A", "Artist A", "Album", TimeSpan.Zero);
        engine.HandleTrackChanged(trackNoDur);
        Assert.Equal("--:--", engine.CurrentTrackRemainingFormatted);

        // 2. Negative / invalid duration -> sanitized to zero -> "--:--"
        var trackNegDur = new MediaTrackInfo("Song Neg", "Artist Neg", "Album", TimeSpan.FromSeconds(-45));
        engine.HandleTrackChanged(trackNegDur);
        Assert.Equal(TimeSpan.Zero, engine.CurrentTrackDuration);
        Assert.Equal("--:--", engine.CurrentTrackRemainingFormatted);

        // 3. Normal 4:00 song at 1:30 -> remaining is 2:30 ("-2:30")
        var trackA = new MediaTrackInfo("Song 4Min", "Artist A", "Album", TimeSpan.FromMinutes(4));
        engine.HandleTrackChanged(trackA);
        engine.HandleTimelineProperties(
            TimeSpan.FromSeconds(90),
            TimeSpan.FromMinutes(4),
            MediaPlaybackStatus.Paused,
            DateTime.UtcNow,
            canSeek: true);
        Assert.Equal(TimeSpan.FromSeconds(150), engine.CurrentTrackRemaining);
        Assert.Equal("-2:30", engine.CurrentTrackRemainingFormatted);

        // 4. Seek to 3:15 -> remaining updates immediately to 0:45 ("-0:45")
        engine.HandleSeek(TimeSpan.FromSeconds(195));
        engine.HandleTimelineProperties(
            TimeSpan.FromSeconds(195),
            TimeSpan.FromMinutes(4),
            MediaPlaybackStatus.Paused,
            DateTime.UtcNow,
            canSeek: true);
        Assert.Equal("-0:45", engine.CurrentTrackRemainingFormatted);

        // 5. Track switch to Track B with initially unknown duration -> old remaining time does NOT linger
        var trackBUnknown = new MediaTrackInfo("Song B", "Artist B", "Album", TimeSpan.Zero);
        engine.HandleTrackChanged(trackBUnknown);
        Assert.Equal(TimeSpan.Zero, engine.CurrentTrackDuration);
        Assert.Equal("--:--", engine.CurrentTrackRemainingFormatted);

        // 6. Track B real duration (3:20) arrives via enrichment -> remaining updates cleanly
        var trackBEnriched = new MediaTrackInfo("Song B", "Artist B", "Album", TimeSpan.FromSeconds(200));
        engine.HandleTrackChanged(trackBEnriched);
        Assert.Equal("-3:20", engine.CurrentTrackRemainingFormatted);

        // 7. Playing -> Paused -> Playing keeps accurate remaining time
        engine.HandleTimelineProperties(
            TimeSpan.FromSeconds(50),
            TimeSpan.FromSeconds(200),
            MediaPlaybackStatus.Playing,
            DateTime.UtcNow,
            canSeek: true);
        engine.HandleTimelineProperties(
            TimeSpan.FromSeconds(50),
            TimeSpan.FromSeconds(200),
            MediaPlaybackStatus.Paused,
            DateTime.UtcNow,
            canSeek: true);
        Assert.Equal("-2:30", engine.CurrentTrackRemainingFormatted);
    }

    [Fact]
    public void RealPayloads_ClassifySyllableWordLineAndCharacterTimingTruthfully()
    {
        // 1. Better Lyrics TTML with genuine intra-word syllable spans -> TimedSyllables
        const string syllableTtml = """
            <tt xmlns="http://www.w3.org/ns/ttml" xmlns:itunes="http://music.apple.com/lyric-ttml-internal" itunes:timing="Word">
              <body><div>
                <p begin="00:01.000" end="00:03.000">
                  <span begin="00:01.000" end="00:01.300">Blind</span><span begin="00:01.300" end="00:01.600">ing</span>
                  <span begin="00:01.700" end="00:02.200">lights</span>
                </p>
              </div></body>
            </tt>
            """;
        var ttmlData = TtmlLyricsParser.Parse(syllableTtml, "Blinding Lights", "The Weeknd", LyricsProviderId.BetterLyrics);
        Assert.True(ttmlData.HasTimedSyllables);
        Assert.Equal(LyricsSyncMode.Syllable, ttmlData.BestSupportedMode);

        // 2. LiriQo response with QQ Music (Word), Kuwo (Character - MUST NOT be Syllable!), and LRCLIB (Line)
        const string liriqoJson = """
            {
              "tracks": [
                {
                  "provider": "qq",
                  "syncLevel": "word",
                  "nativeWordTiming": true,
                  "title": "Shape of You",
                  "artist": "Ed Sheeran",
                  "timed": [
                    { "text": "The club", "start": 1000, "end": 2500, "words": [
                      { "text": "The", "start": 1000, "end": 1400 },
                      { "text": "club", "start": 1500, "end": 2500 }
                    ]}
                  ]
                },
                {
                  "provider": "kuwo",
                  "syncLevel": "character",
                  "nativeWordTiming": true,
                  "title": "Shape of You",
                  "artist": "Ed Sheeran",
                  "timed": [
                    { "text": "hello", "start": 1000, "end": 2000, "words": [
                      { "text": "h", "start": 1000, "end": 1200 },
                      { "text": "e", "start": 1200, "end": 1400 },
                      { "text": "l", "start": 1400, "end": 1600 },
                      { "text": "l", "start": 1600, "end": 1800 },
                      { "text": "o", "start": 1800, "end": 2000 }
                    ]}
                  ]
                },
                {
                  "provider": "lrclib",
                  "syncLevel": "line",
                  "title": "Shape of You",
                  "artist": "Ed Sheeran",
                  "timed": [
                    { "text": "The club isn't the best place", "start": 1000, "end": 3000 }
                  ]
                }
              ]
            }
            """;

        var tracks = LiriQoClient.ParseResponseJson(liriqoJson, "Shape of You", "Ed Sheeran");
        Assert.Equal(3, tracks.Count);

        var qq = tracks.Single(t => t.SourceProvider == LyricsProviderId.QQMusic);
        Assert.False(qq.HasTimedSyllables);
        Assert.True(qq.HasTimedWords);
        Assert.Equal(LyricsSyncMode.Word, qq.BestSupportedMode);

        var kuwo = tracks.Single(t => t.SourceProvider == LyricsProviderId.Kuwo);
        Assert.False(kuwo.HasTimedSyllables); // Character timing NEVER misclassified as Syllable!
        Assert.False(kuwo.HasTimedWords);     // Single-char Latin splits NEVER misclassified as Word!
        Assert.Equal(LyricsSyncMode.Line, kuwo.BestSupportedMode);

        var lrc = tracks.Single(t => t.SourceProvider == LyricsProviderId.Lrclib);
        Assert.Equal(LyricsSyncMode.Line, lrc.BestSupportedMode);
    }

    private sealed class StubSyncProvider : ILyricsSyncProvider
    {
        private readonly Func<Task<LyricsTimingData?>> _factory;
        public LyricsProviderId ProviderId { get; }
        public StubSyncProvider(LyricsProviderId id, Func<Task<LyricsTimingData?>> factory)
        {
            ProviderId = id;
            _factory = factory;
        }
        public Task<LyricsTimingData?> FetchTimingDataAsync(string trackName, string artistName, TimeSpan? duration = null, CancellationToken cancellationToken = default)
            => _factory();
    }

    [Fact]
    public async Task Orchestrator_FallsBackCleanlyOnProviderFailure_AndNeverDowngradesHigherQuality()
    {
        var failingBly = new StubSyncProvider(LyricsProviderId.BetterLyrics, () => throw new HttpRequestException("429 Too Many Requests"));
        var lineLrc = new StubSyncProvider(LyricsProviderId.Lrclib, () => Task.FromResult<LyricsTimingData?>(
            new LyricsTimingData("Test", "Artist", LyricsProviderId.Lrclib,
                LyricsTimingGranularity.PlainLyrics | LyricsTimingGranularity.TimedLines,
                new[] { new LyricLineTiming(0, "Line 1", TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(3), Array.Empty<LyricWordSegment>(), Array.Empty<LyricSyllableTiming>()) },
                "Line 1")));

        var wordQq = new StubSyncProvider(LyricsProviderId.QQMusic, async () =>
        {
            await Task.Delay(30);
            var w = new[] { new LyricWordSegment("Line", TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), 0, Array.Empty<LyricSyllableTiming>()) };
            return new LyricsTimingData("Test", "Artist", LyricsProviderId.QQMusic,
                LyricsTimingGranularity.PlainLyrics | LyricsTimingGranularity.TimedLines | LyricsTimingGranularity.TimedWords,
                new[] { new LyricLineTiming(0, "Line 1", TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(3), w, Array.Empty<LyricSyllableTiming>()) },
                "Line 1");
        });

        var orchestrator = new MultiProviderLyricsOrchestrator(new ILyricsSyncProvider[] { failingBly, lineLrc, wordQq });
        var result = await orchestrator.ResolveLyricsProgressiveAsync("Test", "Artist", LyricsSyncMode.Automatic);

        Assert.True(result.Success);
        // BetterLyrics threw 429; LRCLIB arrived fast (Line), then QQ Music upgraded it to Word (higher quality wins!)
        Assert.Equal(LyricsProviderId.QQMusic, result.ProviderId);
        Assert.Equal(LyricsSyncMode.Word, result.ResolvedMode);
    }

    [Fact]
    public async Task DragReorder_PersistsSourcePriority_AffectsAutomaticTies_AndPreservesSyllableSuperiority()
    {
        // 1. Reorder helper moves NetEase ahead of QQMusic and persists cleanly across SettingsManager reload
        var initial = LyricsSourceOrderHelper.NormalizeOrder(null);
        int idxNetEase = initial.ToList().IndexOf(LyricsProviderId.NetEase);
        int idxQq = initial.ToList().IndexOf(LyricsProviderId.QQMusic);
        Assert.True(idxNetEase > idxQq);

        var reordered = LyricsSourceOrderHelper.Reorder(initial, idxNetEase, 0);
        Assert.Equal(LyricsProviderId.NetEase, reordered[0]);

        string tempPath = Path.Combine(Path.GetTempPath(), $"lyrow_drag_order_{Guid.NewGuid():N}.json");
        try
        {
            var sm = new SettingsManager(tempPath);
            sm.Current.Lyrics.SourcePriorityOrder = LyricsSourceOrderHelper.ToSettingsList(reordered);
            await sm.SaveAsync(sm.Current);

            var smReloaded = new SettingsManager(tempPath);
            await smReloaded.LoadAsync();
            var normalizedReloaded = LyricsSourceOrderHelper.NormalizeOrder(smReloaded.Current.Lyrics.SourcePriorityOrder);
            Assert.Equal(LyricsProviderId.NetEase, normalizedReloaded[0]);

            // 2. Same quality (both Word): NetEase wins because user dragged it above QQMusic
            var wordSegment = new[] { new LyricWordSegment("Hello", TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), 0, Array.Empty<LyricSyllableTiming>()) };
            var qqWord = new StubSyncProvider(LyricsProviderId.QQMusic, () => Task.FromResult<LyricsTimingData?>(
                new LyricsTimingData("Song", "Artist", LyricsProviderId.QQMusic,
                    LyricsTimingGranularity.PlainLyrics | LyricsTimingGranularity.TimedLines | LyricsTimingGranularity.TimedWords,
                    new[] { new LyricLineTiming(0, "Hello", TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(3), wordSegment, Array.Empty<LyricSyllableTiming>()) },
                    "Hello")));
            var netEaseWord = new StubSyncProvider(LyricsProviderId.NetEase, () => Task.FromResult<LyricsTimingData?>(
                new LyricsTimingData("Song", "Artist", LyricsProviderId.NetEase,
                    LyricsTimingGranularity.PlainLyrics | LyricsTimingGranularity.TimedLines | LyricsTimingGranularity.TimedWords,
                    new[] { new LyricLineTiming(0, "Hello", TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(3), wordSegment, Array.Empty<LyricSyllableTiming>()) },
                    "Hello")));

            var orchWordTie = new MultiProviderLyricsOrchestrator(new ILyricsSyncProvider[] { qqWord, netEaseWord }, settingsManager: smReloaded);
            var tieResult = await orchWordTie.ResolveLyricsProgressiveAsync("Song", "Artist", LyricsSyncMode.Automatic);
            Assert.True(tieResult.Success);
            Assert.Equal(LyricsProviderId.NetEase, tieResult.ProviderId);
            Assert.Equal(LyricsSyncMode.Word, tieResult.ResolvedMode);

            // 3. Syllable superiority preserved: even though NetEase (Word) is #1 in user order, BetterLyrics (Syllable) MUST still win!
            var syls = new[]
            {
                new LyricSyllableTiming("Hel", TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(1400), 0, 0, false),
                new LyricSyllableTiming("lo", TimeSpan.FromMilliseconds(1400), TimeSpan.FromSeconds(2), 1, 0, true)
            };
            var sylWord = new[] { new LyricWordSegment("Hello", TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), 0, syls) };
            var blySyllable = new StubSyncProvider(LyricsProviderId.BetterLyrics, () => Task.FromResult<LyricsTimingData?>(
                new LyricsTimingData("Song", "Artist", LyricsProviderId.BetterLyrics,
                    LyricsTimingGranularity.PlainLyrics | LyricsTimingGranularity.TimedLines | LyricsTimingGranularity.TimedWords | LyricsTimingGranularity.TimedSyllables,
                    new[] { new LyricLineTiming(0, "Hello", TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(3), sylWord, syls) },
                    "Hello")));

            var orchQuality = new MultiProviderLyricsOrchestrator(new ILyricsSyncProvider[] { netEaseWord, blySyllable }, settingsManager: smReloaded);
            var qualityResult = await orchQuality.ResolveLyricsProgressiveAsync("Song", "Artist", LyricsSyncMode.Automatic);
            Assert.True(qualityResult.Success);
            Assert.Equal(LyricsProviderId.BetterLyrics, qualityResult.ProviderId);
            Assert.Equal(LyricsSyncMode.Syllable, qualityResult.ResolvedMode);
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }

    [Fact]
    public void WelcomeV4_AppearanceCompact_RecommendedBadge_AndFooterStartSharesCompleteAsync()
    {
        string welcomeXaml = File.ReadAllText(Path.Combine(RepoRoot, "src", "Lyrow.App", "WelcomeWindow.xaml"));
        string welcomeCs = File.ReadAllText(Path.Combine(RepoRoot, "src", "Lyrow.App", "WelcomeWindow.xaml.cs"));

        // 1. Compact Welcome > Appearance layout (fits cleanly without clipping)
        Assert.Contains("x:Name=\"PanelWelcomeTabAppearance\" Spacing=\"10\"", welcomeXaml);
        Assert.Contains("x:Name=\"EdgeLightAuraOuter\" Height=\"114\"", welcomeXaml);

        // 2. Recommended ("Önerilen") badge on Expressive (Belirgin) card
        Assert.Contains("x:Name=\"BadgeWelcomeSensRecommended\"", welcomeXaml);
        Assert.Contains("x:Name=\"TxtWelcomeSensRecommended\"", welcomeXaml);

        // 3. Visible Footer Start ("Başla") button on Finish step sharing exact OnStartListeningClicked -> CompleteAsync
        Assert.Contains("x:Name=\"BtnWelcomeFinishFooter\"", welcomeXaml);
        Assert.Contains("BtnWelcomeFinishFooter.Click += OnStartListeningClicked;", welcomeCs);
        Assert.Contains("BtnWelcomeFinishFooter.Visibility = _coordinator.IsReadyStep ? Visibility.Visible : Visibility.Collapsed;", welcomeCs);

        // Localization verification (TR & EN)
        var loc = LocalizationService.Instance;
        loc.SetLanguage("tr");
        Assert.Equal("Önerilen", loc["WelcomeV4_Badge_Recommended"]);
        Assert.Equal("Başla", loc["WelcomeV4_Btn_Start"]);
        loc.SetLanguage("en");
        Assert.Equal("Recommended", loc["WelcomeV4_Badge_Recommended"]);
        Assert.Equal("Start", loc["WelcomeV4_Btn_Start"]);
    }
}
