using System.Runtime.Versioning;
using Lyrow.Core.Contracts;
using Microsoft.Win32;

namespace Lyrow.App.Platform;

/// <summary>
/// Hardware Windows startup manager writing to HKCU\Software\Microsoft\Windows\CurrentVersion\Run.
/// </summary>
public sealed class WindowsStartupManager : IWindowsStartupManager
{
    private const string RunRegistryKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "Lyrow";

    public bool IsSupported => OperatingSystem.IsWindows();

    [SupportedOSPlatform("windows")]
    public bool IsStartupEnabled()
    {
        if (!OperatingSystem.IsWindows()) return false;

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunRegistryKeyPath, writable: false);
            var val = key?.GetValue(AppName);
            return val != null;
        }
        catch
        {
            return false;
        }
    }

    [SupportedOSPlatform("windows")]
    public bool SetStartupEnabled(bool enabled)
    {
        if (!OperatingSystem.IsWindows()) return false;

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunRegistryKeyPath, writable: true);
            if (key == null) return false;

            if (enabled)
            {
                var processPath = Environment.ProcessPath;
                if (string.IsNullOrWhiteSpace(processPath)) return false;

                key.SetValue(AppName, $"\"{processPath}\"");
            }
            else
            {
                if (key.GetValue(AppName) != null)
                {
                    key.DeleteValue(AppName, throwOnMissingValue: false);
                }
            }

            return true;
        }
        catch
        {
            return false;
        }
    }
}
