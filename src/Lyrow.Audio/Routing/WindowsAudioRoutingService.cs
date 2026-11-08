using System;
using System.Runtime.InteropServices;
using Lyrow.Core.Contracts;

namespace Lyrow.Audio.Routing;

/// <summary>
/// COM interop definitions and service implementation for routing the real Windows default audio output endpoint.
/// Utilizes the Windows undocumented CPolicyConfigClient / IPolicyConfig interface.
/// </summary>
public sealed class WindowsAudioRoutingService : IAudioRoutingService
{
    public bool IsSupported => OperatingSystem.IsWindows();

    public bool SetDefaultPlaybackDevice(string deviceId)
    {
        if (!OperatingSystem.IsWindows() || string.IsNullOrWhiteSpace(deviceId))
        {
            return false;
        }

        try
        {
            var policy = (IPolicyConfig)new CPolicyConfigClient();
            if (policy == null) return false;

            // Apply deviceId to all standard Windows roles
            int hrConsole = policy.SetDefaultEndpoint(deviceId, ERole.eConsole);
            int hrMulti = policy.SetDefaultEndpoint(deviceId, ERole.eMultimedia);
            int hrComm = policy.SetDefaultEndpoint(deviceId, ERole.eCommunications);

            return hrConsole == 0 && hrMulti == 0;
        }
        catch
        {
            return false;
        }
    }

    [ComImport]
    [Guid("870af99c-171d-4f9e-af0d-e63df40c2bc9")]
    private class CPolicyConfigClient
    {
    }

    private enum ERole
    {
        eConsole = 0,
        eMultimedia = 1,
        eCommunications = 2
    }

    [ComImport]
    [Guid("f8679f50-850a-41cf-9c72-430f290290c8")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPolicyConfig
    {
        [PreserveSig] int GetMixFormat(string pszDeviceName, IntPtr ppWaveFormat);
        [PreserveSig] int GetDeviceFormat(string pszDeviceName, bool bEnsureCollection, IntPtr ppWaveFormat);
        [PreserveSig] int ResetDeviceFormat(string pszDeviceName);
        [PreserveSig] int SetDeviceFormat(string pszDeviceName, IntPtr pWaveFormat, IntPtr pMixFormat);
        [PreserveSig] int GetProcessingPeriod(string pszDeviceName, bool bEnsureCollection, out IntPtr pmnsDuration, out IntPtr pmnsPeriod);
        [PreserveSig] int SetProcessingPeriod(string pszDeviceName, IntPtr pmnsDuration, IntPtr pmnsPeriod);
        [PreserveSig] int GetShareMode(string pszDeviceName, IntPtr pDeviceMode);
        [PreserveSig] int SetShareMode(string pszDeviceName, IntPtr pDeviceMode);
        [PreserveSig] int GetPropertyValue(string pszDeviceName, bool bEnsureCollection, IntPtr pKey, IntPtr pPropVariant);
        [PreserveSig] int SetPropertyValue(string pszDeviceName, bool bEnsureCollection, IntPtr pKey, IntPtr pPropVariant);
        [PreserveSig] int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string pszDeviceName, ERole eRole);
        [PreserveSig] int SetEndpointVisibility(string pszDeviceName, bool bVisible);
    }
}
