namespace AudioDeviceDebugger.Models;

/// <summary>Which signal-processing path a mic recording asks Windows for.</summary>
public enum CaptureMode
{
    /// <summary>The default path (stream category "Other"): what most apps — and this app's plain mic test — get.</summary>
    Standard,

    /// <summary>Stream category "Communications": what call apps such as Teams and Webex request, so the driver's
    /// call processing (echo cancellation, noise suppression, …) applies — i.e. what the other side of a call is
    /// sent, before the call app's own processing.</summary>
    Communications,

    /// <summary>The RAW stream option: the driver's and Windows' processing bypassed (if the driver supports it).</summary>
    Raw,
}

public static class CaptureModeText
{
    public static string Name(CaptureMode mode) => mode switch
    {
        CaptureMode.Communications => "Call",
        CaptureMode.Raw => "Raw",
        _ => "Standard",
    };

    public static string Description(CaptureMode mode) => mode switch
    {
        CaptureMode.Communications => "what call apps (Teams, Webex…) receive — the other side hears this, plus the app's own processing",
        CaptureMode.Raw => "no processing — straight from the microphone",
        _ => "default processing — what most apps receive",
    };
}

/// <summary>One effect the audio engine reported for a capture stream (IAudioEffectsManager, Windows 11).</summary>
public sealed record AudioEffectInfo(string Name, bool IsOn, bool CanSetState);
