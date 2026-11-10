namespace AudioDeviceDebugger.Models;

/// <summary>One channel of an endpoint's shared-mode format, e.g. index 2 = "C" / "Centre".</summary>
/// <param name="Index">Position of the channel in the interleaved stream.</param>
/// <param name="ShortName">Compact label for the channel strip ("FL", "LFE", "Ch 3").</param>
/// <param name="LongName">Full name for tooltips/status ("Front Left", "Input channel 3").</param>
/// <param name="IsLfe">The low-frequency effects (subwoofer) channel — gets a low-passed test signal.</param>
public sealed record SpeakerChannel(int Index, string ShortName, string LongName, bool IsLfe);
