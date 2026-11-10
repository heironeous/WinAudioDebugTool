using AudioDeviceDebugger.Models;

namespace AudioDeviceDebugger.Services;

public interface IDeviceDetailsService
{
    DeviceDetails GetDetails(string deviceId);
}
