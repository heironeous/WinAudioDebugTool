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
