using AudioDeviceDebugger.Interop;
using NAudio.CoreAudioApi;

namespace AudioDeviceDebugger.Services;

public sealed class DefaultDeviceService : IDefaultDeviceService
{
    public void SetDefaultDevice(string deviceId)
    {
        // Windows treats "the" default device as Console + Multimedia together; setting only one
        // leaves Sound settings showing a mismatched/ambiguous default.
        PolicyConfigClient.SetDefaultEndpoint(deviceId, Role.Console);
        PolicyConfigClient.SetDefaultEndpoint(deviceId, Role.Multimedia);
    }

    public void SetDefaultCommunicationsDevice(string deviceId)
    {
        PolicyConfigClient.SetDefaultEndpoint(deviceId, Role.Communications);
    }
}
