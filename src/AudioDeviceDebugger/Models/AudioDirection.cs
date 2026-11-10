namespace AudioDeviceDebugger.Models;

/// <summary>Mirrors NAudio's DataFlow, kept separate so Models don't leak NAudio types into the UI layer.</summary>
public enum AudioDirection
{
    Output,
    Input,
}
