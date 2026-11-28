namespace Lyrow.Core.Lyrics;

/// <summary>
/// Manages canonical ordering and drag-and-drop reordering of active lyrics providers.
/// Used by both the Settings > Lyrics UI and <see cref="MultiProviderLyricsOrchestrator"/>.
/// </summary>
public static class LyricsSourceOrderHelper
{
    public static readonly IReadOnlyList<LyricsProviderId> DefaultActiveProviderOrder =
    [
        LyricsProviderId.BetterLyrics,
        LyricsProviderId.AppleMusic,
        LyricsProviderId.BetterLyricsUnison,
        LyricsProviderId.QQMusic,
        LyricsProviderId.NetEase,
        LyricsProviderId.KuGou,
        LyricsProviderId.Kuwo,
        LyricsProviderId.Migu,
        LyricsProviderId.PetitLyrics,
        LyricsProviderId.Lrclib
    ];

    /// <summary>
    /// Normalizes a persisted provider name list into a deduplicated, complete ordered list of all active providers.
    /// </summary>
    public static IReadOnlyList<LyricsProviderId> NormalizeOrder(IEnumerable<string>? savedOrder)
    {
        var result = new List<LyricsProviderId>(DefaultActiveProviderOrder.Count);
        var seen = new HashSet<LyricsProviderId>();

        if (savedOrder != null)
        {
            foreach (string raw in savedOrder)
            {
                if (string.IsNullOrWhiteSpace(raw)) continue;
                if (Enum.TryParse<LyricsProviderId>(raw.Trim(), ignoreCase: true, out var id) &&
                    DefaultActiveProviderOrder.Contains(id) &&
                    seen.Add(id))
                {
                    result.Add(id);
                }
            }
        }

        foreach (var id in DefaultActiveProviderOrder)
        {
            if (seen.Add(id))
            {
                result.Add(id);
            }
        }

        return result;
    }

    /// <summary>
    /// Moves an item from <paramref name="fromIndex"/> to <paramref name="toIndex"/> and returns the updated order.
    /// </summary>
    public static IReadOnlyList<LyricsProviderId> Reorder(
        IReadOnlyList<LyricsProviderId> currentOrder,
        int fromIndex,
        int toIndex)
    {
        ArgumentNullException.ThrowIfNull(currentOrder);
        if (currentOrder.Count == 0 ||
            fromIndex < 0 || fromIndex >= currentOrder.Count ||
            toIndex < 0 || toIndex >= currentOrder.Count ||
            fromIndex == toIndex)
        {
            return currentOrder.ToArray();
        }

        var list = currentOrder.ToList();
        var item = list[fromIndex];
        list.RemoveAt(fromIndex);
        list.Insert(toIndex, item);
        return list;
    }

    /// <summary>
    /// Serializes a provider order into canonical string names suitable for persistent settings storage.
    /// </summary>
    public static List<string> ToSettingsList(IEnumerable<LyricsProviderId> order)
        => order.Select(id => id.ToString()).ToList();
}
