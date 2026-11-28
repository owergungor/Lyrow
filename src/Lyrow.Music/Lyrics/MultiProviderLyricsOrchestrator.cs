using System.Collections.Concurrent;
using Lyrow.Core.Contracts;
using Lyrow.Core.Lyrics;
using Lyrow.Core.Models;

namespace Lyrow.Music.Lyrics;

/// <summary>
/// Configurable per-provider timeout policy for fast parallel lyrics resolution.
/// </summary>
public sealed class LyricsProviderTimeoutOptions
{
    public TimeSpan DefaultTimeout { get; init; } = TimeSpan.FromMilliseconds(2500);
    public TimeSpan BetterLyricsTimeout { get; init; } = TimeSpan.FromMilliseconds(2800);
    public TimeSpan BetterLyricsUnisonTimeout { get; init; } = TimeSpan.FromMilliseconds(2200);
    public TimeSpan LrclibTimeout { get; init; } = TimeSpan.FromMilliseconds(2500);
    public TimeSpan BiniLyricsTimeout { get; init; } = TimeSpan.FromMilliseconds(2200);
    public TimeSpan PortatoTimeout { get; init; } = TimeSpan.FromMilliseconds(2200);
    public TimeSpan LegatoTimeout { get; init; } = TimeSpan.FromMilliseconds(2200);

    public TimeSpan GetTimeout(LyricsProviderId providerId) => providerId switch
    {
        LyricsProviderId.BetterLyrics => BetterLyricsTimeout,
        LyricsProviderId.BetterLyricsUnison => BetterLyricsUnisonTimeout,
        LyricsProviderId.Lrclib => LrclibTimeout,
        LyricsProviderId.BiniLyrics => BiniLyricsTimeout,
        LyricsProviderId.BetterLyricsPortato => PortatoTimeout,
        LyricsProviderId.BetterLyricsLegato => LegatoTimeout,
        _ => DefaultTimeout
    };
}

/// <summary>
/// High-performance mode-aware multi-provider lyrics orchestrator (Lyrow v5.0).
/// Features:
/// - Controlled parallel provider querying with short configurable per-provider timeouts.
/// - Immediate first-usable-result delivery + seamless progressive upgrade to higher precision (never downgrades).
/// - Per-track in-flight request deduplication and in-memory result caching to prevent duplicate network calls.
/// - Strict capability validation via <see cref="LyricsProviderPriorityResolver"/>.
/// </summary>
public sealed class MultiProviderLyricsOrchestrator : IMultiProviderLyricsService
{
    private readonly IReadOnlyDictionary<LyricsProviderId, ILyricsSyncProvider> _providers;
    private readonly IReadOnlyList<IMultiTrackLyricsProvider> _multiTrackProviders;
    private readonly LyricsProviderPriorityResolver _resolver;
    private readonly ISettingsManager? _settingsManager;
    private readonly LyricsProviderTimeoutOptions _timeouts;

    // In-memory per-provider timing cache: "{providerId}|{artist}|{track}" -> LyricsTimingData
    private readonly ConcurrentDictionary<string, LyricsTimingData> _providerDataCache = new(StringComparer.OrdinalIgnoreCase);
    // In-flight deduplication: "{providerId}|{artist}|{track}" -> shared Task<LyricsTimingData?>
    private readonly ConcurrentDictionary<string, Task<LyricsTimingData?>> _inFlightProviderCalls = new(StringComparer.OrdinalIgnoreCase);
    // In-flight deduplication for multi-track aggregators (e.g. LiriQo)
    private readonly ConcurrentDictionary<string, Task<IReadOnlyList<LyricsTimingData>>> _inFlightMultiTrackCalls = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, IReadOnlyList<LyricsTimingData>> _multiTrackCache = new(StringComparer.OrdinalIgnoreCase);
    // Resolved session cache: "{mode}|{artist}|{track}" -> LyricsResolutionResult
    private readonly ConcurrentDictionary<string, LyricsResolutionResult> _resolvedCache = new(StringComparer.OrdinalIgnoreCase);

    public MultiProviderLyricsOrchestrator(
        IEnumerable<ILyricsSyncProvider> providers,
        LyricsProviderPriorityResolver? resolver = null,
        ISettingsManager? settingsManager = null,
        LyricsProviderTimeoutOptions? timeouts = null,
        IEnumerable<IMultiTrackLyricsProvider>? multiTrackProviders = null)
    {
        _providers = (providers ?? throw new ArgumentNullException(nameof(providers)))
            .ToDictionary(p => p.ProviderId);
        _multiTrackProviders = multiTrackProviders?.ToArray() ?? Array.Empty<IMultiTrackLyricsProvider>();
        _resolver = resolver ?? new LyricsProviderPriorityResolver();
        _settingsManager = settingsManager;
        _timeouts = timeouts ?? new LyricsProviderTimeoutOptions();
    }

    public LyricsResolutionResult? LastResolution { get; private set; }

    public LyricsProviderTimeoutOptions TimeoutOptions => _timeouts;

    public void ClearMemoryCache()
    {
        _providerDataCache.Clear();
        _multiTrackCache.Clear();
        _resolvedCache.Clear();
    }

    /// <summary>
    /// Backward-compatible <see cref="ILyricsClient"/> entry point.
    /// Reads the user's configured <see cref="LyricsModuleSettings.SyncMode"/> (defaults to Automatic).
    /// </summary>
    public async Task<LyricsSearchResult> FetchLyricsAsync(
        string trackName,
        string artistName,
        TimeSpan? duration = null,
        CancellationToken cancellationToken = default)
    {
        var requestedMode = _settingsManager?.Current.Lyrics.SyncMode ?? LyricsSyncMode.Automatic;
        var resolved = await ResolveLyricsProgressiveAsync(trackName, artistName, requestedMode, null, duration, cancellationToken).ConfigureAwait(false);
        LastResolution = resolved;

        if (!resolved.Success || resolved.SyncedLyrics == null)
        {
            return new LyricsSearchResult(false, null, resolved.ProviderId?.ToString() ?? "MultiProvider", resolved.StatusMessage);
        }

        string sourceLabel = resolved.ProviderId.HasValue
            ? _resolver.GetCapability(resolved.ProviderId.Value).DisplayName
            : "Lyrow";

        return new LyricsSearchResult(true, resolved.SyncedLyrics, sourceLabel);
    }

    /// <summary>
    /// Priority-ordered strict resolution (honors exact provider call order and short-circuits on strict match).
    /// Uses per-provider timeouts, deduplication, and caching.
    /// </summary>
    public async Task<LyricsResolutionResult> ResolveLyricsAsync(
        string trackName,
        string artistName,
        LyricsSyncMode requestedMode,
        TimeSpan? duration = null,
        CancellationToken cancellationToken = default)
    {
        string resolvedKey = BuildResolvedCacheKey(requestedMode, artistName, trackName);
        if (_resolvedCache.TryGetValue(resolvedKey, out var cachedRes))
        {
            LastResolution = cachedRes;
            return cachedRes;
        }

        var priorityOrder = _resolver.GetPriorityOrder(requestedMode);
        var attempted = new List<LyricsProviderId>(priorityOrder.Count);
        var fallbackCandidates = new List<(LyricsProviderId Provider, LyricsTimingData Data)>();

        foreach (var providerId in priorityOrder)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!_providers.TryGetValue(providerId, out var provider)) continue;

            attempted.Add(providerId);
            var data = await QueryProviderWithTimeoutAndDedupAsync(provider, trackName, artistName, duration, cancellationToken).ConfigureAwait(false);
            if (data == null || data.ActualGranularity == LyricsTimingGranularity.None) continue;

            if (requestedMode == LyricsSyncMode.Automatic)
            {
                fallbackCandidates.Add((providerId, data));
                if (data.HasTimedSyllables)
                {
                    var autoResult = BuildSuccess(requestedMode, LyricsSyncMode.Syllable, null, providerId, data, attempted, "Syllable sync active.");
                    _resolvedCache[resolvedKey] = autoResult;
                    LastResolution = autoResult;
                    return autoResult;
                }
            }
            else if (LyricsProviderPriorityResolver.SatisfiesStrictMode(requestedMode, data))
            {
                var strictResult = BuildSuccess(requestedMode, requestedMode, null, providerId, data, attempted, $"{requestedMode} sync active.");
                _resolvedCache[resolvedKey] = strictResult;
                LastResolution = strictResult;
                return strictResult;
            }
            else
            {
                fallbackCandidates.Add((providerId, data));
            }
        }

        if (requestedMode == LyricsSyncMode.Automatic && fallbackCandidates.Count > 0)
        {
            var best = SelectBestCandidate(fallbackCandidates);
            var bestMode = best.Data.BestSupportedMode;
            var res = BuildSuccess(requestedMode, bestMode, null, best.Provider, best.Data, attempted, $"Automatic selected {bestMode} sync.");
            _resolvedCache[resolvedKey] = res;
            LastResolution = res;
            return res;
        }

        if (fallbackCandidates.Count == 0 && requestedMode != LyricsSyncMode.Automatic)
        {
            foreach (var fallbackId in _resolver.GetPriorityOrder(LyricsSyncMode.Automatic))
            {
                if (attempted.Contains(fallbackId)) continue;
                if (!_providers.TryGetValue(fallbackId, out var provider)) continue;

                attempted.Add(fallbackId);
                var data = await QueryProviderWithTimeoutAndDedupAsync(provider, trackName, artistName, duration, cancellationToken).ConfigureAwait(false);
                if (data != null && data.ActualGranularity != LyricsTimingGranularity.None)
                {
                    fallbackCandidates.Add((fallbackId, data));
                    break;
                }
            }
        }

        if (fallbackCandidates.Count > 0)
        {
            var res = BuildFallbackResult(requestedMode, fallbackCandidates, attempted);
            _resolvedCache[resolvedKey] = res;
            LastResolution = res;
            return res;
        }

        var notFound = BuildNotFound(requestedMode, attempted);
        LastResolution = notFound;
        return notFound;
    }

    /// <summary>
    /// Fast parallel multi-provider resolution:
    /// - Launches all applicable providers concurrently with per-provider short timeouts.
    /// - Emits the first usable result immediately via <paramref name="onInterimResult"/> so lyrics appear right away.
    /// - Continues listening for remaining providers and upgrades seamlessly ONLY when strictly higher precision arrives.
    /// - Never blocks on a slow/unresponsive provider; never downgrades precision; cancels cleanly on track switch.
    /// </summary>
    public async Task<LyricsResolutionResult> ResolveLyricsProgressiveAsync(
        string trackName,
        string artistName,
        LyricsSyncMode requestedMode,
        Func<LyricsResolutionResult, Task>? onInterimResult = null,
        TimeSpan? duration = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        string resolvedKey = BuildResolvedCacheKey(requestedMode, artistName, trackName);
        if (_resolvedCache.TryGetValue(resolvedKey, out var cachedRes))
        {
            LastResolution = cachedRes;
            if (onInterimResult != null && cachedRes.Success)
            {
                await onInterimResult(cachedRes).ConfigureAwait(false);
            }
            return cachedRes;
        }

        // Build candidate provider list: primary chain for mode + fallback providers for Automatic/honest fallback
        var primaryOrder = _resolver.GetPriorityOrder(requestedMode);
        var queryList = new List<LyricsProviderId>(primaryOrder);
        if (requestedMode != LyricsSyncMode.Automatic)
        {
            foreach (var fallbackId in _resolver.GetPriorityOrder(LyricsSyncMode.Automatic))
            {
                if (!queryList.Contains(fallbackId))
                {
                    queryList.Add(fallbackId);
                }
            }
        }

        var attempted = new List<LyricsProviderId>(queryList.Count);
        var primaryRankMap = new Dictionary<LyricsProviderId, int>();

        // Apply user's persisted drag-and-drop source priority order (within equal genuine sync quality;
        // Syllable > Word > Line > Unsynced quality superiority is strictly preserved below)
        var customOrder = LyricsSourceOrderHelper.NormalizeOrder(_settingsManager?.Current.Lyrics.SourcePriorityOrder);
        for (int i = 0; i < customOrder.Count; i++)
        {
            primaryRankMap[customOrder[i]] = i;
        }
        for (int i = 0; i < queryList.Count; i++)
        {
            if (!primaryRankMap.ContainsKey(queryList[i]))
            {
                primaryRankMap[queryList[i]] = customOrder.Count + i;
            }
        }

        string? preferredName = _settingsManager?.Current.Lyrics.PreferredProvider;
        if (!string.IsNullOrWhiteSpace(preferredName) &&
            !string.Equals(preferredName, "Auto", StringComparison.OrdinalIgnoreCase) &&
            Enum.TryParse<LyricsProviderId>(preferredName, ignoreCase: true, out var preferredId))
        {
            // Preferred provider wins ties within the same highest genuine sync precision without ever downgrading quality
            primaryRankMap[preferredId] = -1;
        }

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var tasks = new List<Task<IReadOnlyList<(LyricsProviderId ProviderId, LyricsTimingData Data)>>>(queryList.Count + _multiTrackProviders.Count);

        foreach (var providerId in queryList)
        {
            if (!_providers.TryGetValue(providerId, out var provider)) continue;
            // Skip unconfigured adapters with zero delay
            if (provider is UnconfiguredSyncProvider) continue;

            attempted.Add(providerId);
            tasks.Add(QuerySingleProviderAsListAsync(provider, trackName, artistName, duration, linkedCts.Token));
        }

        // Launch multi-track aggregators (e.g. LiriQo) in parallel alongside direct providers
        foreach (var aggregator in _multiTrackProviders)
        {
            tasks.Add(QueryMultiTrackAggregatorAsync(aggregator, trackName, artistName, duration, linkedCts.Token));
        }

        if (tasks.Count == 0)
        {
            var emptyRes = BuildNotFound(requestedMode, attempted);
            LastResolution = emptyRes;
            return emptyRes;
        }

        LyricsResolutionResult? bestEmitted = null;
        int bestEmittedPrecision = -1;
        int bestEmittedProviderPriority = int.MaxValue;
        var allCandidates = new List<(LyricsProviderId Provider, LyricsTimingData Data)>();
        // Canonical upstream deduplication: ensures the same upstream (e.g. LRC or APL) never appears twice in < / > switcher
        var verifiedSourcesByProvider = new Dictionary<LyricsProviderId, LyricsTrackSourceEntry>();

        while (tasks.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var finished = await Task.WhenAny(tasks).ConfigureAwait(false);
            tasks.Remove(finished);

            IReadOnlyList<(LyricsProviderId ProviderId, LyricsTimingData Data)> batch;
            try
            {
                batch = await finished.ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                continue;
            }

            if (batch == null || batch.Count == 0) continue;

            bool anySourceListChanged = false;
            LyricsResolutionResult? pendingUpgrade = null;

            foreach (var (providerId, data) in batch)
            {
                if (data == null || data.ActualGranularity == LyricsTimingGranularity.None) continue;
                if (!attempted.Contains(providerId)) attempted.Add(providerId);

                allCandidates.Add((providerId, data));

                var candidateRes = EvaluateCandidate(requestedMode, providerId, data, attempted);
                int candidatePrecision = LyricsProviderPriorityResolver.GetPrecisionRank(candidateRes.ResolvedMode);
                int candidateProviderOrder = primaryRankMap.TryGetValue(providerId, out int rank) ? rank : int.MaxValue;

                // Register verified source entry (deduplicated by canonical upstream ProviderId)
                string? badge = LyricsSyncCodeFormatter.Format(candidateRes);
                if (!string.IsNullOrEmpty(badge) && candidateRes.SyncedLyrics != null)
                {
                    string displayName = _resolver.GetCapability(providerId).DisplayName;
                    var entry = new LyricsTrackSourceEntry(
                        providerId,
                        candidateRes.ResolvedMode,
                        badge,
                        candidateRes.TimingData,
                        candidateRes.SyncedLyrics,
                        displayName);

                    if (!verifiedSourcesByProvider.TryGetValue(providerId, out var existing) ||
                        LyricsProviderPriorityResolver.GetPrecisionRank(entry.ResolvedMode) > LyricsProviderPriorityResolver.GetPrecisionRank(existing.ResolvedMode))
                    {
                        verifiedSourcesByProvider[providerId] = entry;
                        anySourceListChanged = true;
                    }
                }

                bool isStrictUpgrade = bestEmitted == null
                    || candidatePrecision > bestEmittedPrecision
                    || (candidatePrecision == bestEmittedPrecision && candidateProviderOrder < bestEmittedProviderPriority);

                if (isStrictUpgrade)
                {
                    bestEmitted = candidateRes;
                    bestEmittedPrecision = candidatePrecision;
                    bestEmittedProviderPriority = candidateProviderOrder;
                    pendingUpgrade = candidateRes;
                }
            }

            if (bestEmitted != null && (pendingUpgrade != null || anySourceListChanged))
            {
                var orderedSources = OrderVerifiedSources(verifiedSourcesByProvider.Values, primaryRankMap);
                bestEmitted = bestEmitted with { AvailableSources = orderedSources };
                LastResolution = bestEmitted;

                if (onInterimResult != null)
                {
                    await onInterimResult(bestEmitted).ConfigureAwait(false);
                }

                // Only short-circuit early when NO multi-track aggregator is configured and max precision is already reached
                if (_multiTrackProviders.Count == 0 && pendingUpgrade != null)
                {
                    bool reachedTarget = requestedMode switch
                    {
                        LyricsSyncMode.Automatic => bestEmitted.ResolvedMode == LyricsSyncMode.Syllable,
                        LyricsSyncMode.Syllable => bestEmitted.ResolvedMode == LyricsSyncMode.Syllable,
                        LyricsSyncMode.Word => bestEmitted.ResolvedMode == LyricsSyncMode.Word && bestEmittedProviderPriority == 0,
                        LyricsSyncMode.Line => bestEmitted.ResolvedMode == LyricsSyncMode.Line && bestEmittedProviderPriority == 0,
                        LyricsSyncMode.Asynchronous => bestEmitted.ResolvedMode == LyricsSyncMode.Asynchronous,
                        _ => false
                    };

                    if (reachedTarget)
                    {
                        linkedCts.Cancel();
                        break;
                    }
                }
            }
        }

        if (bestEmitted != null)
        {
            var orderedSources = OrderVerifiedSources(verifiedSourcesByProvider.Values, primaryRankMap);
            bestEmitted = bestEmitted with { AvailableSources = orderedSources };
            _resolvedCache[resolvedKey] = bestEmitted;
            LastResolution = bestEmitted;
            return bestEmitted;
        }

        if (allCandidates.Count > 0)
        {
            var fallback = BuildFallbackResult(requestedMode, allCandidates, attempted);
            var orderedSources = OrderVerifiedSources(verifiedSourcesByProvider.Values, primaryRankMap);
            fallback = fallback with { AvailableSources = orderedSources };
            _resolvedCache[resolvedKey] = fallback;
            LastResolution = fallback;
            return fallback;
        }

        var notFound = BuildNotFound(requestedMode, attempted);
        LastResolution = notFound;
        return notFound;
    }

    private static IReadOnlyList<LyricsTrackSourceEntry> OrderVerifiedSources(
        IEnumerable<LyricsTrackSourceEntry> entries,
        Dictionary<LyricsProviderId, int> rankMap)
    {
        return entries
            .OrderByDescending(e => LyricsProviderPriorityResolver.GetPrecisionRank(e.ResolvedMode))
            .ThenBy(e => rankMap.TryGetValue(e.ProviderId, out int r) ? r : int.MaxValue)
            .ToArray();
    }

    private async Task<IReadOnlyList<(LyricsProviderId ProviderId, LyricsTimingData Data)>> QuerySingleProviderAsListAsync(
        ILyricsSyncProvider provider,
        string trackName,
        string artistName,
        TimeSpan? duration,
        CancellationToken ct)
    {
        var data = await QueryProviderWithTimeoutAndDedupAsync(provider, trackName, artistName, duration, ct).ConfigureAwait(false);
        return data != null
            ? new[] { (provider.ProviderId, data) }
            : Array.Empty<(LyricsProviderId, LyricsTimingData)>();
    }

    private async Task<IReadOnlyList<(LyricsProviderId ProviderId, LyricsTimingData Data)>> QueryMultiTrackAggregatorAsync(
        IMultiTrackLyricsProvider aggregator,
        string trackName,
        string artistName,
        TimeSpan? duration,
        CancellationToken callerCt)
    {
        string key = $"{aggregator.AggregatorName}|{(artistName ?? string.Empty).Trim().ToLowerInvariant()}|{(trackName ?? string.Empty).Trim().ToLowerInvariant()}";
        if (_multiTrackCache.TryGetValue(key, out var cached))
        {
            return cached.Select(d => (d.SourceProvider, d)).ToArray();
        }

        var sharedTask = _inFlightMultiTrackCalls.GetOrAdd(key, _ => ExecuteMultiTrackCallAsync(aggregator, key, trackName ?? string.Empty, artistName ?? string.Empty, duration));
        try
        {
            var tracks = await sharedTask.WaitAsync(callerCt).ConfigureAwait(false);
            return tracks.Select(d => (d.SourceProvider, d)).ToArray();
        }
        catch (OperationCanceledException) when (callerCt.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return Array.Empty<(LyricsProviderId, LyricsTimingData)>();
        }
    }

    private async Task<IReadOnlyList<LyricsTimingData>> ExecuteMultiTrackCallAsync(
        IMultiTrackLyricsProvider aggregator,
        string key,
        string trackName,
        string artistName,
        TimeSpan? duration)
    {
        var timeout = TimeSpan.FromMilliseconds(4500);
        using var timeoutCts = new CancellationTokenSource(timeout);
        try
        {
            var tracks = await aggregator.FetchAllUpstreamTracksAsync(trackName, artistName, duration, timeoutCts.Token)
                .WaitAsync(timeout, timeoutCts.Token)
                .ConfigureAwait(false);
            if (tracks.Count > 0)
            {
                _multiTrackCache[key] = tracks;
            }
            return tracks;
        }
        catch
        {
            return Array.Empty<LyricsTimingData>();
        }
        finally
        {
            _inFlightMultiTrackCalls.TryRemove(key, out _);
        }
    }

    private async Task<LyricsTimingData?> QueryProviderWithTimeoutAndDedupAsync(
        ILyricsSyncProvider provider,
        string trackName,
        string artistName,
        TimeSpan? duration,
        CancellationToken callerCt)
    {
        string key = BuildProviderCacheKey(provider.ProviderId, artistName, trackName);
        if (_providerDataCache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var timeout = _timeouts.GetTimeout(provider.ProviderId);

        var sharedTask = _inFlightProviderCalls.GetOrAdd(key, _ => ExecuteProviderCallAsync(provider, key, trackName, artistName, duration, timeout));
        try
        {
            return await sharedTask.WaitAsync(callerCt).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (callerCt.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }

    private async Task<LyricsTimingData?> ExecuteProviderCallAsync(
        ILyricsSyncProvider provider,
        string cacheKey,
        string trackName,
        string artistName,
        TimeSpan? duration,
        TimeSpan timeout)
    {
        using var timeoutCts = new CancellationTokenSource(timeout);
        try
        {
            var fetchTask = provider.FetchTimingDataAsync(trackName, artistName, duration, timeoutCts.Token);
            var data = await fetchTask.WaitAsync(timeout, timeoutCts.Token).ConfigureAwait(false);
            if (data != null && data.ActualGranularity != LyricsTimingGranularity.None)
            {
                _providerDataCache[cacheKey] = data;
                return data;
            }
            return null;
        }
        catch
        {
            return null;
        }
        finally
        {
            _inFlightProviderCalls.TryRemove(cacheKey, out _);
        }
    }

    private static LyricsResolutionResult EvaluateCandidate(
        LyricsSyncMode requestedMode,
        LyricsProviderId providerId,
        LyricsTimingData data,
        IReadOnlyList<LyricsProviderId> attempted)
    {
        if (requestedMode == LyricsSyncMode.Automatic)
        {
            var bestMode = data.BestSupportedMode;
            return BuildSuccess(requestedMode, bestMode, null, providerId, data, attempted, $"Automatic selected {bestMode} sync.");
        }

        if (requestedMode == LyricsSyncMode.Asynchronous)
        {
            var plainData = new LyricsTimingData(data.Title, data.Artist, providerId, LyricsTimingGranularity.PlainLyrics, Array.Empty<LyricLineTiming>(), data.PlainLyrics);
            return BuildSuccess(requestedMode, LyricsSyncMode.Asynchronous, null, providerId, plainData, attempted, "Asynchronous lyrics loaded.");
        }

        if (LyricsProviderPriorityResolver.SatisfiesStrictMode(requestedMode, data))
        {
            return BuildSuccess(requestedMode, requestedMode, null, providerId, data, attempted, $"{requestedMode} sync active.");
        }

        var recommended = data.BestSupportedMode;
        return BuildSuccess(
            requestedMode,
            resolved: recommended,
            recommended: requestedMode == recommended ? null : recommended,
            providerId,
            data,
            attempted,
            $"{requestedMode} sync is not available for this track. Recommended mode: {recommended}.");
    }

    private static (LyricsProviderId Provider, LyricsTimingData Data) SelectBestCandidate(
        List<(LyricsProviderId Provider, LyricsTimingData Data)> candidates)
    {
        return candidates
            .OrderByDescending(c => LyricsProviderPriorityResolver.GetPrecisionRank(c.Data.BestSupportedMode))
            .First();
    }

    private static LyricsResolutionResult BuildFallbackResult(
        LyricsSyncMode requestedMode,
        List<(LyricsProviderId Provider, LyricsTimingData Data)> candidates,
        IReadOnlyList<LyricsProviderId> attempted)
    {
        var best = SelectBestCandidate(candidates);
        var recommended = requestedMode == LyricsSyncMode.Asynchronous
            ? LyricsSyncMode.Asynchronous
            : best.Data.BestSupportedMode;

        LyricsTimingData finalData = requestedMode == LyricsSyncMode.Asynchronous
            ? new LyricsTimingData(best.Data.Title, best.Data.Artist, best.Provider, LyricsTimingGranularity.PlainLyrics, Array.Empty<LyricLineTiming>(), best.Data.PlainLyrics)
            : best.Data;

        string msg = requestedMode == LyricsSyncMode.Asynchronous
            ? "Asynchronous lyrics loaded."
            : $"{requestedMode} sync is not available for this track. Recommended mode: {recommended}.";

        return BuildSuccess(
            requestedMode,
            resolved: recommended,
            recommended: requestedMode == recommended ? null : recommended,
            best.Provider,
            finalData,
            attempted,
            msg);
    }

    private static LyricsResolutionResult BuildSuccess(
        LyricsSyncMode requested,
        LyricsSyncMode resolved,
        LyricsSyncMode? recommended,
        LyricsProviderId providerId,
        LyricsTimingData data,
        IReadOnlyList<LyricsProviderId> attempted,
        string message)
    {
        return new LyricsResolutionResult(
            Success: true,
            RequestedMode: requested,
            ResolvedMode: resolved,
            RecommendedMode: recommended,
            ProviderId: providerId,
            TimingData: data,
            SyncedLyrics: data.ToSyncedLyrics(),
            StatusMessage: message,
            AttemptedProviders: attempted);
    }

    private static LyricsResolutionResult BuildNotFound(LyricsSyncMode requestedMode, IReadOnlyList<LyricsProviderId> attempted)
    {
        return new LyricsResolutionResult(
            Success: false,
            RequestedMode: requestedMode,
            ResolvedMode: LyricsSyncMode.Asynchronous,
            RecommendedMode: null,
            ProviderId: null,
            TimingData: LyricsTimingData.Empty,
            SyncedLyrics: null,
            StatusMessage: "No lyrics found across configured providers.",
            AttemptedProviders: attempted);
    }

    private static string BuildProviderCacheKey(LyricsProviderId providerId, string artist, string track)
        => $"{(int)providerId}|{(artist ?? string.Empty).Trim().ToLowerInvariant()}|{(track ?? string.Empty).Trim().ToLowerInvariant()}";

    private static string BuildResolvedCacheKey(LyricsSyncMode mode, string artist, string track)
        => $"{(int)mode}|{(artist ?? string.Empty).Trim().ToLowerInvariant()}|{(track ?? string.Empty).Trim().ToLowerInvariant()}";
}
