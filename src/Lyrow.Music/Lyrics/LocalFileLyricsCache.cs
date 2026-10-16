using System.Text.Json;
using Lyrow.Core.Contracts;
using Lyrow.Core.Models;

namespace Lyrow.Music.Lyrics;

/// <summary>
/// Persistent disk-based lyrics cache storing fetched LRC data in JSON files.
/// Avoids repeated network hits and enables instant offline lyrics display.
/// </summary>
public sealed class LocalFileLyricsCache : ILyricsCache
{
    private readonly string _cacheDirectory;
    private readonly TimeSpan _maxAge;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public LocalFileLyricsCache(string? cacheDirectory = null, TimeSpan? maxAge = null)
    {
        _cacheDirectory = cacheDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Lyrow",
            "LyricsCache");

        _maxAge = maxAge ?? TimeSpan.FromDays(30);

        try
        {
            if (!Directory.Exists(_cacheDirectory))
            {
                Directory.CreateDirectory(_cacheDirectory);
            }
        }
        catch
        {
            // Directory creation will be retried on write
        }
    }

    public async Task<SyncedLyrics?> GetAsync(string trackName, string artistName, CancellationToken cancellationToken = default)
    {
        var filePath = GetCacheFilePath(trackName, artistName);
        if (!File.Exists(filePath)) return null;

        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var fileInfo = new FileInfo(filePath);
            if (DateTime.UtcNow - fileInfo.LastWriteTimeUtc > _maxAge)
            {
                try { File.Delete(filePath); } catch { }
                return null;
            }

            using var stream = File.OpenRead(filePath);
            var dto = await JsonSerializer.DeserializeAsync<LyricsCacheDto>(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
            if (dto == null) return null;

            var lines = dto.Lines.Select(l => new LyricLine(TimeSpan.FromMilliseconds(l.TimestampMs), l.Text, l.Index)).ToList();

            return new SyncedLyrics(
                title: dto.Title,
                artist: dto.Artist,
                isSynced: dto.IsSynced,
                lines: lines,
                plainLyrics: dto.PlainLyrics,
                duration: dto.DurationMs.HasValue ? TimeSpan.FromMilliseconds(dto.DurationMs.Value) : null);
        }
        catch
        {
            return null;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task SaveAsync(string trackName, string artistName, SyncedLyrics lyrics, CancellationToken cancellationToken = default)
    {
        var filePath = GetCacheFilePath(trackName, artistName);
        var tempFilePath = filePath + ".tmp." + Guid.NewGuid().ToString("N");

        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!Directory.Exists(_cacheDirectory))
            {
                Directory.CreateDirectory(_cacheDirectory);
            }

            var dto = new LyricsCacheDto
            {
                Title = lyrics.Title,
                Artist = lyrics.Artist,
                IsSynced = lyrics.IsSynced,
                PlainLyrics = lyrics.PlainLyrics,
                DurationMs = lyrics.Duration.HasValue ? (long)lyrics.Duration.Value.TotalMilliseconds : null,
                SavedAtUtc = DateTime.UtcNow,
                Lines = lyrics.Lines.Select(l => new LyricLineDto
                {
                    TimestampMs = (long)l.Timestamp.TotalMilliseconds,
                    Text = l.Text,
                    Index = l.LineIndex
                }).ToList()
            };

            await using (var stream = File.Create(tempFilePath))
            {
                await JsonSerializer.SerializeAsync(stream, dto, cancellationToken: cancellationToken).ConfigureAwait(false);
            }

            File.Move(tempFilePath, filePath, overwrite: true);
        }
        catch
        {
            try
            {
                if (File.Exists(tempFilePath)) File.Delete(tempFilePath);
            }
            catch { }
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (Directory.Exists(_cacheDirectory))
            {
                var files = Directory.GetFiles(_cacheDirectory, "*.json");
                foreach (var file in files)
                {
                    try { File.Delete(file); } catch { }
                }
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    private string GetCacheFilePath(string trackName, string artistName)
    {
        string safeArtist = SanitizeFileName(artistName);
        string safeTrack = SanitizeFileName(trackName);
        string fileName = $"{safeArtist}_{safeTrack}.json";
        return Path.Combine(_cacheDirectory, fileName);
    }

    public static string SanitizeFileName(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return "unknown";

        var invalidChars = Path.GetInvalidFileNameChars();
        var chars = input.Trim().ToCharArray();
        for (int i = 0; i < chars.Length; i++)
        {
            if (Array.IndexOf(invalidChars, chars[i]) >= 0 || chars[i] == ' ' || chars[i] == '.')
            {
                chars[i] = '_';
            }
        }
        return new string(chars).ToLowerInvariant();
    }

    private sealed class LyricsCacheDto
    {
        public string Title { get; set; } = string.Empty;
        public string Artist { get; set; } = string.Empty;
        public bool IsSynced { get; set; }
        public string? PlainLyrics { get; set; }
        public long? DurationMs { get; set; }
        public DateTime SavedAtUtc { get; set; }
        public List<LyricLineDto> Lines { get; set; } = new();
    }

    private sealed class LyricLineDto
    {
        public long TimestampMs { get; set; }
        public string Text { get; set; } = string.Empty;
        public int Index { get; set; }
    }
}
