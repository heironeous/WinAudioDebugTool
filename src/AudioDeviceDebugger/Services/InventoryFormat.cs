using System.Globalization;
using System.Text.RegularExpressions;
using AudioDeviceDebugger.Interop;

namespace AudioDeviceDebugger.Services;

/// <summary>USB IDs from a hardware or instance ID such as <c>USB\VID_046D&amp;PID_085C&amp;REV_0016&amp;MI_02</c>.</summary>
public sealed record UsbIds(string VendorId, string ProductId, string? Revision, int? Interface);

/// <summary>The pure text and parsing helpers of the device inventory (kept apart from the Windows calls so they
/// can be unit-tested).</summary>
public static class InventoryFormat
{
    private static readonly Regex UsbIdPattern = new(
        @"^USB\\VID_(?<vid>[0-9A-F]{4})&PID_(?<pid>[0-9A-F]{4})(?:&REV_(?<rev>[0-9A-F]{4}))?(?:&MI_(?<mi>[0-9A-F]{2}))?",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex EndpointIdPattern = new(
        @"^\{0\.0\.(?<flow>[01])\.00000000\}\.(?<guid>\{[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}\})$",
        RegexOptions.CultureInvariant);

    public static UsbIds? ParseUsbIds(string? id)
    {
        if (id is null) return null;
        var m = UsbIdPattern.Match(id);
        if (!m.Success) return null;
        return new UsbIds(
            m.Groups["vid"].Value.ToUpperInvariant(),
            m.Groups["pid"].Value.ToUpperInvariant(),
            m.Groups["rev"].Success ? m.Groups["rev"].Value.ToUpperInvariant() : null,
            m.Groups["mi"].Success ? int.Parse(m.Groups["mi"].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture) : null);
    }

    /// <summary>A USB device node (not one interface of a composite device): <c>USB\VID_xxxx&amp;PID_xxxx\…</c>.</summary>
    public static bool IsUsbDevice(string instanceId) =>
        ParseUsbIds(instanceId) is { Interface: null } && !instanceId.Contains("&MI_", StringComparison.OrdinalIgnoreCase);

    /// <summary>The MMDevices registry key of an endpoint ("Render\{guid}" / "Capture\{guid}"), or null for an ID
    /// that isn't a Core Audio endpoint ID.</summary>
    public static string? EndpointRegistryKey(string endpointId)
    {
        var m = EndpointIdPattern.Match(endpointId);
        if (!m.Success) return null;
        return (m.Groups["flow"].Value == "0" ? "Render\\" : "Capture\\") + m.Groups["guid"].Value;
    }

    /// <summary>What connects the audio device, from its enumerator (the first part of its instance ID).</summary>
    public static string BusName(string? enumerator) => enumerator?.ToUpperInvariant() switch
    {
        "USB" => "USB",
        "HDAUDIO" => "High Definition Audio (built-in, or HDMI / DisplayPort)",
        "INTELAUDIO" => "Intel Smart Sound Technology (built-in)",
        "BTHENUM" or "BTHHFENUM" or "BTHLEDEVICE" or "BTHLE" => "Bluetooth",
        "PCI" => "PCI",
        "ACPI" => "Built-in (ACPI)",
        "SWD" => "Software device",
        "ROOT" => "Virtual (software, no hardware)",
        null or "" => "Unknown",
        var other => other,
    };

    /// <summary>
    /// How the audio device is connected, from its device-tree chain (instance IDs, nearest first): the first node on
    /// a real bus decides. Software layers in between (APXENUM audio proxies, SWD) are skipped, so a Bluetooth LE
    /// Audio headset reads "Bluetooth" even though its chain ends at a USB Bluetooth adapter.
    /// </summary>
    public static string ConnectionName(IEnumerable<string> chain)
    {
        string? first = null;
        foreach (var id in chain)
        {
            string enumerator = id.Split('\\')[0].ToUpperInvariant();
            first ??= enumerator;
            switch (enumerator)
            {
                case "BTHLE" or "BTHLEDEVICE":
                    return "Bluetooth Low Energy (LE Audio)";
                case "BTHENUM" or "BTHHFENUM" or "BTH":
                    return "Bluetooth";
                case "USB" or "HDAUDIO" or "INTELAUDIO" or "PCI" or "ACPI":
                    return BusName(enumerator);
            }
        }
        return BusName(first);
    }

    /// <summary>"USB 1.1", "USB 2.0", "USB 3.2" from a bcdUSB value (0x0110, 0x0200, 0x0320).</summary>
    public static string UsbVersionText(int bcd) => $"USB {(bcd >> 8) & 0xFF:X}.{(bcd >> 4) & 0xF:X}";

    public static string UsbSpeedText(UsbConnectionSpeed s) => s.Speed switch
    {
        0 => "Low speed (1.5 Mbit/s)",
        1 => "Full speed (12 Mbit/s)",
        2 => "High speed (480 Mbit/s)",
        3 when s.OperatingAtSuperSpeedPlus => "SuperSpeed+ (10 Gbit/s or more)",
        3 => "SuperSpeed (5 Gbit/s)",
        var other => $"Unknown speed ({other})",
    };

    /// <summary>A hint when the speed says something: a faster device held back by its port or cable, or the usual
    /// question whether full speed is enough for audio. Null when there's nothing to add.</summary>
    public static string? UsbSpeedNote(UsbConnectionSpeed s)
    {
        if (s.SuperSpeedCapable && s.Speed < 3)
        {
            return "The device supports SuperSpeed but is connected slower: a USB 2 port, hub or cable is in between.";
        }
        if (s.Speed == 1)
        {
            return "Full speed is normal for headsets and dongles and plenty for audio: a 48 kHz 16-bit stereo stream " +
                   "needs about 1.5 Mbit/s.";
        }
        return null;
    }

    /// <summary>Device Manager's wording for common problem codes (CM_PROB_*).</summary>
    public static string ProblemText(uint? code, bool isPresent)
    {
        if (!isPresent) return "Not connected";
        return code switch
        {
            null or 0 => "Working",
            10 => "Can't start (code 10)",
            22 => "Disabled in Device Manager (code 22)",
            24 => "Not present or not working (code 24)",
            28 => "No driver installed (code 28)",
            31 => "Driver failed to load (code 31)",
            43 => "Stopped by Windows after reporting problems (code 43)",
            45 => "Not connected (code 45)",
            52 => "Driver signature not verified (code 52)",
            var other => $"Problem code {other}",
        };
    }

    /// <summary>Driver dates are midnight UTC of the day in the INF, so the UTC date is the one to show.</summary>
    public static string DateText(DateTime utc) => utc.ToUniversalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>Label of a PKEY_FX_* / PKEY_CompositeFX_* slot (fmtid d04e05a6…): which kind of effect runs there.
    /// Null for the keys that aren't effect slots.</summary>
    public static string? FxSlotLabel(uint propertyId) => propertyId switch
    {
        1 => "Pre-mix effect (LFX)",
        2 => "Post-mix effect (GFX)",
        3 => "Settings page (UI)",
        5 or 13 => "Stream effect (SFX)",
        6 or 14 => "Mode effect (MFX)",
        7 or 15 => "Endpoint effect (EFX)",
        8 or 16 => "Keyword detector stream effect",
        9 or 17 => "Keyword detector mode effect",
        10 or 18 => "Keyword detector endpoint effect",
        11 or 19 => "Offload stream effect",
        12 or 20 => "Offload mode effect",
        _ => null,
    };

    /// <summary>Label of a PKEY_xFX_ProcessingModes_Supported_For_Streaming value (fmtid d3993a3f…).</summary>
    public static string? ProcessingModesLabel(uint propertyId) => propertyId switch
    {
        5 => "Stream effect modes",
        6 => "Mode effect modes",
        7 => "Endpoint effect modes",
        _ => null,
    };

    /// <summary>"Default, Speech" for a list of AUDIO_SIGNALPROCESSINGMODE_* GUIDs.</summary>
    public static string ModesText(IEnumerable<Guid> modes) =>
        string.Join(", ", modes.Select(m => KnownGuidNames.Describe(m) ?? m.ToString("B")));

    /// <summary>GUIDs in a registry value: a REG_SZ with one, or a REG_MULTI_SZ with several. Empty GUIDs and
    /// anything that isn't a GUID are left out.</summary>
    public static IReadOnlyList<Guid> ParseGuids(object? value)
    {
        IEnumerable<string> texts = value switch
        {
            string s => new[] { s },
            string[] list => list,
            _ => Array.Empty<string>(),
        };
        return texts.Select(t => Guid.TryParse(t.Trim(), out var g) ? g : Guid.Empty).Where(g => g != Guid.Empty).ToList();
    }
}
