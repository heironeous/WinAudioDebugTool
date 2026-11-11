using System.Text.RegularExpressions;

namespace AudioDeviceDebugger.Interop;

/// <summary>
/// Maps PROPERTYKEYs (fmtid + pid) to names. The bulk of the table — every documented PKEY_* / DEVPKEY_* in the
/// Windows SDK — lives in the generated <c>PropertyKeyNames.g.cs</c> (see tools/PropertyKeyNameGenerator); keys not
/// in the SDK can be added to <see cref="ManualNames"/> below.
/// </summary>
internal static partial class PropertyKeyNames
{
    /// <summary>
    /// Keys missing from the SDK metadata (e.g. undocumented MMDevAPI / driver keys). Only add an entry when its
    /// meaning is confirmed — an unknown key is shown by its raw {fmtid},pid, which is better than a wrong name.
    /// Entries here take precedence over the generated table. Use the PKEY_Area_PropertyName naming convention so
    /// <see cref="ToDisplayName"/> renders it like the SDK ones.
    /// </summary>
    private static readonly Dictionary<(Guid FormatId, int PropertyId), string> ManualNames = new()
    {
        // Undocumented MMDevAPI endpoint keys, as named by NAudio's PropertyKeys (values observed on real endpoints
        // match: "{1}.USB\VID_…" device instance path / "{2}.\\?\usb#…" device interface path).
        [(new Guid("b3f8fa53-0004-438e-9003-51a46e139bfc"), 2)] = "PKEY_Device_ControllerDeviceId",
        [(new Guid("233164c8-1b2c-4c7d-bc68-b671687a2567"), 1)] = "PKEY_Device_InterfaceKey",
    };

    /// <summary>
    /// Undocumented keys whose meaning was confirmed by comparing their values with what Windows itself reports
    /// (PnP device properties via Get-PnpDeviceProperty) across all 29 endpoints of the development machine
    /// (2026-11-11). They have no SDK symbol, so they get a display name plus a note on how it was confirmed — shown
    /// as the name's tooltip — instead of an invented PKEY_ symbol. Keys that couldn't be confirmed stay raw.
    /// </summary>
    private static readonly Dictionary<(Guid FormatId, int PropertyId), UndocumentedKey> Undocumented = new()
    {
        [(new Guid("b3f8fa53-0004-438e-9003-51a46e139bfc"), 6)] = new("Audio Device: Name",
            "Name of the audio device (adapter) this endpoint belongs to, as Device Manager shows it. Recorded when the "
            + "endpoint was last registered, so it can be out of date after a driver change."),
        [(new Guid("9c119480-ddc2-4954-a150-5bd240d454ad"), 1)] = new("Endpoint: Device Interface Path",
            @"The endpoint's device interface path: \\?\SWD#MMDEVAPI#<endpoint ID>#<audio render/capture interface class>."),
        [(new Guid("9c119480-ddc2-4954-a150-5bd240d454ad"), 9)] = new("Endpoint: Device Interface Path",
            @"The endpoint's device interface path (\\?\SWD#MMDEVAPI#<endpoint ID>#<interface class>) — the same "
            + "value as pid 1, kept under a second key."),
        [(new Guid("9c119480-ddc2-4954-a150-5bd240d454ad"), 2)] = new("Endpoint: Device Instance ID",
            @"The endpoint's own PnP device instance ID (SWD\MMDEVAPI\<endpoint ID>), as used by Device Manager."),
        [(new Guid("9c119480-ddc2-4954-a150-5bd240d454ad"), 10)] = new("Endpoint: Device Instance ID",
            @"The endpoint's own PnP device instance ID (SWD\MMDEVAPI\<endpoint ID>) — the same value as pid 2, kept "
            + "under a second key."),
        [(new Guid("80f111c3-b103-42e1-afb6-db7a6fa8be1f"), 0)] = new("Audio Device: Hardware ID",
            "The first hardware ID of the audio device (USB devices: vendor, product, revision and interface)."),
        [(new Guid("83da6326-97a6-4088-9453-a1923f573b29"), 3)] = new("Audio Device: Driver",
            "The audio device's driver: INF file, a hash, install section, driver version and the hardware ID it was "
            + "matched on. Recorded when the endpoint was last registered — an older version than the installed "
            + "driver means the endpoint predates the driver update."),
    };

    /// <summary>The display name and note for a confirmed undocumented key, or null.</summary>
    public static UndocumentedKey? TryGetUndocumented(Guid formatId, int propertyId) =>
        Undocumented.TryGetValue((formatId, propertyId), out var key) ? key : null;

    /// <summary>The SDK-style symbol for the key (e.g. "PKEY_Device_FriendlyName"), or null if unknown.</summary>
    public static string? TryGetSymbol(Guid formatId, int propertyId) =>
        ManualNames.TryGetValue((formatId, propertyId), out var manual) ? manual
        : SdkNames.TryGetValue((formatId, propertyId), out var sdk) ? sdk
        : null;

    /// <summary>The raw key as Windows prints it: "{fmtid},pid".</summary>
    public static string FormatRaw(Guid formatId, int propertyId) => $"{{{formatId}}},{propertyId}";

    private static readonly Regex WordBoundary = new(
        "(?<=[a-z0-9])(?=[A-Z])|(?<=[A-Z])(?=[A-Z][a-z])", RegexOptions.Compiled);

    /// <summary>
    /// Turns an SDK symbol into a readable label: "PKEY_AudioEndpoint_FormFactor" → "Audio Endpoint: Form Factor",
    /// "DEVPKEY_Device_ContainerId" → "Device: Container ID".
    /// </summary>
    public static string ToDisplayName(string symbol)
    {
        string trimmed = symbol.StartsWith("DEVPKEY_", StringComparison.Ordinal) ? symbol["DEVPKEY_".Length..]
            : symbol.StartsWith("PKEY_", StringComparison.Ordinal) ? symbol["PKEY_".Length..]
            : symbol;

        var parts = trimmed.Split('_', StringSplitOptions.RemoveEmptyEntries).Select(Humanize).ToArray();
        return parts.Length switch
        {
            0 => symbol,
            1 => parts[0],
            _ => $"{parts[0]}: {string.Join(" ", parts[1..])}",
        };

        static string Humanize(string part) => string.Join(" ",
            WordBoundary.Split(part).Select(word => word == "Id" ? "ID" : word));
    }
}

/// <summary>A readable name for an undocumented property key, and how its meaning was confirmed.</summary>
internal sealed record UndocumentedKey(string DisplayName, string Note);
