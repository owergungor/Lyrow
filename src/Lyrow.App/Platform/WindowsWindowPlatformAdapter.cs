using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Lyrow.Core.Contracts;

namespace Lyrow.App.Platform;

/// <summary>
/// Hardware Windows Win32 window platform adapter.
/// Uses SetWindowLongPtr with WS_EX_TRANSPARENT and WS_EX_LAYERED to allow seamless mouse click-through
/// on overlay windows without polling or UI interference.
/// </summary>
public sealed class WindowsWindowPlatformAdapter : IWindowPlatformAdapter
{
    public const int GWL_EXSTYLE = -20;
    public const uint WS_EX_LAYERED = 0x00080000;
    public const uint WS_EX_TRANSPARENT = 0x00000020;
    public const uint WS_EX_NOACTIVATE = 0x08000000;
    public const uint WS_EX_TOOLWINDOW = 0x00000080;
    public const uint WS_EX_TOPMOST = 0x00000008;

    public bool IsSupported => OperatingSystem.IsWindows();

    public bool ApplyOverlayWindowStyles(IntPtr windowHandle)
    {
        if (!OperatingSystem.IsWindows() || windowHandle == IntPtr.Zero) return false;
        try
        {
            var exStyle = GetWindowLongPtr(windowHandle, GWL_EXSTYLE);
            long styleVal = exStyle.ToInt64();
            styleVal |= (WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW | WS_EX_TOPMOST);
            var result = SetWindowLongPtr(windowHandle, GWL_EXSTYLE, new IntPtr(styleVal));
            return result != IntPtr.Zero || Marshal.GetLastWin32Error() == 0;
        }
        catch
        {
            return false;
        }
    }

    public bool SetClickThrough(IntPtr windowHandle, bool enabled)
    {
        if (!OperatingSystem.IsWindows() || windowHandle == IntPtr.Zero)
        {
            return false;
        }

        try
        {
            var exStyle = GetWindowLongPtr(windowHandle, GWL_EXSTYLE);
            long styleVal = exStyle.ToInt64();

            if (enabled)
            {
                styleVal |= (WS_EX_LAYERED | WS_EX_TRANSPARENT);
            }
            else
            {
                styleVal &= ~WS_EX_TRANSPARENT;
            }

            var result = SetWindowLongPtr(windowHandle, GWL_EXSTYLE, new IntPtr(styleVal));
            return result != IntPtr.Zero || Marshal.GetLastWin32Error() == 0;
        }
        catch
        {
            return false;
        }
    }

    public bool IsClickThroughEnabled(IntPtr windowHandle)
    {
        if (!OperatingSystem.IsWindows() || windowHandle == IntPtr.Zero)
        {
            return false;
        }

        try
        {
            var exStyle = GetWindowLongPtr(windowHandle, GWL_EXSTYLE);
            long styleVal = exStyle.ToInt64();
            return (styleVal & WS_EX_TRANSPARENT) != 0;
        }
        catch
        {
            return false;
        }
    }

    [SupportedOSPlatform("windows")]
    private static IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex)
    {
        if (IntPtr.Size == 8)
        {
            return GetWindowLongPtr64(hWnd, nIndex);
        }
        return new IntPtr(GetWindowLong32(hWnd, nIndex));
    }

    [SupportedOSPlatform("windows")]
    private static IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong)
    {
        if (IntPtr.Size == 8)
        {
            return SetWindowLongPtr64(hWnd, nIndex, dwNewLong);
        }
        return new IntPtr(SetWindowLong32(hWnd, nIndex, dwNewLong.ToInt32()));
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)]
    private static extern int GetWindowLong32(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
    private static extern int SetWindowLong32(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);
}
