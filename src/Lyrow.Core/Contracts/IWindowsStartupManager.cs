namespace Lyrow.Core.Contracts;

/// <summary>
/// Abstraction for managing Windows automatic application startup on user sign-in.
/// </summary>
public interface IWindowsStartupManager
{
    /// <summary>Indicates whether the current platform supports automatic startup configuration.</summary>
    bool IsSupported { get; }

    /// <summary>Checks whether auto-start on Windows login is currently enabled.</summary>
    bool IsStartupEnabled();

    /// <summary>Enables or disables auto-start on Windows login.</summary>
    bool SetStartupEnabled(bool enabled);
}
