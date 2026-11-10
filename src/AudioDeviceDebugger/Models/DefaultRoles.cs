namespace AudioDeviceDebugger.Models;

/// <summary>Which default-device roles to assign. "Default" is Windows' Console + Multimedia pair;
/// "Communications" is the separate role used by call/VoIP apps.</summary>
[Flags]
public enum DefaultRoles
{
    None = 0,
    Default = 1,
    Communications = 2,
    Both = Default | Communications,
}
