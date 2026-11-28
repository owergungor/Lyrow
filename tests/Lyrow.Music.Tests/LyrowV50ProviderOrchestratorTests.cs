using Lyrow.Core.Contracts;
using Lyrow.Core.Lyrics;
using Lyrow.Music.Lyrics;
using Xunit;

namespace Lyrow.Music.Tests;

public class LyrowV50ProviderOrchestratorTests
{
    // Real Better Lyrics TTML excerpt from The Weeknd - Blinding Lights (captured live from lyrics-api.boidu.dev)
    private const string RealBetterLyricsTtmlSample = """
<?xml version="1.0" encoding="utf-8"?>
<tt xmlns="http://www.w3.org/ns/ttml" xmlns:itunes="http://music.apple.com/lyric-ttml-internal" itunes:timing="Word">
  <body>
    <div>
      <p begin="27.000" end="29.800">
        <span begin="27.000" end="27.500">I've</span> <span begin="27.500" end="28.000">been</span> <span begin="28.000" end="28.450">try</span><span begin="28.450" end="28.900">na</span> <span begin="28.900" end="29.800">call</span>
      </p>
      <p begin="30.000" end="32.529">
        <span begin="30.000" end="30.400">I've</span> <span begin="30.400" end="30.800">been</span> <span begin="31.839" end="31.996">e</span><span begin="31.996" end="32.529">nough</span>
      </p>
      <p begin="35.729" end="37.394">
        <span begin="35.729" end="36.489">May</span><span begin="36.489" end="37.394">be</span>
      </p>
    </div>
  </body>
</tt>
""";

    // Word-only TTML (space between every span — zero intra-word syllables)
    private const string WordOnlyTtmlSample = """
<?xml version="1.0" encoding="utf-8"?>
<tt xmlns="http://www.w3.org/ns/ttml" xmlns:itunes="http://music.apple.com/lyric-ttml-internal" itunes:timing="Word">
  <body>
    <div>
      <p begin="10.000" end="14.000">
        <span begin="10.000" end="12.000">Hello</span> <span begin="12.000" end="14.000">world</span>
      </p>
    </div>
  </body>
</tt>
""";

    // Line-only TTML (no spans inside <p>)
    private const string LineOnlyTtmlSample = """
<?xml version="1.0" encoding="utf-8"?>
<tt xmlns="http://www.w3.org/ns/ttml">
  <body>
    <div>
      <p begin="00:05.000" end="00:09.000">First line</p>
      <p begin="00:10.000" end="00:14.000">Second line</p>
    </div>
  </body>
</tt>
""";

    private sealed class StubSyncProvider : ILyricsSyncProvider
    {
        private readonly Func<string, string, TimeSpan?, Task<LyricsTimingData?>> _handler;

        public StubSyncProvider(LyricsProviderId providerId, Func<string, string, TimeSpan?, Task<LyricsTimingData?>> handler)
        {
            ProviderId = providerId;
            _handler = handler;
        }

        public LyricsProviderId ProviderId { get; }
        public int CallCount { get; private set; }

        public Task<LyricsTimingData?> FetchTimingDataAsync(string trackName, string artistName, TimeSpan? duration = null, CancellationToken cancellationToken = default)
        {
            CallCount++;
            return _handler(trackName, artistName, duration);
        }
    }

    [Fact]
    public void TtmlParser_DistinguishesRealSyllablesFromWordOnlyAndLineOnlyTtml()
    {
        var syllableData = TtmlLyricsParser.Parse(RealBetterLyricsTtmlSample, "Blinding Lights", "The Weeknd", LyricsProviderId.BetterLyrics);
        Assert.NotNull(syllableData);
        Assert.True(syllableData!.HasTimedSyllables);
        Assert.Equal(3, syllableData.Lines.Count);
        var enoughWord = syllableData.Lines[1].Words.Last();
        Assert.Equal("enough", enoughWord.Text);
        Assert.Equal(2, enoughWord.Syllables.Count);
        Assert.Equal("e", enoughWord.Syllables[0].Text);
        Assert.Equal("nough", enoughWord.Syllables[1].Text);

        // Word-only TTML must NOT be falsely promoted to TimedSyllables
        var wordOnly = TtmlLyricsParser.Parse(WordOnlyTtmlSample, "Hello", "Artist", LyricsProviderId.BetterLyricsUnison);
        Assert.NotNull(wordOnly);
        Assert.True(wordOnly!.HasTimedWords);
        Assert.False(wordOnly.HasTimedSyllables);
        Assert.Equal(2, wordOnly.Lines[0].Words.Count);
        Assert.Empty(wordOnly.Lines[0].Words[0].Syllables);

        // Line-only TTML must remain TimedLines
        var lineOnly = TtmlLyricsParser.Parse(LineOnlyTtmlSample, "Track", "Artist", LyricsProviderId.BetterLyrics);
        Assert.NotNull(lineOnly);
        Assert.True(lineOnly!.HasTimedLines);
        Assert.False(lineOnly.HasTimedWords);
        Assert.False(lineOnly.HasTimedSyllables);
        Assert.Equal(2, lineOnly.Lines.Count);
        Assert.Empty(lineOnly.Lines[0].Words);
    }

    [Fact]
    public async Task Orchestrator_SyllableMode_FollowsExactPriorityAndFallsBackOnFailure()
    {
        var callOrder = new List<LyricsProviderId>();
        var syllableData = TtmlLyricsParser.Parse(RealBetterLyricsTtmlSample, "Blinding Lights", "The Weeknd", LyricsProviderId.BetterLyricsUnison)!;

        var p1 = new StubSyncProvider(LyricsProviderId.BetterLyrics,
            (_, _, _) => { callOrder.Add(LyricsProviderId.BetterLyrics); return Task.FromResult<LyricsTimingData?>(null); });
        var p2 = new StubSyncProvider(LyricsProviderId.BetterLyricsUnison,
            (_, _, _) => { callOrder.Add(LyricsProviderId.BetterLyricsUnison); return Task.FromResult<LyricsTimingData?>(syllableData); });
        var p3 = new StubSyncProvider(LyricsProviderId.BiniLyrics,
            (_, _, _) => { callOrder.Add(LyricsProviderId.BiniLyrics); return Task.FromResult<LyricsTimingData?>(null); });

        var orchestrator = new MultiProviderLyricsOrchestrator(new ILyricsSyncProvider[] { p1, p2, p3 });
        var result = await orchestrator.ResolveLyricsAsync("Blinding Lights", "The Weeknd", LyricsSyncMode.Syllable, TimeSpan.FromSeconds(200));

        Assert.True(result.Success);
        Assert.Equal(LyricsProviderId.BetterLyricsUnison, result.ProviderId);
        Assert.Equal(LyricsSyncMode.Syllable, result.ResolvedMode);
        Assert.Null(result.RecommendedMode);
        Assert.Equal(new[] { LyricsProviderId.BetterLyrics, LyricsProviderId.BetterLyricsUnison }, callOrder);
        Assert.Equal(0, p3.CallCount);
    }

    [Fact]
    public async Task Orchestrator_StrictSyllableMode_RejectsLineOnlyResponseAndRecommendsLineMode()
    {
        var lineOnly = TtmlLyricsParser.Parse(LineOnlyTtmlSample, "Track", "Artist", LyricsProviderId.BetterLyrics)!;

        var p1 = new StubSyncProvider(LyricsProviderId.BetterLyrics,
            (_, _, _) => Task.FromResult<LyricsTimingData?>(lineOnly));
        var p2 = new StubSyncProvider(LyricsProviderId.BetterLyricsUnison,
            (_, _, _) => Task.FromResult<LyricsTimingData?>(null));

        var orchestrator = new MultiProviderLyricsOrchestrator(new ILyricsSyncProvider[] { p1, p2 });
        var result = await orchestrator.ResolveLyricsAsync("Track", "Artist", LyricsSyncMode.Syllable, TimeSpan.FromSeconds(180));

        Assert.True(result.Success);
        Assert.Equal(LyricsSyncMode.Syllable, result.RequestedMode);
        Assert.Equal(LyricsSyncMode.Line, result.ResolvedMode);
        Assert.Equal(LyricsSyncMode.Line, result.RecommendedMode);
        Assert.True(result.TimingData.HasTimedLines);
        Assert.False(result.TimingData.HasTimedSyllables);
    }

    [Fact]
    public async Task Orchestrator_WordMode_FollowsUnisonThenPortatoPriority()
    {
        var callOrder = new List<LyricsProviderId>();
        var wordData = TtmlLyricsParser.Parse(WordOnlyTtmlSample, "Hello", "Artist", LyricsProviderId.BetterLyricsPortato)!;

        var unison = new StubSyncProvider(LyricsProviderId.BetterLyricsUnison,
            (_, _, _) => { callOrder.Add(LyricsProviderId.BetterLyricsUnison); return Task.FromResult<LyricsTimingData?>(null); });
        var portato = new StubSyncProvider(LyricsProviderId.BetterLyricsPortato,
            (_, _, _) => { callOrder.Add(LyricsProviderId.BetterLyricsPortato); return Task.FromResult<LyricsTimingData?>(wordData); });

        var orchestrator = new MultiProviderLyricsOrchestrator(new ILyricsSyncProvider[] { unison, portato });
        var result = await orchestrator.ResolveLyricsAsync("Hello", "Artist", LyricsSyncMode.Word, TimeSpan.FromSeconds(150));

        Assert.True(result.Success);
        Assert.Equal(LyricsProviderId.BetterLyricsPortato, result.ProviderId);
        Assert.Equal(LyricsSyncMode.Word, result.ResolvedMode);
        Assert.Equal(new[] { LyricsProviderId.BetterLyricsUnison, LyricsProviderId.BetterLyricsPortato }, callOrder);
    }

    [Fact]
    public async Task Orchestrator_LineMode_FallsBackAcrossFiveProvidersToLrclib()
    {
        var callOrder = new List<LyricsProviderId>();
        var lrcLineData = TtmlLyricsParser.Parse(LineOnlyTtmlSample, "Track", "Artist", LyricsProviderId.Lrclib)!;

        var p1 = new StubSyncProvider(LyricsProviderId.BetterLyrics,
            (_, _, _) => { callOrder.Add(LyricsProviderId.BetterLyrics); return Task.FromResult<LyricsTimingData?>(null); });
        var p2 = new StubSyncProvider(LyricsProviderId.BetterLyricsUnison,
            (_, _, _) => { callOrder.Add(LyricsProviderId.BetterLyricsUnison); return Task.FromResult<LyricsTimingData?>(null); });
        var p3 = new StubSyncProvider(LyricsProviderId.BiniLyrics,
            (_, _, _) => { callOrder.Add(LyricsProviderId.BiniLyrics); return Task.FromResult<LyricsTimingData?>(null); });
        var p4 = new StubSyncProvider(LyricsProviderId.Lrclib,
            (_, _, _) => { callOrder.Add(LyricsProviderId.Lrclib); return Task.FromResult<LyricsTimingData?>(lrcLineData); });
        var p5 = new StubSyncProvider(LyricsProviderId.BetterLyricsLegato,
            (_, _, _) => { callOrder.Add(LyricsProviderId.BetterLyricsLegato); return Task.FromResult<LyricsTimingData?>(null); });

        var orchestrator = new MultiProviderLyricsOrchestrator(new ILyricsSyncProvider[] { p1, p2, p3, p4, p5 });
        var result = await orchestrator.ResolveLyricsAsync("Track", "Artist", LyricsSyncMode.Line, TimeSpan.FromSeconds(180));

        Assert.True(result.Success);
        Assert.Equal(LyricsProviderId.Lrclib, result.ProviderId);
        Assert.Equal(LyricsSyncMode.Line, result.ResolvedMode);
        Assert.Equal(new[]
        {
            LyricsProviderId.BetterLyrics,
            LyricsProviderId.BetterLyricsUnison,
            LyricsProviderId.BiniLyrics,
            LyricsProviderId.Lrclib
        }, callOrder);
        Assert.Equal(0, p5.CallCount);
    }
}
