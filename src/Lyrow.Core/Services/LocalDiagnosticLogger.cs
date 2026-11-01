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

    public static string LogFilePath => _logFilePath;

    public static string Sanitize(string message)
    {
        if (string.IsNullOrEmpty(message)) return string.Empty;

        // Mask user profile names in absolute Windows file paths
        return System.Text.RegularExpressions.Regex.Replace(
            message,
            @"(?i)(Users\\)[^\\]+(\\)",
            "$1***$2");
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
            lock (_fileLock)
            {
                if (!Directory.Exists(_logDirectory))
                {
                    Directory.CreateDirectory(_logDirectory);
                }

                // Simple log file rotation if exceeding 5 MB
                var fileInfo = new FileInfo(_logFilePath);
                if (fileInfo.Exists && fileInfo.Length > 5 * 1024 * 1024)
                {
                    string oldPath = Path.Combine(_logDirectory, "app.old.log");
                    if (File.Exists(oldPath)) File.Delete(oldPath);
                    File.Move(_logFilePath, oldPath);
                }

                string line = $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff UTC}] [{level}] {sanitized}{Environment.NewLine}";
                File.AppendAllText(_logFilePath, line, Encoding.UTF8);
            }
        }
        catch
        {
            // Silent fail to guarantee no application crashes caused by diagnostic logging
        }
    }
}
