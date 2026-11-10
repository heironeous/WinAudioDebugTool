using System.Runtime.InteropServices;
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
    int GetMixFormat(string deviceId, IntPtr ppFormat);

    [PreserveSig]
    int GetDeviceFormat(string deviceId, bool bDefault, IntPtr ppFormat);

    [PreserveSig]
    int ResetDeviceFormat(string deviceId);

    [PreserveSig]
    int SetDeviceFormat(string deviceId, IntPtr pEndpointFormat, IntPtr pMixFormat);

    [PreserveSig]
    int GetProcessingPeriod(string deviceId, bool bDefault, IntPtr pmftDefaultPeriod, IntPtr pmftMinimumPeriod);

    [PreserveSig]
    int SetProcessingPeriod(string deviceId, IntPtr pmftPeriod);

    [PreserveSig]
    int GetShareMode(string deviceId, IntPtr pMode);

    [PreserveSig]
    int SetShareMode(string deviceId, IntPtr mode);

    [PreserveSig]
    int GetPropertyValue(string deviceId, bool bFxStore, ref PropertyKey key, IntPtr pv);

    [PreserveSig]
    int SetPropertyValue(string deviceId, bool bFxStore, ref PropertyKey key, IntPtr pv);

    [PreserveSig]
    int SetDefaultEndpoint(string deviceId, NAudio.CoreAudioApi.Role role);

    [PreserveSig]
    int SetEndpointVisibility(string deviceId, bool bVisible);
}

/// <summary>Activates the undocumented CPolicyConfigClient COM object and exposes the one call we need.</summary>
internal static class PolicyConfigClient
{
    private static readonly Guid ClsidPolicyConfigClient = new("870af99c-171d-4f9e-af0d-e63df40c2bc9");

    public static void SetDefaultEndpoint(string deviceId, NAudio.CoreAudioApi.Role role)
    {
        var comType = Type.GetTypeFromCLSID(ClsidPolicyConfigClient)
            ?? throw new InvalidOperationException("CPolicyConfigClient COM class is not registered on this system.");

        object comObject = Activator.CreateInstance(comType)
            ?? throw new InvalidOperationException("Failed to activate CPolicyConfigClient.");

        try
        {
            var policyConfig = (IPolicyConfig)comObject;
            int hr = policyConfig.SetDefaultEndpoint(deviceId, role);
            Marshal.ThrowExceptionForHR(hr);
        }
        finally
        {
            Marshal.ReleaseComObject(comObject);
        }
    }
}
