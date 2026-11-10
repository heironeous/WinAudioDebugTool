using AudioDeviceDebugger.Models;

namespace AudioDeviceDebugger.Services;

public interface IDeviceEnumerationService : IDisposable
{
    /// <summary>Enumerates every render/capture endpoint regardless of state (active, disabled, unplugged, not present).</summary>
    IReadOnlyList<DeviceInfo> GetDevices();

    /// <summary>
    /// Raised on a Core Audio notification thread (not the UI thread) whenever a device is added/removed,
    /// changes state, or the system default changes. Subscribers must marshal back to the UI thread before
    /// touching bound collections.
    /// </summary>
    event EventHandler? DevicesChanged;
}
