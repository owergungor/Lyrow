using Lyrow.Core.Contracts;

namespace Lyrow.App.Platform;

/// <summary>
/// Synthetic mock startup manager for unit tests and non-Windows environments.
/// </summary>
public sealed class SyntheticStartupManager : IWindowsStartupManager
{
    private bool _enabled;

    public bool IsSupported => true;

    public bool IsStartupEnabled() => _enabled;

    public bool SetStartupEnabled(bool enabled)
    {
        _enabled = enabled;
        return true;
    }
}
