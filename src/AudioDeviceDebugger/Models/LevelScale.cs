namespace AudioDeviceDebugger.Models;

/// <summary>
/// Converts the linear peak amplitudes Windows reports (0.0–1.0, from IAudioMeterInformation) into the decibel scale
/// every meter in the UI uses.
/// </summary>
/// <remarks>
/// A linear bar badly understates real-world levels: speech peaking at −30 dBFS (perfectly clear) is only 3% of full
/// scale. On this dB scale the bar spans <see cref="FloorDb"/> (empty) to 0 dBFS (full), like a typical audio meter.
/// </remarks>
public static class LevelScale
{
    /// <summary>Level shown as an empty bar; anything quieter is treated as silence.</summary>
    public const double FloorDb = -60;

    /// <summary>Peak amplitude (0–1) → dBFS, or <see cref="double.NegativeInfinity"/> for digital silence.</summary>
    public static double ToDb(double peak) => peak > 0 ? 20 * Math.Log10(peak) : double.NegativeInfinity;

    /// <summary>Peak amplitude (0–1) → bar fill (0–1) on the −60…0 dBFS scale.</summary>
    public static double ToMeter(double peak) => Math.Clamp((ToDb(peak) - FloorDb) / -FloorDb, 0, 1);

    /// <summary>dBFS → bar fill (0–1); for drawing scale gridlines.</summary>
    public static double DbToMeter(double db) => Math.Clamp((db - FloorDb) / -FloorDb, 0, 1);

    /// <summary>Readout text, e.g. "−24.3 dBFS"; anything below the floor reads "&lt; −60 dBFS".</summary>
    public static string Format(double peak)
    {
        double db = ToDb(peak);
        return db < FloorDb ? $"< {FloorDb:0} dBFS" : $"{db:0.0} dBFS";
    }
}
