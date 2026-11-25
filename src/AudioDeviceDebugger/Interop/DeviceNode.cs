using System.Runtime.InteropServices;
using System.Text;

namespace AudioDeviceDebugger.Interop;

/// <summary>
/// A Plug and Play device node (what Device Manager shows), read with the Configuration Manager API (cfgmgr32).
/// Every audio endpoint has one (<c>SWD\MMDEVAPI\{endpoint ID}</c>); its parent is the audio device whose driver
/// created the endpoint, and further up are the bus devices (USB device, hub, PCI controller…).
/// </summary>
/// <remarks>Read-only and free-threaded; no admin rights needed. Nodes of devices that aren't connected (phantoms)
/// still carry their last properties.</remarks>
internal sealed class DeviceNode
{
    private const int CrSuccess = 0;
    private const int CrBufferSmall = 0x1A;
    private const uint LocateNormal = 0;
    private const uint LocatePhantom = 1;

    private const uint DevpropTypeGuid = 0x0D;
    private const uint DevpropTypeFiletime = 0x10;
    private const uint DevpropTypeBoolean = 0x11;
    private const uint DevpropTypeString = 0x12;
    private const uint DevpropTypeUint32 = 0x07;
    private const uint DevpropTypeStringList = 0x2012;

    private readonly uint _devInst;

    private DeviceNode(uint devInst, string instanceId, bool isPresent)
    {
        _devInst = devInst;
        InstanceId = instanceId;
        IsPresent = isPresent;
    }

    public string InstanceId { get; }

    /// <summary>False for a phantom: a device that was installed once but isn't connected now.</summary>
    public bool IsPresent { get; }

    /// <summary>The node with this instance ID (present or not), or null if Windows doesn't know it.</summary>
    public static DeviceNode? Find(string instanceId)
    {
        if (CM_Locate_DevNodeW(out uint devInst, instanceId, LocateNormal) == CrSuccess)
        {
            return new DeviceNode(devInst, instanceId, isPresent: true);
        }
        return CM_Locate_DevNodeW(out devInst, instanceId, LocatePhantom) == CrSuccess
            ? new DeviceNode(devInst, instanceId, isPresent: false)
            : null;
    }

    public DeviceNode? Parent() => GetString(Keys.Parent) is { Length: > 0 } parent ? Find(parent) : null;

    /// <summary>This node and its ancestors, nearest first (at most <paramref name="max"/>).</summary>
    public IEnumerable<DeviceNode> SelfAndAncestors(int max = 12)
    {
        var node = this;
        for (int i = 0; i < max && node is not null; i++)
        {
            yield return node;
            node = node.Parent();
        }
    }

    public string? GetString(DevPropKey key) =>
        Read(key, DevpropTypeString, bytes => Encoding.Unicode.GetString(bytes).TrimEnd('\0'));

    public IReadOnlyList<string> GetStringList(DevPropKey key) =>
        Read(key, DevpropTypeStringList, bytes =>
            (IReadOnlyList<string>)Encoding.Unicode.GetString(bytes).Split('\0', StringSplitOptions.RemoveEmptyEntries))
        ?? Array.Empty<string>();

    public uint? GetUInt32(DevPropKey key) =>
        Read<uint?>(key, DevpropTypeUint32, bytes => bytes.Length >= 4 ? BitConverter.ToUInt32(bytes) : null);

    public bool? GetBool(DevPropKey key) =>
        Read<bool?>(key, DevpropTypeBoolean, bytes => bytes.Length >= 1 ? bytes[0] != 0 : null);

    public DateTime? GetFileTime(DevPropKey key) =>
        Read<DateTime?>(key, DevpropTypeFiletime, bytes => bytes.Length >= 8 ? DateTime.FromFileTimeUtc(BitConverter.ToInt64(bytes)) : null);

    public Guid? GetGuid(DevPropKey key) =>
        Read<Guid?>(key, DevpropTypeGuid, bytes => bytes.Length >= 16 ? new Guid(bytes.AsSpan(0, 16)) : null);

    private T? Read<T>(DevPropKey key, uint expectedType, Func<byte[], T?> convert)
    {
        try
        {
            uint size = 0;
            int cr = CM_Get_DevNode_PropertyW(_devInst, ref key, out uint type, null, ref size, 0);
            if (cr != CrBufferSmall || size == 0) return default;
            var buffer = new byte[size];
            cr = CM_Get_DevNode_PropertyW(_devInst, ref key, out type, buffer, ref size, 0);
            return cr == CrSuccess && type == expectedType ? convert(buffer) : default;
        }
        catch
        {
            return default;
        }
    }

    /// <summary>Device interface paths of one class registered by a device, e.g. a USB hub's hub interface.</summary>
    public static IReadOnlyList<string> InterfacePaths(Guid interfaceClass, string instanceId)
    {
        if (CM_Get_Device_Interface_List_SizeW(out uint length, ref interfaceClass, instanceId, 0) != CrSuccess || length <= 1)
        {
            return Array.Empty<string>();
        }
        var buffer = new char[length];
        if (CM_Get_Device_Interface_ListW(ref interfaceClass, instanceId, buffer, length, 0) != CrSuccess)
        {
            return Array.Empty<string>();
        }
        return new string(buffer).Split('\0', StringSplitOptions.RemoveEmptyEntries);
    }

    /// <summary>DEVPROPKEY: the device-property equivalent of a property-store key.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct DevPropKey
    {
        public Guid FormatId;
        public uint PropertyId;

        public DevPropKey(string formatId, uint propertyId)
        {
            FormatId = new Guid(formatId);
            PropertyId = propertyId;
        }
    }

    /// <summary>The DEVPKEY_Device_* keys the inventory reads (devpkey.h).</summary>
    public static class Keys
    {
        private const string General = "a45c254e-df1c-4efd-8020-67d146a850e0";
        private const string Driver = "a8b865dd-2e3d-4094-ad97-e593a70c75d6";

        public static readonly DevPropKey DeviceDesc = new(General, 2);
        public static readonly DevPropKey HardwareIds = new(General, 3);
        public static readonly DevPropKey Service = new(General, 6);
        public static readonly DevPropKey Manufacturer = new(General, 13);
        public static readonly DevPropKey FriendlyName = new(General, 14);
        public static readonly DevPropKey EnumeratorName = new(General, 24);
        public static readonly DevPropKey Address = new(General, 30);
        public static readonly DevPropKey ProblemCode = new("4340a6c5-93fa-4706-972c-7b648008a5a7", 3);
        public static readonly DevPropKey Parent = new("4340a6c5-93fa-4706-972c-7b648008a5a7", 8);
        public static readonly DevPropKey BusReportedDeviceDesc = new("540b947e-8b40-45bc-a8a2-6a0b894cbda2", 4);
        public static readonly DevPropKey DriverDate = new(Driver, 2);
        public static readonly DevPropKey DriverVersion = new(Driver, 3);
        public static readonly DevPropKey DriverDesc = new(Driver, 4);
        public static readonly DevPropKey DriverInfPath = new(Driver, 5);
        public static readonly DevPropKey DriverInfSection = new(Driver, 6);
        public static readonly DevPropKey DriverProvider = new(Driver, 9);
        public static readonly DevPropKey GenericDriverInstalled = new(Driver, 18);
    }

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    private static extern int CM_Locate_DevNodeW(out uint devInst, string deviceId, uint flags);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    private static extern int CM_Get_DevNode_PropertyW(uint devInst, ref DevPropKey key, out uint type, byte[]? buffer, ref uint size, uint flags);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    private static extern int CM_Get_Device_Interface_List_SizeW(out uint length, ref Guid interfaceClass, string? deviceId, uint flags);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    private static extern int CM_Get_Device_Interface_ListW(ref Guid interfaceClass, string? deviceId, char[] buffer, uint length, uint flags);
}
