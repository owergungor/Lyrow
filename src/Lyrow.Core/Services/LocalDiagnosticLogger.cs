using System.Text;

namespace Lyrow.Core.Services;

/// <summary>
/// Safe, zero-telemetry, local diagnostic logger for Lyrow.
/// Writes timestamped log entries strictly to %APPDATA%\Lyrow\logs\app.log.
/// </summary>
public static class LocalDiagnosticLogger
{
    private static readonly object _fileLock = new();
    private static readonly string _logDirectory;
    private static readonly string _logFilePath;
    private static string? _customLogFilePath;

    private static string? _lastEntryText;
    private static string? _lastEntryLevel;
    private static DateTime _lastEntryTime = DateTime.MinValue;
    private static int _duplicateCount = 0;

    static LocalDiagnosticLogger()
    {
        string baseDir = AppDomain.CurrentDomain.BaseDirectory;
        bool isPortable = File.Exists(Path.Combine(baseDir, "portable")) ||
                          File.Exists(Path.Combine(baseDir, ".portable")) ||
                          File.Exists(Path.Combine(baseDir, "settings.json"));

        if (isPortable)
        {
            _logDirectory = Path.Combine(baseDir, "logs");
        }
        else
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            _logDirectory = Path.Combine(appData, "Lyrow", "logs");
        }
        _logFilePath = Path.Combine(_logDirectory, "app.log");

        try
        {
            if (!Directory.Exists(_logDirectory))
            {
                Directory.CreateDirectory(_logDirectory);
            }
        }
        catch
        {
            // Suppress directory creation issues on constrained environments
        }
    }

    public static string LogFilePath
    {
        get
        {
            lock (_fileLock)
            {
                return _customLogFilePath ?? _logFilePath;
            }
        }
    }

    public static void SetCustomLogFilePathForTesting(string? path)
    {
        lock (_fileLock)
        {
            _customLogFilePath = path;
            ResetForTesting();
        }
    }

    public static string Sanitize(string message)
    {
        if (string.IsNullOrEmpty(message)) return string.Empty;

        // Mask user profile names in absolute Windows file paths
        var sanitized = System.Text.RegularExpressions.Regex.Replace(
            message,
            @"(?i)(Users\\)[^\\]+(\\)",
            "$1***$2");

        // Mask Bearer tokens
        sanitized = System.Text.RegularExpressions.Regex.Replace(
            sanitized,
            @"(?i)(Bearer\s+)[A-Za-z0-9\-._~+/]+=*",
            "$1[REDACTED]");

        // Mask sensitive key-value pairs (passwords, tokens, api keys, secrets)
        sanitized = System.Text.RegularExpressions.Regex.Replace(
            sanitized,
            @"(?i)(token|password|secret|apikey|client_secret|access_token|refresh_token)\s*([:=])\s*[""']?([^\s,""'&]+)[""']?",
            "$1$2[REDACTED]");

        return sanitized;
    }

    public static void Flush()
    {
        try
        {
            lock (_fileLock)
            {
                string targetPath = _customLogFilePath ?? _logFilePath;
                string targetDir = Path.GetDirectoryName(targetPath) ?? _logDirectory;

                if (_duplicateCount > 0 && !string.IsNullOrEmpty(targetPath) && Directory.Exists(targetDir))
                {
                    string dupNotice = $"[{_lastEntryTime:yyyy-MM-dd HH:mm:ss.fff UTC}] [{_lastEntryLevel}] [Suppressed {_duplicateCount} duplicate log entries]{Environment.NewLine}";
                    File.AppendAllText(targetPath, dupNotice, Encoding.UTF8);
                    _duplicateCount = 0;
                }
            }
        }
        catch { }
    }

    public static void ResetForTesting()
    {
        lock (_fileLock)
        {
            _lastEntryText = null;
            _lastEntryLevel = null;
            _lastEntryTime = DateTime.MinValue;
            _duplicateCount = 0;
        }
    }

    public static void LogInfo(string message) => WriteEntry("INFO", message);
    public static void LogWarning(string message) => WriteEntry("WARN", message);
    public static void LogError(string message, Exception? ex = null)
    {
        var sb = new StringBuilder(message);
        if (ex != null)
        {
            sb.AppendLine();
            sb.AppendLine($"Exception: {ex.GetType().FullName}: {ex.Message}");
            sb.AppendLine($"StackTrace: {ex.StackTrace}");
            if (ex.InnerException != null)
            {
                sb.AppendLine($"InnerException: {ex.InnerException.GetType().FullName}: {ex.InnerException.Message}");
            }
        }
        WriteEntry("ERROR", sb.ToString());
    }

    private static void WriteEntry(string level, string text)
    {
        try
        {
            string sanitized = Sanitize(text);
            DateTime now = DateTime.UtcNow;

            lock (_fileLock)
            {
                string targetPath = _customLogFilePath ?? _logFilePath;
                string targetDir = Path.GetDirectoryName(targetPath) ?? _logDirectory;

                // Rate-limiting consecutive duplicate entries within 1 second
                if (level == _lastEntryLevel &&
                    sanitized == _lastEntryText &&
                    (now - _lastEntryTime).TotalMilliseconds < 1000)
                {
                    _duplicateCount++;
                    _lastEntryTime = now;
                    return;
                }

                if (!Directory.Exists(targetDir))
                {
                    Directory.CreateDirectory(targetDir);
                }

                if (_duplicateCount > 0)
                {
                    string dupNotice = $"[{_lastEntryTime:yyyy-MM-dd HH:mm:ss.fff UTC}] [{_lastEntryLevel}] [Suppressed {_duplicateCount} duplicate log entries]{Environment.NewLine}";
                    File.AppendAllText(targetPath, dupNotice, Encoding.UTF8);
                    _duplicateCount = 0;
                }

                _lastEntryLevel = level;
                _lastEntryText = sanitized;
                _lastEntryTime = now;

                // Simple log file rotation if exceeding 5 MB
                var fileInfo = new FileInfo(targetPath);
                if (fileInfo.Exists && fileInfo.Length > 5 * 1024 * 1024)
                {
                    string oldPath = Path.Combine(targetDir, "app.old.log");
                    if (File.Exists(oldPath)) File.Delete(oldPath);
                    File.Move(targetPath, oldPath);
                }

                string line = $"[{now:yyyy-MM-dd HH:mm:ss.fff UTC}] [{level}] {sanitized}{Environment.NewLine}";
                File.AppendAllText(targetPath, line, Encoding.UTF8);
            }
        }
        catch
        {
            // Silent fail to guarantee no application crashes caused by diagnostic logging
        }
    }
}
