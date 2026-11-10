namespace AudioDeviceDebugger.Services;

public interface IDefaultDeviceService
{
    /// <summary>Sets the endpoint as default for both the Console and Multimedia roles — matching what
    /// Windows' own Sound settings calls just "Default Device".</summary>
    void SetDefaultDevice(string deviceId);

    /// <summary>Sets the endpoint as the default Communications device (calls, VoIP apps, etc.).</summary>
    void SetDefaultCommunicationsDevice(string deviceId);
}
