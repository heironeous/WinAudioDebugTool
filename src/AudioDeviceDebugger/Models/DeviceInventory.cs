using System.Text;

namespace AudioDeviceDebugger.Models;

/// <summary>One line of the inventory, e.g. "Driver version" → "10.0.26100.1".</summary>
public sealed record InventoryRow(string Label, string Value, string? ToolTip = null)
{
    /// <summary>A secondary line (e.g. "not connected"), shown dimmed.</summary>
    public bool IsMuted { get; init; }
}

/// <summary>A titled group of rows: Device, Driver, USB, Audio effects.</summary>
public sealed record InventorySection(string Title, IReadOnlyList<InventoryRow> Rows);

/// <summary>What the Driver tab shows for one endpoint: its audio device, driver, USB connection and the audio
/// processing objects (APOs) Windows runs on it.</summary>
public sealed record DeviceInventory(string EndpointName, IReadOnlyList<InventorySection> Sections)
{
    /// <summary>Plain text for the clipboard (and bug reports): one "Label: value" line per row under each title.</summary>
    public string ToText()
    {
        var text = new StringBuilder();
        text.AppendLine(EndpointName);
        foreach (var section in Sections)
        {
            text.AppendLine().AppendLine(section.Title);
            int width = section.Rows.Count == 0 ? 0 : section.Rows.Max(r => r.Label.Length);
            foreach (var row in section.Rows)
            {
                text.Append("  ").Append((row.Label + ":").PadRight(width + 2)).AppendLine(row.Value);
            }
        }
        return text.ToString();
    }
}
