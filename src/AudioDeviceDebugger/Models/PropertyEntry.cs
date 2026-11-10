namespace AudioDeviceDebugger.Models;

/// <summary>One PROPERTYKEY/value pair from a device's property store, for the "show me everything" dump.</summary>
public sealed class PropertyEntry
{
    /// <summary>Readable name (e.g. "Device: Friendly Name") when the key is known, otherwise the raw "{fmtid},pid".</summary>
    public required string Name { get; init; }

    /// <summary>The raw key, "{fmtid},pid" — always shown so it can be looked up / compared with other tools.</summary>
    public required string RawKey { get; init; }

    /// <summary>SDK symbol (e.g. "PKEY_Device_FriendlyName"), or null for keys not in the name table.</summary>
    public string? Symbol { get; init; }

    public bool IsKnown => Symbol is not null;

    public required string Value { get; init; }
}
