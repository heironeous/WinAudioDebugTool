using System.Runtime.InteropServices;

namespace AudioDeviceDebugger.Interop;

// Raw WASAPI interop for capture streams that ask for a particular processing mode. NAudio 2.2.1 declares
// AudioClientProperties but its AudioClient has no SetClientProperties, so a stream's category (e.g. Communications)
// or the RAW option can't be set through it. IIDs, method order and enum values were checked against
// Windows.Win32.winmd (Microsoft.Windows.SDK.Win32Metadata).

/// <summary>AUDIO_STREAM_CATEGORY (the values used here).</summary>
internal enum AudioStreamCategory
{
    Other = 0,
    Communications = 3,
}

/// <summary>AUDCLNT_STREAMOPTIONS.</summary>
[Flags]
internal enum AudioClientStreamOptionFlags
{
    None = 0,
    Raw = 1,
}

/// <summary>AudioClientProperties (Windows 8.1+ layout: cbSize, bIsOffload, eCategory, Options).</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct AudioClientPropertiesNative
{
    public int Size;
    public int IsOffload;
    public AudioStreamCategory Category;
    public AudioClientStreamOptionFlags Options;
}

[ComImport, Guid("726778CD-F60A-4EDA-82DE-E47610CD78AA"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioClient2Native
{
    // IAudioClient
    [PreserveSig] int Initialize(int shareMode, int streamFlags, long bufferDuration, long periodicity, IntPtr format, IntPtr audioSessionGuid);
    [PreserveSig] int GetBufferSize(out uint frames);
    [PreserveSig] int GetStreamLatency(out long latency);
    [PreserveSig] int GetCurrentPadding(out uint padding);
    [PreserveSig] int IsFormatSupported(int shareMode, IntPtr format, out IntPtr closestMatch);
    [PreserveSig] int GetMixFormat(out IntPtr format);
    [PreserveSig] int GetDevicePeriod(out long defaultPeriod, out long minimumPeriod);
    [PreserveSig] int Start();
    [PreserveSig] int Stop();
    [PreserveSig] int Reset();
    [PreserveSig] int SetEventHandle(IntPtr eventHandle);
    [PreserveSig] int GetService(ref Guid iid, [MarshalAs(UnmanagedType.IUnknown)] out object service);

    // IAudioClient2
    [PreserveSig] int IsOffloadCapable(AudioStreamCategory category, out int offloadCapable);
    [PreserveSig] int SetClientProperties(ref AudioClientPropertiesNative properties);
    [PreserveSig] int GetBufferSizeLimits(IntPtr format, int eventDriven, out long minDuration, out long maxDuration);
}

[ComImport, Guid("C8ADBD64-E71E-48A0-A4DE-185C395CD317"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioCaptureClientNative
{
    [PreserveSig] int GetBuffer(out IntPtr data, out uint frames, out uint flags, out ulong devicePosition, out ulong qpcPosition);
    [PreserveSig] int ReleaseBuffer(uint frames);
    [PreserveSig] int GetNextPacketSize(out uint frames);
}

/// <summary>IAudioEffectsManager (Windows 11): lists the effects the audio engine applies to one stream.</summary>
[ComImport, Guid("4460B3AE-4B44-4527-8676-7548A8ACD260"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioEffectsManagerNative
{
    [PreserveSig] int RegisterAudioEffectsChangedNotificationCallback(IntPtr client);
    [PreserveSig] int UnregisterAudioEffectsChangedNotificationCallback(IntPtr client);
    [PreserveSig] int GetAudioEffects(out IntPtr effects, out uint count);
    [PreserveSig] int SetAudioEffectState(Guid effectId, int state);
}

/// <summary>AUDIO_EFFECT.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct AudioEffectNative
{
    public Guid Id;
    public int CanSetState;
    public int State; // AUDIO_EFFECT_STATE: 0 off, 1 on
}

/// <summary>Activates an <see cref="IAudioClient2Native"/> on an endpoint by device ID.</summary>
internal static class AudioClient2Activator
{
    private const int ClsctxAll = 23;

    // Created by CLSID, not via a [ComImport] class — see lesson 2 in CLAUDE.md / EndpointPropertyReader.
    private static readonly Guid ClsidMMDeviceEnumerator = new("BCDE0395-E52F-467C-8E3D-C4579291692E");
    private static readonly Guid IidAudioClient2 = typeof(IAudioClient2Native).GUID;

    public static IAudioClient2Native Activate(string deviceId)
    {
        var enumerator = (IMMDeviceEnumeratorForActivation)Activator.CreateInstance(
            Type.GetTypeFromCLSID(ClsidMMDeviceEnumerator, throwOnError: true)!)!;
        IMMDeviceForActivation? device = null;
        try
        {
            Marshal.ThrowExceptionForHR(enumerator.GetDevice(deviceId, out device));
            var iid = IidAudioClient2;
            Marshal.ThrowExceptionForHR(device.Activate(ref iid, ClsctxAll, IntPtr.Zero, out var instance));
            try
            {
                return (IAudioClient2Native)Marshal.GetObjectForIUnknown(instance);
            }
            finally
            {
                Marshal.Release(instance);
            }
        }
        finally
        {
            if (device is not null) Marshal.ReleaseComObject(device);
            Marshal.ReleaseComObject(enumerator);
        }
    }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumeratorForActivation
    {
        [PreserveSig] int EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IntPtr endpoint);
        [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDeviceForActivation device);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceForActivation
    {
        [PreserveSig] int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, out IntPtr instance);
    }
}

internal static class AudioPropertyKeys
{
    /// <summary>PKEY_Devices_AudioDevice_RawProcessingSupported (VT_BOOL).</summary>
    public static readonly NAudio.CoreAudioApi.PropertyKey RawProcessingSupported =
        new(new Guid("8943b373-388c-4395-b557-bc6dbaffafdb"), 2);
}

/// <summary>Names for the AUDIO_EFFECT_TYPE_* GUIDs of ksmedia.h (unknown ones are shown as their GUID).</summary>
internal static class AudioEffectNames
{
    private static readonly Dictionary<Guid, string> Names = new()
    {
        [new("6f64adbe-8211-11e2-8c70-2c27d7f001fa")] = "Echo cancellation",
        [new("6f64adbf-8211-11e2-8c70-2c27d7f001fa")] = "Noise suppression",
        [new("6f64adc0-8211-11e2-8c70-2c27d7f001fa")] = "Automatic gain control",
        [new("6f64adc1-8211-11e2-8c70-2c27d7f001fa")] = "Beamforming",
        [new("6f64adc2-8211-11e2-8c70-2c27d7f001fa")] = "Constant tone removal",
        [new("6f64adc3-8211-11e2-8c70-2c27d7f001fa")] = "Equalizer",
        [new("6f64adc4-8211-11e2-8c70-2c27d7f001fa")] = "Loudness equalizer",
        [new("6f64adc5-8211-11e2-8c70-2c27d7f001fa")] = "Bass boost",
        [new("6f64adc6-8211-11e2-8c70-2c27d7f001fa")] = "Virtual surround",
        [new("6f64adc7-8211-11e2-8c70-2c27d7f001fa")] = "Virtual headphones",
        [new("6f64adc8-8211-11e2-8c70-2c27d7f001fa")] = "Speaker fill",
        [new("6f64adc9-8211-11e2-8c70-2c27d7f001fa")] = "Room correction",
        [new("6f64adca-8211-11e2-8c70-2c27d7f001fa")] = "Bass management",
        [new("6f64adcb-8211-11e2-8c70-2c27d7f001fa")] = "Environmental effects",
        [new("6f64adcc-8211-11e2-8c70-2c27d7f001fa")] = "Speaker protection",
        [new("6f64adcd-8211-11e2-8c70-2c27d7f001fa")] = "Speaker compensation",
        [new("6f64adce-8211-11e2-8c70-2c27d7f001fa")] = "Dynamic range compression",
    };

    public static string Describe(Guid id) => Names.TryGetValue(id, out var name) ? name : id.ToString("B");
}
