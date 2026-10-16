using Lyrow.Core.Contracts;

namespace Lyrow.App.Platform;

/// <summary>
/// Synthetic mock window platform adapter for unit tests and non-Windows environments.
/// </summary>
public sealed class SyntheticWindowPlatformAdapter : IWindowPlatformAdapter
{
    private readonly Dictionary<IntPtr, bool> _states = new();

    public bool IsSupported => true;

    public bool SetClickThrough(IntPtr windowHandle, bool enabled)
    {
        if (windowHandle == IntPtr.Zero) return false;
        _states[windowHandle] = enabled;
        return true;
    }

    public bool IsClickThroughEnabled(IntPtr windowHandle)
    {
        if (windowHandle == IntPtr.Zero) return false;
        return _states.TryGetValue(windowHandle, out var enabled) && enabled;
    }
}
