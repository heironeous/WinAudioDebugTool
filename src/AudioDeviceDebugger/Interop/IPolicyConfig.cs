using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace AudioDeviceDebugger.Interop;

/// <summary>
/// Undocumented COM interface Windows itself uses internally (via audiosrv) to change the default
/// playback/recording/communications endpoint. There is no public Win32 API for this — the Core Audio
/// API only lets you *read* the default endpoint, not set it. This interface (and the
/// "870af99c-171d-4f9e-af0d-e63df40c2bc9" CLSID below) has been stable since Windows Vista and is the
/// same mechanism used by Microsoft's own EarTrumpet and by SoundSwitch/NirCmd.
/// </summary>
[ComImport]
[Guid("f8679f50-850a-41cf-9c72-430f290290c8")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IPolicyConfig
{
    [PreserveSig]
    int GetMixFormat([MarshalAs(UnmanagedType.LPWStr)] string deviceId, IntPtr ppFormat);

    [PreserveSig]
    int GetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string deviceId, bool bDefault, IntPtr ppFormat);

    [PreserveSig]
    int ResetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string deviceId);

    [PreserveSig]
    int SetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string deviceId, IntPtr pEndpointFormat, IntPtr pMixFormat);

    [PreserveSig]
    int GetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string deviceId, bool bDefault, IntPtr pmftDefaultPeriod, IntPtr pmftMinimumPeriod);

    [PreserveSig]
    int SetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string deviceId, IntPtr pmftPeriod);

    [PreserveSig]
    int GetShareMode([MarshalAs(UnmanagedType.LPWStr)] string deviceId, IntPtr pMode);

    [PreserveSig]
    int SetShareMode([MarshalAs(UnmanagedType.LPWStr)] string deviceId, IntPtr mode);

    [PreserveSig]
    int GetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string deviceId, bool bFxStore, ref PropertyKey key, IntPtr pv);

    [PreserveSig]
    int SetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string deviceId, bool bFxStore, ref PropertyKey key, IntPtr pv);

    [PreserveSig]
    int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string deviceId, NAudio.CoreAudioApi.Role role);

    [PreserveSig]
    int SetEndpointVisibility([MarshalAs(UnmanagedType.LPWStr)] string deviceId, bool bVisible);
}

/// <summary>Activates the undocumented CPolicyConfigClient COM object and exposes the calls we need.</summary>
internal static class PolicyConfigClient
{
    private static readonly Guid ClsidPolicyConfigClient = new("870af99c-171d-4f9e-af0d-e63df40c2bc9");

    public static void SetDefaultEndpoint(string deviceId, NAudio.CoreAudioApi.Role role) =>
        Invoke(policyConfig => policyConfig.SetDefaultEndpoint(deviceId, role));

    /// <summary>Enables or disables an endpoint, like Disable / Enable in the Windows Sound control panel ("Don't
    /// allow" / "Allow" in the Windows 11 Sound settings): a disabled endpoint reports DEVICE_STATE_DISABLED and apps
    /// can't open it, while the driver and the device's other endpoints keep working. Windows' audio service keeps it
    /// per endpoint ID (a flag in the endpoint's MMDevices registry DeviceState), so it survives reboots and
    /// reconnects.</summary>
    public static void SetEndpointVisibility(string deviceId, bool visible) =>
        Invoke(policyConfig => policyConfig.SetEndpointVisibility(deviceId, visible));

    private static void Invoke(Func<IPolicyConfig, int> call)
    {
        var comType = Type.GetTypeFromCLSID(ClsidPolicyConfigClient)
            ?? throw new InvalidOperationException("CPolicyConfigClient COM class is not registered on this system.");

        object comObject = Activator.CreateInstance(comType)
            ?? throw new InvalidOperationException("Failed to activate CPolicyConfigClient.");

        try
        {
            Marshal.ThrowExceptionForHR(call((IPolicyConfig)comObject));
        }
        finally
        {
            Marshal.ReleaseComObject(comObject);
        }
    }
}
