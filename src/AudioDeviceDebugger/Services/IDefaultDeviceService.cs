namespace AudioDeviceDebugger.Services;

public interface IDefaultDeviceService
{
    /// <summary>Sets the endpoint as default for both the Console and Multimedia roles — matching what
    /// Windows' own Sound settings calls just "Default Device".</summary>
    void SetDefaultDevice(string deviceId);

    /// <summary>Sets the endpoint as the default Communications device (calls, VoIP apps, etc.).</summary>
    void SetDefaultCommunicationsDevice(string deviceId);

    /// <summary>Enables or disables the endpoint, like Enable / Disable in the Windows Sound settings.</summary>
    void SetEndpointEnabled(string deviceId, bool enabled);
}
