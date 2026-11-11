namespace AudioDeviceDebugger.Models;

/// <summary>One PROPERTYKEY/value pair from a device's property store, for the "show me everything" dump.</summary>
public sealed class PropertyEntry
{
    /// <summary>Readable name (e.g. "Device: Friendly Name") when the key is known, otherwise the raw "{fmtid},pid".</summary>
    public required string Name { get; init; }

    /// <summary>The raw key, "{fmtid},pid" — always shown so it can be looked up / compared with other tools.</summary>
    public required string RawKey { get; init; }

    /// <summary>SDK symbol (e.g. "PKEY_Device_FriendlyName"), or null for keys not in the SDK name table.</summary>
    public string? Symbol { get; init; }

    /// <summary>For an undocumented key with a confirmed meaning: what it holds and how that was confirmed.</summary>
    public string? Note { get; init; }

    public bool IsKnown => Symbol is not null || Note is not null;

    /// <summary>Tooltip for the name: the SDK symbol, the note for an undocumented key, or why it's unnamed.</summary>
    public string NameToolTip => Symbol ?? (Note is null ? "Not in the property-key name table" : $"Undocumented key. {Note}");

    /// <summary>Second part of the Key column: the SDK symbol, or "undocumented" for a named undocumented key.</summary>
    public string KeyLabel => Symbol ?? (Note is null ? "" : "undocumented");

    /// <summary>The value as shown: decoded into readable text where possible, otherwise the raw value.</summary>
    public required string Value { get; init; }

    /// <summary>The raw value (blobs as hex), as the property store returns it.</summary>
    public required string RawValue { get; init; }

    /// <summary>Extra lines about a decoded value (what kind of data it is, details), or null.</summary>
    public string? ValueDetail { get; init; }

    public bool IsDecoded => Value != RawValue;

    /// <summary>Tooltip for the value: details plus the raw value when it was decoded, else the value itself.</summary>
    public string ValueToolTip => IsDecoded
        ? (ValueDetail is null ? "" : ValueDetail + "\n\n") + "Raw value: " + RawValue
        : RawValue;
}
