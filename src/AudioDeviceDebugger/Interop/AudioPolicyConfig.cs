using System.Runtime.InteropServices;
using AudioDeviceDebugger.Models;

namespace AudioDeviceDebugger.Interop;

/// <summary>
/// Per-app default devices: the "App volume and device preferences" page of the Windows Sound settings. Windows
/// keeps, per app, an output and an input that replace the system default for that app's streams; an app that plays
/// on "the default device" then plays there. There is no public API for it: this is the undocumented WinRT factory
/// <c>Windows.Media.Internal.AudioPolicyConfig</c>, the one EarTrumpet and SoundSwitch use. No admin rights needed.
/// </summary>
/// <remarks>
/// The interface ID changed in Windows build 21390 (Windows 11); Windows 10 has the older one with, as far as is
/// known, the same method layout — that path is untested. The setting is stored per app (its executable), not per
/// process, so it outlives the process and applies to the app's other processes too. Use an instance only on the
/// thread that created it (lesson 3: COM objects don't cross apartments).
/// </remarks>
internal sealed class AudioPolicyConfig
{
    private const string ClassName = "Windows.Media.Internal.AudioPolicyConfig";

    // The endpoint ID Windows uses here is the device interface path of the MMDevice endpoint:
    // \\?\SWD#MMDEVAPI#{0.0.0.00000000}.{guid}#{KSCATEGORY_RENDER or KSCATEGORY_CAPTURE interface class}
    private const string MmDevApiPrefix = @"\\?\SWD#MMDEVAPI#";
    private const string RenderInterfaceSuffix = "#{e6327cad-dcec-4949-ae8a-991e976a79d2}";
    private const string CaptureInterfaceSuffix = "#{2eef81be-33fa-4800-9670-1cd474972c3f}";

    private const int ERoleConsole = 0, ERoleMultimedia = 1;

    private readonly IAudioPolicyConfigFactory? _factory;
    private readonly IAudioPolicyConfigFactoryDownlevel? _downlevel;

    private AudioPolicyConfig(object factory)
    {
        _factory = factory as IAudioPolicyConfigFactory;
        if (_factory is null) _downlevel = (IAudioPolicyConfigFactoryDownlevel)factory;
    }

    /// <summary>Gets Windows' per-app device factory, or null with the reason if this Windows doesn't have it.</summary>
    public static AudioPolicyConfig? TryCreate(out string? error)
    {
        error = null;
        IntPtr className = IntPtr.Zero;
        try
        {
            Marshal.ThrowExceptionForHR(WindowsCreateString(ClassName, ClassName.Length, out className));
            foreach (var iid in new[] { typeof(IAudioPolicyConfigFactory).GUID, typeof(IAudioPolicyConfigFactoryDownlevel).GUID })
            {
                var id = iid;
                if (RoGetActivationFactory(className, ref id, out IntPtr factory) < 0) continue;
                try
                {
                    return new AudioPolicyConfig(Marshal.GetObjectForIUnknown(factory));
                }
                finally
                {
                    Marshal.Release(factory);
                }
            }
            error = "this version of Windows doesn't offer per-app devices";
            return null;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return null;
        }
        finally
        {
            if (className != IntPtr.Zero) WindowsDeleteString(className);
        }
    }

    /// <summary>The endpoint ID the app (by any of its process IDs) is set to use, or null when it follows the
    /// Windows default.</summary>
    public string? GetAppEndpoint(int processId, AudioDirection direction)
    {
        int hr = _factory is not null
            ? _factory.GetPersistedDefaultAudioEndpoint((uint)processId, Flow(direction), ERoleMultimedia, out IntPtr value)
            : _downlevel!.GetPersistedDefaultAudioEndpoint((uint)processId, Flow(direction), ERoleMultimedia, out value);
        Marshal.ThrowExceptionForHR(hr);
        try
        {
            return FromInterfacePath(ReadHString(value));
        }
        finally
        {
            if (value != IntPtr.Zero) WindowsDeleteString(value);
        }
    }

    /// <summary>Makes the app use this endpoint instead of the Windows default (for the Default role: console and
    /// multimedia, like the Sound settings), or follow the Windows default again when <paramref name="endpointId"/>
    /// is null.</summary>
    public void SetAppEndpoint(int processId, AudioDirection direction, string? endpointId)
    {
        IntPtr value = IntPtr.Zero;
        try
        {
            if (endpointId is not null)
            {
                string path = ToInterfacePath(endpointId, direction);
                Marshal.ThrowExceptionForHR(WindowsCreateString(path, path.Length, out value));
            }
            foreach (int role in new[] { ERoleConsole, ERoleMultimedia })
            {
                int hr = _factory is not null
                    ? _factory.SetPersistedDefaultAudioEndpoint((uint)processId, Flow(direction), role, value)
                    : _downlevel!.SetPersistedDefaultAudioEndpoint((uint)processId, Flow(direction), role, value);
                Marshal.ThrowExceptionForHR(hr);
            }
        }
        finally
        {
            if (value != IntPtr.Zero) WindowsDeleteString(value);
        }
    }

    private static int Flow(AudioDirection direction) => direction == AudioDirection.Output ? 0 : 1; // eRender / eCapture

    internal static string ToInterfacePath(string endpointId, AudioDirection direction) =>
        MmDevApiPrefix + endpointId + (direction == AudioDirection.Output ? RenderInterfaceSuffix : CaptureInterfaceSuffix);

    /// <summary>The endpoint ID inside a device interface path; null for an empty value (no per-app device).</summary>
    internal static string? FromInterfacePath(string? path)
    {
        if (string.IsNullOrEmpty(path)) return null;
        string id = path;
        if (id.StartsWith(MmDevApiPrefix, StringComparison.OrdinalIgnoreCase)) id = id[MmDevApiPrefix.Length..];
        foreach (string suffix in new[] { RenderInterfaceSuffix, CaptureInterfaceSuffix })
        {
            if (id.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) id = id[..^suffix.Length];
        }
        return id;
    }

    private static string? ReadHString(IntPtr hstring)
    {
        if (hstring == IntPtr.Zero) return null;
        IntPtr buffer = WindowsGetStringRawBuffer(hstring, out uint length);
        return buffer == IntPtr.Zero ? null : Marshal.PtrToStringUni(buffer, (int)length);
    }

    [DllImport("combase.dll", CharSet = CharSet.Unicode)]
    private static extern int WindowsCreateString(string sourceString, int length, out IntPtr hstring);

    [DllImport("combase.dll")]
    private static extern int WindowsDeleteString(IntPtr hstring);

    [DllImport("combase.dll")]
    private static extern IntPtr WindowsGetStringRawBuffer(IntPtr hstring, out uint length);

    [DllImport("combase.dll")]
    private static extern int RoGetActivationFactory(IntPtr activatableClassId, ref Guid iid, out IntPtr factory);
}

// The factory is a WinRT (IInspectable) interface. .NET doesn't support InterfaceIsIInspectable any more, so these
// are IUnknown interfaces whose first three slots stand in for IInspectable's methods. Only the last three methods
// are called; the others only keep the vtable slots in place (names as in EarTrumpet's declaration).

[ComImport]
[Guid("ab3d4648-e242-459f-b02f-541c70306324")] // Windows build 21390 and later
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioPolicyConfigFactory
{
    void GetIids();
    void GetRuntimeClassName();
    void GetTrustLevel();
    void add_CtxVolumeChange();
    void remove_CtxVolumeChanged();
    void add_RingerVibrateStateChanged();
    void remove_RingerVibrateStateChange();
    void SetVolumeGroupGainForId();
    void GetVolumeGroupGainForId();
    void GetActiveVolumeGroupForEndpointId();
    void GetVolumeGroupsForEndpoint();
    void GetCurrentVolumeContext();
    void SetVolumeGroupMuteForId();
    void GetVolumeGroupMuteForId();
    void SetRingerVibrateState();
    void GetRingerVibrateState();
    void SetPreferredChatApplication();
    void ResetPreferredChatApplication();
    void GetPreferredChatApplication();
    void GetCurrentChatApplications();
    void add_ChatContextChanged();
    void remove_ChatContextChanged();

    [PreserveSig]
    int SetPersistedDefaultAudioEndpoint(uint processId, int flow, int role, IntPtr deviceId);

    [PreserveSig]
    int GetPersistedDefaultAudioEndpoint(uint processId, int flow, int role, out IntPtr deviceId);

    [PreserveSig]
    int ClearAllPersistedApplicationDefaultEndpoints();
}

[ComImport]
[Guid("2a59116d-6c4f-45e0-a74f-707e3fef9258")] // Windows 10 (before build 21390)
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioPolicyConfigFactoryDownlevel
{
    void GetIids();
    void GetRuntimeClassName();
    void GetTrustLevel();
    void add_CtxVolumeChange();
    void remove_CtxVolumeChanged();
    void add_RingerVibrateStateChanged();
    void remove_RingerVibrateStateChange();
    void SetVolumeGroupGainForId();
    void GetVolumeGroupGainForId();
    void GetActiveVolumeGroupForEndpointId();
    void GetVolumeGroupsForEndpoint();
    void GetCurrentVolumeContext();
    void SetVolumeGroupMuteForId();
    void GetVolumeGroupMuteForId();
    void SetRingerVibrateState();
    void GetRingerVibrateState();
    void SetPreferredChatApplication();
    void ResetPreferredChatApplication();
    void GetPreferredChatApplication();
    void GetCurrentChatApplications();
    void add_ChatContextChanged();
    void remove_ChatContextChanged();

    [PreserveSig]
    int SetPersistedDefaultAudioEndpoint(uint processId, int flow, int role, IntPtr deviceId);

    [PreserveSig]
    int GetPersistedDefaultAudioEndpoint(uint processId, int flow, int role, out IntPtr deviceId);

    [PreserveSig]
    int ClearAllPersistedApplicationDefaultEndpoints();
}
