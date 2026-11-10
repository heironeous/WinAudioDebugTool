namespace AudioDeviceDebugger.Models;

/// <summary>One raw PROPERTYKEY/value pair from a device's property store, for the "show me everything" dump.</summary>
public sealed class PropertyEntry
{
    public required string Key { get; init; }
    public required string Value { get; init; }
}
