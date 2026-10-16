namespace Lyrow.Core.Contracts;

/// <summary>
/// Platform adapter interface for native operating system window interactions (e.g. click-through).
/// </summary>
public interface IWindowPlatformAdapter
{
    /// <summary>Whether native window styling features are supported on the current OS.</summary>
    bool IsSupported { get; }

    /// <summary>
    /// Enables or disables click-through (mouse hit-test transparency) for a native window handle.
    /// </summary>
    bool SetClickThrough(IntPtr windowHandle, bool enabled);

    /// <summary>
    /// Checks whether click-through is currently applied to a native window handle.
    /// </summary>
    bool IsClickThroughEnabled(IntPtr windowHandle);
}
