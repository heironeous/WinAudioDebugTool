using AudioDeviceDebugger.Models;

namespace AudioDeviceDebugger.Services;

/// <summary>One entry of an app's device menu (Processes tab): the Windows default (<see cref="EndpointId"/> null)
/// or one endpoint.</summary>
public sealed record RouteChoice(string? EndpointId, string Name, bool IsCurrent, bool IsAvailable = true);

/// <summary>Per-app devices (N6): the choices offered for an app, built from the device list.</summary>
public static class AppRouting
{
    /// <summary>"Windows default (…current default…)" first, then every active endpoint of the direction by name. An
    /// app set to an endpoint that isn't active (unplugged, disabled) gets that entry too, marked unavailable, so the
    /// menu shows where it is set.</summary>
    public static IReadOnlyList<RouteChoice> BuildChoices(IEnumerable<DeviceInfo> devices, AudioDirection direction,
        string? currentEndpointId)
    {
        var ofDirection = devices.Where(d => d.Direction == direction).ToList();
        string? defaultName = ofDirection.FirstOrDefault(d => d.IsDefaultDevice && d.State == DeviceConnectionState.Active)?.FriendlyName;

        var choices = new List<RouteChoice>
        {
            new(null, defaultName is null ? "Windows default" : $"Windows default ({defaultName})", currentEndpointId is null),
        };
        choices.AddRange(ofDirection
            .Where(d => d.State == DeviceConnectionState.Active)
            .OrderBy(d => d.FriendlyName, StringComparer.CurrentCultureIgnoreCase)
            .Select(d => new RouteChoice(d.Id, d.FriendlyName, SameId(d.Id, currentEndpointId))));

        if (currentEndpointId is not null && !choices.Any(c => c.IsCurrent))
        {
            var current = ofDirection.FirstOrDefault(d => SameId(d.Id, currentEndpointId));
            string name = current is null ? "Unknown device" : $"{current.FriendlyName} ({StateWord(current.State)})";
            choices.Add(new RouteChoice(currentEndpointId, name, IsCurrent: true, IsAvailable: false));
        }
        return choices;
    }

    /// <summary>Endpoint IDs compare without case: Windows hands them back in its own capitalisation.</summary>
    public static bool SameId(string? a, string? b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private static string StateWord(DeviceConnectionState state) => state switch
    {
        DeviceConnectionState.Disabled => "disabled",
        DeviceConnectionState.Unplugged => "unplugged",
        DeviceConnectionState.NotPresent => "not connected",
        _ => "not active",
    };
}
