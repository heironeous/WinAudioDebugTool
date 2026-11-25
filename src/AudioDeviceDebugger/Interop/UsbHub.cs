using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace AudioDeviceDebugger.Interop;

/// <summary>The speed a USB device is connected at, as its hub reports it.</summary>
/// <param name="Speed">USB_DEVICE_SPEED: 0 low, 1 full, 2 high, 3 SuperSpeed.</param>
/// <param name="UsbVersion">bcdUSB from the device descriptor, e.g. 0x0200 for USB 2.0.</param>
/// <param name="OperatingAtSuperSpeedPlus">From the V2 query (Windows 10+): running at SuperSpeed+ (10 Gbit/s or more).</param>
/// <param name="SuperSpeedCapable">From the V2 query: the device could run at SuperSpeed or faster.</param>
public readonly record struct UsbConnectionSpeed(int Speed, int UsbVersion, bool OperatingAtSuperSpeedPlus, bool SuperSpeedCapable);

/// <summary>Asks a USB hub about one of its ports (IOCTL_USB_GET_NODE_CONNECTION_INFORMATION_EX and _EX_V2), the
/// way USBView does. Works without admin rights.</summary>
internal static class UsbHub
{
    private static readonly Guid HubInterface = new("f18a0e88-c30c-11d0-8815-00a0c906bed8"); // GUID_DEVINTERFACE_USB_HUB

    private const uint GenericWrite = 0x40000000;
    private const uint FileShareWrite = 2;
    private const uint OpenExisting = 3;
    private const uint IoctlConnectionInfoEx = 0x220448;   // CTL_CODE(FILE_DEVICE_USB, 274, METHOD_BUFFERED, FILE_ANY_ACCESS)
    private const uint IoctlConnectionInfoExV2 = 0x22045C; // function 279

    // USB_NODE_CONNECTION_INFORMATION_EX (packed): ConnectionIndex (4), USB_DEVICE_DESCRIPTOR (18; bcdUSB at +2),
    // CurrentConfigurationValue (1), Speed (1), …
    private const int SpeedOffset = 4 + 18 + 1;
    private const int BcdUsbOffset = 4 + 2;

    /// <summary>The connection speed of the device on <paramref name="port"/> of the hub
    /// <paramref name="hubInstanceId"/>, or null if the hub can't be asked.</summary>
    public static UsbConnectionSpeed? GetSpeed(string hubInstanceId, int port)
    {
        try
        {
            var path = DeviceNode.InterfacePaths(HubInterface, hubInstanceId).FirstOrDefault();
            if (path is null) return null;

            using var hub = CreateFileW(path, GenericWrite, FileShareWrite, IntPtr.Zero, OpenExisting, 0, IntPtr.Zero);
            if (hub.IsInvalid) return null;

            var info = new byte[512];
            BitConverter.GetBytes(port).CopyTo(info, 0);
            if (!DeviceIoControl(hub, IoctlConnectionInfoEx, info, info.Length, info, info.Length, out _, IntPtr.Zero))
            {
                return null;
            }

            // USB_NODE_CONNECTION_INFORMATION_EX_V2: ConnectionIndex, Length, SupportedUsbProtocols (in: what we
            // understand — USB 1.1, 2.0 and 3.0), Flags (out).
            var v2 = new byte[16];
            BitConverter.GetBytes(port).CopyTo(v2, 0);
            BitConverter.GetBytes(16).CopyTo(v2, 4);
            BitConverter.GetBytes(7).CopyTo(v2, 8);
            uint flags = DeviceIoControl(hub, IoctlConnectionInfoExV2, v2, v2.Length, v2, v2.Length, out _, IntPtr.Zero)
                ? BitConverter.ToUInt32(v2, 12)
                : 0;

            return new UsbConnectionSpeed(
                Speed: info[SpeedOffset],
                UsbVersion: BitConverter.ToUInt16(info, BcdUsbOffset),
                OperatingAtSuperSpeedPlus: (flags & 0x4) != 0,
                SuperSpeedCapable: (flags & 0x2) != 0);
        }
        catch
        {
            return null;
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(string name, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(SafeFileHandle device, uint code, byte[] inBuffer, int inSize, byte[] outBuffer, int outSize, out int returned, IntPtr overlapped);
}
