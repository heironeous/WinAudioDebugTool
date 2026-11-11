using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;

namespace AudioDeviceDebugger.Interop;

/// <summary>
/// Reads single endpoint properties with a direct <c>IPropertyStore::GetValue(key)</c>.
/// </summary>
/// <remarks>
/// NAudio's <c>MMDevice.FriendlyName</c> (and <c>PropertyStore[key]</c>) find a key by walking every entry with
/// <c>GetAt</c> — and FriendlyName walks it twice (Contains + indexer). Endpoint stores hold ~150 entries and each
/// <c>GetAt</c> costs ~0.5 ms, so one name took up to ~450 ms and enumerating ~30 endpoints took ~4 s. A direct
/// keyed lookup is ~0–1 ms. Not thread-safe; create one per thread.
/// </remarks>
internal sealed class EndpointPropertyReader : IDisposable
{
    private const int StgmRead = 0;
    private const ushort VtLpwstr = 31;
    private const ushort VtUi4 = 19;
    private const ushort VtBool = 11;

    // Created by CLSID rather than via a [ComImport] class: NAudio already declares a class for this CLSID, and
    // the runtime hands back an RCW of *that* type, which can't be cast to a second ComImport class.
    private static readonly Guid ClsidMMDeviceEnumerator = new("BCDE0395-E52F-467C-8E3D-C4579291692E");

    private IMMDeviceEnumeratorRaw? _enumerator =
        (IMMDeviceEnumeratorRaw)Activator.CreateInstance(Type.GetTypeFromCLSID(ClsidMMDeviceEnumerator, throwOnError: true)!)!;

    public string? TryGetString(string deviceId, PropertyKey key) =>
        Read(deviceId, key, value => value.vt == VtLpwstr ? Marshal.PtrToStringUni(value.pointerValue) : null);

    public uint? TryGetUInt32(string deviceId, PropertyKey key) =>
        Read<uint?>(deviceId, key, value => value.vt == VtUi4 ? (uint)(value.pointerValue.ToInt64() & 0xFFFFFFFF) : null);

    /// <summary>VT_BOOL: a VARIANT_BOOL (0 or -1) in the low 16 bits of the value.</summary>
    public bool? TryGetBool(string deviceId, PropertyKey key) =>
        Read<bool?>(deviceId, key, value => value.vt == VtBool ? (value.pointerValue.ToInt64() & 0xFFFF) != 0 : null);

    private T? Read<T>(string deviceId, PropertyKey key, Func<RawPropVariant, T?> convert)
    {
        if (_enumerator is null) return default;

        IMMDeviceRaw? device = null;
        IPropertyStoreRaw? store = null;
        try
        {
            if (_enumerator.GetDevice(deviceId, out device) != 0 || device is null) return default;
            if (device.OpenPropertyStore(StgmRead, out store) != 0 || store is null) return default;
            if (store.GetValue(ref key, out var value) != 0) return default;
            try
            {
                return convert(value);
            }
            finally
            {
                PropVariantClear(ref value);
            }
        }
        catch
        {
            return default;
        }
        finally
        {
            if (store is not null) Marshal.ReleaseComObject(store);
            if (device is not null) Marshal.ReleaseComObject(device);
        }
    }

    public void Dispose()
    {
        if (_enumerator is null) return;
        Marshal.ReleaseComObject(_enumerator);
        _enumerator = null;
    }

    [DllImport("ole32.dll")]
    private static extern int PropVariantClear(ref RawPropVariant pvar);

    /// <summary>PROPVARIANT layout (24 bytes on x64); only the pointer member is read.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct RawPropVariant
    {
        public ushort vt;
        public ushort reserved1;
        public ushort reserved2;
        public ushort reserved3;
        public IntPtr pointerValue;
        public IntPtr padding;
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
        [PreserveSig] int OpenPropertyStore(int access, out IPropertyStoreRaw store);
    }

    [ComImport, Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStoreRaw
    {
        [PreserveSig] int GetCount(out int count);
        [PreserveSig] int GetAt(int index, out PropertyKey key);
        [PreserveSig] int GetValue(ref PropertyKey key, out RawPropVariant value);
    }
}
