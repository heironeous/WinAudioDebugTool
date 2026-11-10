using AudioDeviceDebugger.Models;

namespace AudioDeviceDebugger.Services;

public interface IDeviceDetailsService
{
    /// <summary>Identity, state and formats — cheap enough to load on every selection.</summary>
    DeviceDetails GetDetails(string deviceId);

    /// <summary>Full property-store dump (every key, named where known). Comparatively expensive (~100+ entries,
    /// each read through COM), so it's only fetched while the properties panel is open.</summary>
    IReadOnlyList<PropertyEntry> GetProperties(string deviceId);
}
