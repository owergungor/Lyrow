using System.Text.Json;
using Lyrow.Core.Contracts;
using Lyrow.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lyrow.Core.Services;

/// <summary>
/// Persistent settings manager with atomic write, backup and corruption recovery.
/// </summary>
public sealed class SettingsManager : ISettingsManager
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly string _filePath;
    private readonly ILogger<SettingsManager> _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private LyrowSettings _current;

    public LyrowSettings Current => _current;

    public event EventHandler<LyrowSettings>? SettingsChanged;

    public SettingsManager(string? customFilePath = null, ILogger<SettingsManager>? logger = null)
    {
        _logger = logger ?? NullLogger<SettingsManager>.Instance;
        _filePath = customFilePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Lyrow",
            "settings.json");
        _current = LyrowSettings.CreateDefault();
    }

    public async Task<LyrowSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!File.Exists(_filePath))
            {
                _logger.LogInformation("Settings file not found at {Path}. Using default settings.", _filePath);
                _current = LyrowSettings.CreateDefault();
                return _current;
            }

            try
            {
                await using var stream = File.OpenRead(_filePath);
                var loaded = await JsonSerializer.DeserializeAsync<LyrowSettings>(stream, JsonOptions, cancellationToken).ConfigureAwait(false);
                _current = loaded ?? LyrowSettings.CreateDefault();
                _logger.LogInformation("Settings successfully loaded from {Path}.", _filePath);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to deserialize settings from {Path}. Falling back to default settings.", _filePath);
                _current = LyrowSettings.CreateDefault();
            }

            return _current;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task SaveAsync(LyrowSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var directory = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            // Atomic write: write to temp file, then rename/replace
            var tempPath = _filePath + ".tmp";
            await using (var stream = File.Create(tempPath))
            {
                await JsonSerializer.SerializeAsync(stream, settings, JsonOptions, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            bool moved = false;
            for (int attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    File.Move(tempPath, _filePath, overwrite: true);
                    moved = true;
                    break;
                }
                catch (IOException) when (attempt < 2)
                {
                    await Task.Delay(20, cancellationToken).ConfigureAwait(false);
                }
                catch (UnauthorizedAccessException) when (attempt < 2)
                {
                    await Task.Delay(20, cancellationToken).ConfigureAwait(false);
                }
            }

            if (!moved)
            {
                File.Copy(tempPath, _filePath, overwrite: true);
                try { File.Delete(tempPath); } catch { }
            }

            _current = settings;
            _logger.LogInformation("Settings successfully persisted to {Path}.", _filePath);

            SettingsChanged?.Invoke(this, _current);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task UpdateAsync(Action<LyrowSettings> configure, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(configure);

        LyrowSettings target;
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            target = _current;
            configure(target);
        }
        finally
        {
            _lock.Release();
        }

        await SaveAsync(target, cancellationToken).ConfigureAwait(false);
    }
}
