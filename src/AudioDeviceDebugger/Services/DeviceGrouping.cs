using System.Text.RegularExpressions;
using AudioDeviceDebugger.Models;

namespace AudioDeviceDebugger.Services;

/// <summary>A group in the device list: its stable key, heading, sort position and heading tooltip.</summary>
public sealed record DeviceGroupInfo(string Key, string Title, int Order, string? ToolTip = null);

/// <summary>
/// The two ways the device list is grouped: by direction (OUTPUTS / INPUTS) or by physical device. A physical device
/// is a PnP container (<c>DEVPKEY_Device_ContainerId</c>): a headset's speaker and mic endpoints share one, and every
/// built-in device shares Windows' "this PC" container.
/// </summary>
public static class DeviceGrouping
{
    /// <summary>The container Windows gives every built-in device ("this PC").</summary>
    public static readonly Guid ComputerContainerId = new("00000000-0000-0000-ffff-ffffffffffff");

    private const int FormFactorDigitalDisplay = 9;

    // Windows numbers a second device with the same name: "2- Jabra Evolve3 75".
    private static readonly Regex DuplicatePrefix = new(@"^\d+-\s*", RegexOptions.CultureInvariant);

    public static DeviceGroupInfo SectionOf(AudioDirection direction) => direction == AudioDirection.Output
        ? new DeviceGroupInfo("section:outputs", "OUTPUTS", 0)
        : new DeviceGroupInfo("section:inputs", "INPUTS", 1);

    /// <summary>The physical-device group of every endpoint (by endpoint ID). Groups with an active endpoint come
    /// first, "This PC" first among them; the rest follow by name.</summary>
    public static IReadOnlyDictionary<string, DeviceGroupInfo> ByDevice(IEnumerable<DeviceInfo> devices)
    {
        var result = new Dictionary<string, DeviceGroupInfo>();
        foreach (var group in devices.GroupBy(KeyOf))
        {
            var members = group.ToList();
            bool thisPc = group.Key == "device:pc";
            bool unknown = group.Key == "device:none";
            string title = thisPc ? "This PC" : unknown ? "Other" : TitleOf(members);
            string tooltip = thisPc
                ? "Built-in audio. Windows puts every built-in device (speakers, headphone jack, microphones, HDMI on " +
                  "the graphics card, …) in one \"this PC\" container."
                : unknown
                    ? "Endpoints that don't report which physical device they belong to"
                    : "The endpoints of one physical device (they share a container ID)";
            int order = (members.Any(m => m.State == DeviceConnectionState.Active) ? 0 : 10) + (thisPc ? 0 : 1);
            var info = new DeviceGroupInfo(group.Key, title, order, tooltip);
            foreach (var member in members) result[member.Id] = info;
        }
        return result;
    }

    private static string KeyOf(DeviceInfo device) => device.ContainerId switch
    {
        null => "device:none",
        var c when c == ComputerContainerId => "device:pc",
        var c => "device:" + c.Value.ToString("D"),
    };

    /// <summary>The name most of the group's endpoints give their device (active ones preferred).</summary>
    public static string TitleOf(IReadOnlyList<DeviceInfo> members)
    {
        var preferred = members.Where(m => m.State == DeviceConnectionState.Active).ToList();
        if (preferred.Count == 0) preferred = members.ToList();
        return preferred.Select(DeviceNameOf)
                   .Where(n => !string.IsNullOrWhiteSpace(n))
                   .GroupBy(n => n!, StringComparer.OrdinalIgnoreCase)
                   .OrderByDescending(g => g.Count())
                   .ThenBy(g => g.Key.Length)
                   .Select(g => g.Key)
                   .FirstOrDefault()
               ?? members[0].FriendlyName;
    }

    /// <summary>
    /// The physical device's name as one endpoint tells it. Endpoint names read "Speakers (Jabra Link 390)": the part
    /// in brackets is the audio interface, which is the device — except for HDMI / DisplayPort, where the interface is
    /// the graphics card's audio ("NVIDIA High Definition Audio") and the endpoint is named after the monitor.
    /// </summary>
    public static string? DeviceNameOf(DeviceInfo device)
    {
        var (endpoint, adapter) = SplitName(device.FriendlyName);
        // Without a monitor name Windows calls a display endpoint "Digital Output", "NVIDIA Output", …: then the
        // interface is the better name.
        bool monitorName = device.FormFactor == FormFactorDigitalDisplay && adapter is not null
                           && !GenericDisplayName.IsMatch(endpoint);
        string? name = monitorName ? endpoint : device.InterfaceName ?? adapter;
        return name is null ? null : DuplicatePrefix.Replace(name.Trim(), "");
    }

    private static readonly Regex GenericDisplayName = new(@"\b(output|digital|audio|speakers?)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>"Speakers (Jabra Link 390)" → ("Speakers", "Jabra Link 390"); a name without brackets → (name, null).
    /// The last " (" counts, so "Digital Audio (HDMI) (Intel Display Audio)" → ("Digital Audio (HDMI)", "Intel Display
    /// Audio"), and "Speakers (Realtek(R) Audio)" keeps its "(R)".</summary>
    public static (string Endpoint, string? Adapter) SplitName(string friendlyName)
    {
        int open = friendlyName.LastIndexOf(" (", StringComparison.Ordinal);
        return open > 0 && friendlyName.EndsWith(')')
            ? (friendlyName[..open], friendlyName[(open + 2)..^1])
            : (friendlyName, null);
    }
}
