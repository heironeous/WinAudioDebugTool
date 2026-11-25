using System.Diagnostics;
using System.IO;
using AudioDeviceDebugger.Interop;
using AudioDeviceDebugger.Models;
using Microsoft.Win32;

namespace AudioDeviceDebugger.Services;

public interface IDeviceInventoryService
{
    /// <summary>Reads the endpoint's device, driver, USB connection and audio effects. Slow-ish (tens of ms; it
    /// walks the device tree and the registry), so call it off the UI thread. Never throws.</summary>
    DeviceInventory GetInventory(string endpointId, string endpointName);
}

/// <summary>
/// The "Driver" tab: what is behind an endpoint. Sources, all readable without admin rights:
/// <list type="bullet">
/// <item>the device tree (cfgmgr32): the endpoint's node <c>SWD\MMDEVAPI\{id}</c>, its parent (the audio device and
/// its driver) and the USB device further up;</item>
/// <item>the USB hub the device is plugged into, for the connection speed;</item>
/// <item>the endpoint's <c>FxProperties</c> registry key, for the audio processing objects (APOs) Windows runs on it,
/// named from the APO registration, the COM class, or the driver package that installed it.</item>
/// </list>
/// </summary>
public sealed class DeviceInventoryService : IDeviceInventoryService
{
    private const string MmDevicesKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\MMDevices\Audio\";
    private const string ApoRegistrationsKey = @"SOFTWARE\Classes\AudioEngine\AudioProcessingObjects\";

    private static readonly Guid FxFormatId = new("d04e05a6-594b-4fb6-a80d-01af5eed7d1d");
    private static readonly Guid ProcessingModesFormatId = new("d3993a3f-99c2-4402-b5ec-a92a0367664b");
    private static readonly (Guid FormatId, uint PropertyId) DisableSysFx = (new Guid("1da5d803-d492-4edd-8c23-e0c0ffee7f0e"), 5);
    private const uint ApoPackagePropertyId = 100; // "{APO CLSID},100" = instance ID of the driver package's software device

    public DeviceInventory GetInventory(string endpointId, string endpointName)
    {
        var sections = new List<InventorySection>();
        try
        {
            var endpoint = DeviceNode.Find(@"SWD\MMDEVAPI\" + endpointId);
            var device = endpoint?.Parent();
            if (device is null)
            {
                sections.Add(new InventorySection("Device", new[]
                {
                    new InventoryRow("Device", "Windows has no device entry for this endpoint any more") { IsMuted = true },
                }));
            }
            else
            {
                sections.Add(DeviceSection(device));
                sections.Add(DriverSection(device));
                if (UsbSection(device) is { } usb) sections.Add(usb);
            }
        }
        catch (Exception ex)
        {
            sections.Add(new InventorySection("Device", new[] { new InventoryRow("Error", ex.Message) }));
        }

        try
        {
            sections.Add(EffectsSection(endpointId));
        }
        catch (Exception ex)
        {
            sections.Add(new InventorySection("Audio effects (APOs)", new[] { new InventoryRow("Error", ex.Message) }));
        }
        return new DeviceInventory(endpointName, sections);
    }

    // ---- device and driver -------------------------------------------------------------------------------------

    private static InventorySection DeviceSection(DeviceNode device)
    {
        var rows = new List<InventoryRow>();
        Add(rows, "Name", device.GetString(DeviceNode.Keys.FriendlyName) ?? device.GetString(DeviceNode.Keys.DeviceDesc));
        Add(rows, "Manufacturer", device.GetString(DeviceNode.Keys.Manufacturer));
        rows.Add(new InventoryRow("Connection", InventoryFormat.ConnectionName(device.SelfAndAncestors().Select(n => n.InstanceId))));
        rows.Add(new InventoryRow("Status", InventoryFormat.ProblemText(device.GetUInt32(DeviceNode.Keys.ProblemCode), device.IsPresent))
        {
            IsMuted = !device.IsPresent,
        });
        var hardwareIds = device.GetStringList(DeviceNode.Keys.HardwareIds);
        if (hardwareIds.Count > 0)
        {
            rows.Add(new InventoryRow("Hardware ID", hardwareIds[0], string.Join("\n", hardwareIds)));
        }
        rows.Add(new InventoryRow("Instance ID", device.InstanceId, "The device's ID in Device Manager (Details → Device instance path)"));
        return new InventorySection("Device", rows);
    }

    private static InventorySection DriverSection(DeviceNode device)
    {
        var rows = new List<InventoryRow>();
        Add(rows, "Driver", device.GetString(DeviceNode.Keys.DriverDesc));
        Add(rows, "Provider", device.GetString(DeviceNode.Keys.DriverProvider));
        Add(rows, "Version", device.GetString(DeviceNode.Keys.DriverVersion));
        if (device.GetFileTime(DeviceNode.Keys.DriverDate) is { } date)
        {
            rows.Add(new InventoryRow("Date", InventoryFormat.DateText(date)));
        }
        if (device.GetString(DeviceNode.Keys.DriverInfPath) is { } inf)
        {
            string section = device.GetString(DeviceNode.Keys.DriverInfSection) is { } s ? $" (section {s})" : "";
            rows.Add(new InventoryRow("INF file", inf + section, @"In C:\Windows\INF"));
        }
        Add(rows, "Service", device.GetString(DeviceNode.Keys.Service));
        if (device.GetBool(DeviceNode.Keys.GenericDriverInstalled) is { } generic)
        {
            rows.Add(new InventoryRow("Kind", generic ? "Generic class driver that comes with Windows" : "Driver for this device",
                generic ? "No vendor driver is installed; Windows' own class driver runs the device." : null));
        }
        if (rows.Count == 0) rows.Add(new InventoryRow("Driver", "None installed") { IsMuted = true });
        return new InventorySection("Driver", rows);
    }

    // ---- USB ------------------------------------------------------------------------------------------------------

    private static InventorySection? UsbSection(DeviceNode device)
    {
        var chain = device.SelfAndAncestors().ToList();
        // Only for USB audio devices: a Bluetooth headset's chain also ends at a (USB) Bluetooth adapter.
        if (InventoryFormat.ConnectionName(chain.Select(n => n.InstanceId)) != "USB") return null;
        var usbFunction = chain.FirstOrDefault(n => InventoryFormat.ParseUsbIds(n.InstanceId) is not null);
        if (usbFunction is null) return null;
        var usbDevice = chain.FirstOrDefault(n => InventoryFormat.IsUsbDevice(n.InstanceId)) ?? usbFunction;

        var rows = new List<InventoryRow>();
        Add(rows, "Product", usbDevice.GetString(DeviceNode.Keys.BusReportedDeviceDesc));

        var hardwareIds = usbFunction.GetStringList(DeviceNode.Keys.HardwareIds);
        var ids = hardwareIds.Select(InventoryFormat.ParseUsbIds).FirstOrDefault(i => i?.Revision is not null)
                  ?? InventoryFormat.ParseUsbIds(usbFunction.InstanceId)!;
        rows.Add(new InventoryRow("Vendor / product ID", $"{ids.VendorId} / {ids.ProductId}",
            "USB vendor ID (VID) and product ID (PID), as in the hardware ID"));
        Add(rows, "Revision", ids.Revision);
        if (ids.Interface is { } mi)
        {
            rows.Add(new InventoryRow("Interface", $"Audio is interface {mi} of a composite device",
                "The device has other functions too (for example a camera, buttons or a firmware-update interface), each with its own driver."));
        }

        var hub = usbDevice.Parent();
        uint? port = usbDevice.GetUInt32(DeviceNode.Keys.Address);
        if (!usbDevice.IsPresent)
        {
            rows.Add(new InventoryRow("Speed", "Not connected") { IsMuted = true });
        }
        else if (hub is not null && port is { } p && UsbHub.GetSpeed(hub.InstanceId, (int)p) is { } speed)
        {
            string? note = InventoryFormat.UsbSpeedNote(speed);
            rows.Add(new InventoryRow("Speed", InventoryFormat.UsbSpeedText(speed), note));
            if (speed.SuperSpeedCapable && speed.Speed < 3) rows.Add(new InventoryRow("Note", note!));
            rows.Add(new InventoryRow("USB version", InventoryFormat.UsbVersionText(speed.UsbVersion),
                "The USB version the device declares (bcdUSB in its device descriptor)"));
        }
        if (hub is not null && port is { } portNumber)
        {
            string hubName = hub.GetString(DeviceNode.Keys.FriendlyName) ?? hub.GetString(DeviceNode.Keys.DeviceDesc) ?? hub.InstanceId;
            rows.Add(new InventoryRow("Port", $"Port {portNumber} of {hubName}", hub.InstanceId));
        }
        return new InventorySection("USB", rows);
    }

    // ---- audio effects (APOs) -------------------------------------------------------------------------------------

    private static InventorySection EffectsSection(string endpointId)
    {
        const string title = "Audio effects (APOs)";
        var rows = new List<InventoryRow>();
        using var fx = InventoryFormat.EndpointRegistryKey(endpointId) is { } key
            ? Registry.LocalMachine.OpenSubKey(MmDevicesKey + key + @"\FxProperties")
            : null;
        if (fx is null)
        {
            rows.Add(new InventoryRow("Effects", "None registered for this endpoint") { IsMuted = true });
            return new InventorySection(title, rows);
        }

        var values = fx.GetValueNames()
            .Select(name => (Key: ParseValueName(name), Name: name))
            .Where(v => v.Key is not null)
            .Select(v => (Key: v.Key!.Value, Value: fx.GetValue(v.Name)))
            .ToList();

        // Driver packages that installed APOs: "{APO CLSID},100" → the package's software device.
        var packages = values.Where(v => v.Key.PropertyId == ApoPackagePropertyId && v.Value is string)
            .GroupBy(v => v.Key.FormatId)
            .ToDictionary(g => g.Key, g => (string)g.First().Value!);

        var listed = new HashSet<Guid>();
        foreach (var (k, value) in values.Where(v => v.Key.FormatId == FxFormatId).OrderBy(v => SlotOrder(v.Key.PropertyId)))
        {
            if (InventoryFormat.FxSlotLabel(k.PropertyId) is not { } label) continue;
            foreach (var clsid in InventoryFormat.ParseGuids(value))
            {
                listed.Add(clsid);
                rows.Add(ApoRow(label, clsid, packages));
            }
        }

        // APOs a driver package registered for this endpoint outside the slots above (Windows 11 effect packs,
        // e.g. Voice Clarity).
        foreach (var clsid in packages.Keys.Where(c => !listed.Contains(c)))
        {
            rows.Add(ApoRow("Effect pack", clsid, packages));
        }

        foreach (var (k, value) in values.Where(v => v.Key.FormatId == ProcessingModesFormatId).OrderBy(v => v.Key.PropertyId))
        {
            if (InventoryFormat.ProcessingModesLabel(k.PropertyId) is { } label && InventoryFormat.ParseGuids(value) is { Count: > 0 } modes)
            {
                rows.Add(new InventoryRow(label, InventoryFormat.ModesText(modes),
                    "Signal-processing modes the effects in this slot support (apps pick one per stream)"));
            }
        }

        if (values.FirstOrDefault(v => v.Key == DisableSysFx).Value is int disabled)
        {
            rows.Add(new InventoryRow("Enhancements", disabled != 0 ? "Off (switched off for this device)" : "On",
                "Windows Sound settings → the device → \"Audio enhancements\". Off bypasses the vendor's effects."));
        }

        if (rows.Count == 0) rows.Add(new InventoryRow("Effects", "None registered for this endpoint") { IsMuted = true });
        return new InventorySection(title, rows);
    }

    /// <summary>SFX, MFX, EFX (and their composite lists) first, then the legacy and special slots.</summary>
    private static int SlotOrder(uint propertyId) => propertyId switch
    {
        5 or 13 => 0,
        6 or 14 => 1,
        7 or 15 => 2,
        1 => 3,
        2 => 4,
        3 => 9,
        _ => 5,
    };

    private static InventoryRow ApoRow(string label, Guid clsid, IReadOnlyDictionary<Guid, string> packages)
    {
        string braced = clsid.ToString("B").ToUpperInvariant();
        var tip = new List<string> { braced };
        string? name = null;

        using (var apo = Registry.LocalMachine.OpenSubKey(ApoRegistrationsKey + braced))
        {
            name = apo?.GetValue("FriendlyName") as string;
            if (apo?.GetValue("Copyright") is string copyright) tip.Add(copyright);
        }
        using (var com = Registry.ClassesRoot.OpenSubKey(@"CLSID\" + braced))
        {
            name ??= com?.GetValue(null) as string;
            using var server = com?.OpenSubKey("InprocServer32");
            if (server?.GetValue(null) is string dll) tip.Add(FileText(dll));
        }

        string? packageName = null;
        if (packages.TryGetValue(clsid, out var packageId) && DeviceNode.Find(packageId) is { } package)
        {
            packageName = package.GetString(DeviceNode.Keys.DeviceDesc);
            var installedBy = new[]
            {
                packageName, package.GetString(DeviceNode.Keys.DriverProvider), package.GetString(DeviceNode.Keys.DriverVersion),
                package.GetFileTime(DeviceNode.Keys.DriverDate) is { } d ? InventoryFormat.DateText(d) : null,
            }.Where(t => !string.IsNullOrEmpty(t));
            tip.Add("Installed by: " + string.Join(", ", installedBy));
        }

        string value = name is not null && packageName is not null ? $"{name} ({packageName})" : name ?? packageName ?? "Unknown effect";
        return new InventoryRow(label, value, string.Join("\n", tip));
    }

    private static string FileText(string dll)
    {
        string path = Environment.ExpandEnvironmentVariables(dll);
        if (!Path.IsPathRooted(path)) path = Path.Combine(Environment.SystemDirectory, path);
        try
        {
            return File.Exists(path) && FileVersionInfo.GetVersionInfo(path).FileVersion is { Length: > 0 } version
                ? $"{path} (version {version})"
                : path;
        }
        catch
        {
            return path;
        }
    }

    /// <summary>FxProperties value names are property keys written as "{fmtid},pid".</summary>
    private static (Guid FormatId, uint PropertyId)? ParseValueName(string name)
    {
        int comma = name.LastIndexOf(',');
        return comma > 0 && Guid.TryParse(name[..comma], out var formatId) && uint.TryParse(name[(comma + 1)..], out var pid)
            ? (formatId, pid)
            : null;
    }

    private static void Add(List<InventoryRow> rows, string label, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value)) rows.Add(new InventoryRow(label, value));
    }
}
