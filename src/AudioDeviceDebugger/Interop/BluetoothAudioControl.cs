using System.Runtime.InteropServices;

namespace AudioDeviceDebugger.Interop;

/// <summary>
/// Connects or disconnects a Bluetooth audio device the way the Windows Sound settings' Connect / Disconnect button
/// does: the Bluetooth audio driver's one-shot KS properties <c>KSPROPERTY_ONESHOT_RECONNECT</c> /
/// <c>KSPROPERTY_ONESHOT_DISCONNECT</c> (property set <c>KSPROPSETID_BtAudio</c>, ksmedia.h), sent with
/// <c>IKsControl</c> to the audio device (KS filter) behind the endpoint.
/// </summary>
/// <remarks>
/// The filter is reached through the endpoint's device topology: endpoint connector → the filter's connector →
/// <c>IPart.GetTopologyObject</c> → <c>IDeviceTopology.GetDeviceId</c> (e.g. <c>{2}.\\?\bthhfenum#…\wave</c>), which
/// the enumerator opens as a device that can be activated as <c>IKsControl</c>. A Classic headset has two filters
/// (A2DP for "Headphones", Hands-Free for the "Headset" output and mic), and each needs its own call.
/// Uses its own COM objects: call it on one (MTA) thread, never with objects from the UI thread (lesson 3).
/// </remarks>
internal static class BluetoothAudioControl
{
    // From the Win32 metadata (Windows.Win32.winmd): KSPROPSETID_BtAudio and the KSPROPERTY_BTAUDIO values.
    private static readonly Guid KsPropSetIdBtAudio = new("7fa06c40-b8f6-4c7e-8556-e8c33a12e54d");
    private const uint KsPropertyOneShotReconnect = 0;
    private const uint KsPropertyOneShotDisconnect = 1;
    private const uint KsPropertyTypeGet = 0x1;
    private const int ClsCtxAll = 0x17;

    // Created by CLSID: NAudio already declares a class for it (lesson 2).
    private static readonly Guid ClsidMMDeviceEnumerator = new("BCDE0395-E52F-467C-8E3D-C4579291692E");

    /// <summary>The device ID of the audio device (KS filter) behind the endpoint; endpoints with the same filter
    /// share one Bluetooth profile connection.</summary>
    public static string GetAudioDeviceId(string endpointId)
    {
        var enumerator = CreateEnumerator();
        object? device = null, topology = null, connector = null, connectedTo = null, filterTopology = null;
        try
        {
            Check(enumerator.GetDevice(endpointId, out var endpoint));
            device = endpoint;
            var iid = typeof(IDeviceTopology).GUID;
            Check(endpoint.Activate(ref iid, ClsCtxAll, IntPtr.Zero, out var unknown));
            try
            {
                topology = Marshal.GetObjectForIUnknown(unknown);
            }
            finally
            {
                Marshal.Release(unknown);
            }
            Check(((IDeviceTopology)topology).GetConnector(0, out var endpointConnector));
            connector = endpointConnector;
            Check(endpointConnector.GetConnectedTo(out var filterConnector));
            connectedTo = filterConnector;
            Check(((IPart)filterConnector).GetTopologyObject(out var filter));
            filterTopology = filter;
            Check(filter.GetDeviceId(out var filterId));
            return filterId;
        }
        finally
        {
            Release(filterTopology, connectedTo, connector, topology, device);
        }
    }

    /// <summary>Asks the Bluetooth audio driver of the given audio device (from <see cref="GetAudioDeviceId"/>) to
    /// connect or disconnect. It returns at once; the endpoints change state when the link is up or down.</summary>
    public static void Send(string audioDeviceId, bool connect)
    {
        var enumerator = CreateEnumerator();
        object? device = null, control = null;
        try
        {
            Check(enumerator.GetDevice(audioDeviceId, out var filterDevice));
            device = filterDevice;
            var iid = typeof(IKsControl).GUID;
            Check(filterDevice.Activate(ref iid, ClsCtxAll, IntPtr.Zero, out var unknown));
            try
            {
                control = Marshal.GetObjectForIUnknown(unknown);
            }
            finally
            {
                Marshal.Release(unknown);
            }
            var property = new KsProperty
            {
                Set = KsPropSetIdBtAudio,
                Id = connect ? KsPropertyOneShotReconnect : KsPropertyOneShotDisconnect,
                Flags = KsPropertyTypeGet,
            };
            Check(((IKsControl)control).KsProperty(ref property, (uint)Marshal.SizeOf<KsProperty>(), IntPtr.Zero, 0, out _));
        }
        finally
        {
            Release(control, device);
        }
    }

    // Never released: the enumerator is a singleton, and creating it by CLSID can hand back the same RCW that
    // NAudio's MMDeviceEnumerator wraps (lesson 15); ReleaseComObject on it would break NAudio's.
    private static IMMDeviceEnumeratorRaw CreateEnumerator() =>
        (IMMDeviceEnumeratorRaw)Activator.CreateInstance(Type.GetTypeFromCLSID(ClsidMMDeviceEnumerator, throwOnError: true)!)!;

    private static void Check(int hr) => Marshal.ThrowExceptionForHR(hr);

    private static void Release(params object?[] objects)
    {
        foreach (var o in objects)
        {
            if (o is not null && Marshal.IsComObject(o)) Marshal.ReleaseComObject(o);
        }
    }

    /// <summary>KSPROPERTY (KSIDENTIFIER): property set, property ID, flags.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct KsProperty
    {
        public Guid Set;
        public uint Id;
        public uint Flags;
    }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumeratorRaw
    {
        [PreserveSig] int EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IntPtr endpoint);
        [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDeviceRaw device);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceRaw
    {
        [PreserveSig] int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, out IntPtr instance);
    }

    [ComImport, Guid("2A07407E-6497-4A18-9787-32F79BD0D98F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDeviceTopology
    {
        [PreserveSig] int GetConnectorCount(out uint count);
        [PreserveSig] int GetConnector(uint index, out IConnector connector);
        [PreserveSig] int GetSubunitCount(out uint count);
        [PreserveSig] int GetSubunit(uint index, out IntPtr subunit);
        [PreserveSig] int GetPartById(uint id, out IntPtr part);
        [PreserveSig] int GetDeviceId([MarshalAs(UnmanagedType.LPWStr)] out string deviceId);
    }

    [ComImport, Guid("9C2C4058-23F5-41DE-877A-DF3AF236A09E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IConnector
    {
        [PreserveSig] int GetType(out int type);
        [PreserveSig] int GetDataFlow(out int flow);
        [PreserveSig] int ConnectTo(IConnector connectTo);
        [PreserveSig] int Disconnect();
        [PreserveSig] int IsConnected(out bool connected);
        [PreserveSig] int GetConnectedTo(out IConnector connectedTo);
    }

    [ComImport, Guid("AE2DE0E4-5BCA-4F2D-AA46-5D13F8FDB3A9"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPart
    {
        [PreserveSig] int GetName(out IntPtr name);
        [PreserveSig] int GetLocalId(out uint id);
        [PreserveSig] int GetGlobalId(out IntPtr id);
        [PreserveSig] int GetPartType(out int type);
        [PreserveSig] int GetSubType(out Guid subType);
        [PreserveSig] int GetControlInterfaceCount(out uint count);
        [PreserveSig] int GetControlInterface(uint index, out IntPtr controlInterface);
        [PreserveSig] int EnumPartsIncoming(out IntPtr parts);
        [PreserveSig] int EnumPartsOutgoing(out IntPtr parts);
        [PreserveSig] int GetTopologyObject(out IDeviceTopology topology);
    }

    [ComImport, Guid("28F54685-06FD-11D2-B27A-00A0C9223196"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IKsControl
    {
        [PreserveSig] int KsProperty(ref KsProperty property, uint propertyLength, IntPtr data, uint dataLength, out uint bytesReturned);
    }
}
