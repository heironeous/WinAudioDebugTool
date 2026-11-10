using System.Collections.Concurrent;
using NAudio.Wave;

namespace AudioDeviceDebugger.Services;

/// <summary>
/// A short, original, procedurally synthesised music clip used as the output test sound (instead of a sine tone).
/// </summary>
/// <remarks>
/// It's designed to exercise the whole band so quality differences are audible: kick + bass for the lows, chord pads
/// for the mids, a plucked melody, a bell layer and noise hi-hats/snare for the highs (above ~4–8 kHz — exactly what a headset in
/// call / hands-free mode cuts off). Elements are panned so left/right can be checked too.
/// Arrangement: 120 BPM, C – G – Am – F (two beats each), then a final C chord ringing out — ~5.5 s.
/// </remarks>
internal static class TestMusic
{
    private const double Bpm = 120;
    private const double Beat = 60.0 / Bpm;           // 0.5 s
    private const double Eighth = Beat / 2;
    private const double PeakLevel = 0.35;            // final normalised peak (the old sine tone was 0.25)

    public static readonly TimeSpan Duration = TimeSpan.FromSeconds(8 * Beat + 1.5);

    private static readonly ConcurrentDictionary<int, float[]> Cache = new();

    /// <summary>Renders the clip for a sample rate ahead of time (~0.5 s of CPU) so the first Test click starts
    /// instantly. Most endpoints run at 48 kHz.</summary>
    public static void Prewarm(int sampleRate = 48000) => Cache.GetOrAdd(sampleRate, Render);

    /// <summary>The clip mixed down to mono (L+R)/2 at <paramref name="sampleRate"/> — used for per-channel tests,
    /// where the whole arrangement should come out of one speaker.</summary>
    public static float[] GetMono(int sampleRate)
    {
        var stereo = Cache.GetOrAdd(sampleRate, Render);
        var mono = new float[stereo.Length / 2];
        for (int i = 0; i < mono.Length; i++)
        {
            mono[i] = 0.5f * (stereo[2 * i] + stereo[2 * i + 1]);
        }
        return mono;
    }

    /// <summary>Creates a provider playing the clip once, in <paramref name="format"/>'s sample rate and channel
    /// count (mono: L+R mixed; 3+ channels: front left/right carry the music, the rest stay silent).</summary>
    public static ISampleProvider Create(WaveFormat format) =>
        new StereoClipSampleProvider(Cache.GetOrAdd(format.SampleRate, Render), format.SampleRate, format.Channels);

    // ---- Arrangement -------------------------------------------------------------------------------------------

    private record struct Chord(int[] Notes, int Bass, double StartBeat, double LengthBeats);

    private static readonly Chord[] Progression =
    {
        new(new[] { 60, 64, 67 }, 36, 0, 2),          // C
        new(new[] { 55, 59, 62 }, 43, 2, 2),          // G
        new(new[] { 57, 60, 64 }, 45, 4, 2),          // Am
        new(new[] { 53, 57, 60 }, 41, 6, 2),          // F
        new(new[] { 60, 64, 67, 72 }, 36, 8, 3),      // C, ringing out
    };

    // Plucked melody, one note per eighth over the four chords, resolving to C on beat 8.
    private static readonly int[] Melody =
    {
        72, 76, 79, 76,    // over C
        74, 79, 83, 79,    // over G
        76, 72, 69, 72,    // over Am
        77, 76, 74, 71,    // over F (B leads back home)
    };

    // ---- Rendering --------------------------------------------------------------------------------------------

    /// <summary>Renders the clip as interleaved stereo float samples.</summary>
    private static float[] Render(int sampleRate)
    {
        int frames = (int)(Duration.TotalSeconds * sampleRate);
        var left = new double[frames];
        var right = new double[frames];
        var rng = new Random(20261110); // fixed seed: same clip every time

        foreach (var chord in Progression)
        {
            double start = chord.StartBeat * Beat;
            double length = chord.LengthBeats * Beat;
            foreach (int note in chord.Notes)
            {
                AddPad(left, right, sampleRate, Hz(note), start, length);
            }
            AddBass(left, right, sampleRate, Hz(chord.Bass), start, length);
        }

        for (int i = 0; i < Melody.Length; i++)
        {
            AddPluck(left, right, sampleRate, Hz(Melody[i]), i * Eighth, pan: 0.4, decay: 0.22, level: 0.22);
        }
        AddPluck(left, right, sampleRate, Hz(72), 8 * Beat, pan: 0.4, decay: 0.6, level: 0.25);

        for (int i = 0; i < Melody.Length; i += 2)
        {
            AddBell(left, right, sampleRate, Hz(Melody[i] + 12), i * Eighth);
        }
        AddBell(left, right, sampleRate, Hz(84), 8 * Beat);

        for (int beat = 0; beat <= 8; beat++)
        {
            if (beat % 2 == 0) AddKick(left, right, sampleRate, beat * Beat);
            else AddSnare(left, right, sampleRate, beat * Beat, rng);
        }
        for (int eighth = 0; eighth < 16; eighth++)
        {
            // Accented off-beats, quieter on-beats — constant high-frequency content.
            AddHat(left, right, sampleRate, eighth * Eighth, eighth % 2 == 1 ? 0.45 : 0.2, rng);
        }

        return Finish(left, right, sampleRate);
    }

    private static double Hz(int midiNote) => 440.0 * Math.Pow(2, (midiNote - 69) / 12.0);

    /// <summary>Equal-power pan: -1 = left, 0 = centre, +1 = right.</summary>
    private static (double L, double R) Pan(double pan)
    {
        double angle = (pan + 1) * Math.PI / 4;
        return (Math.Cos(angle), Math.Sin(angle));
    }

    /// <summary>Soft saw-like pad (band-limited additive), slightly detuned left vs right for width.</summary>
    private static void AddPad(double[] l, double[] r, int sr, double freq, double start, double length)
    {
        const double attack = 0.06, release = 0.25, level = 0.05;
        double fL = freq * Math.Pow(2, -4 / 1200.0), fR = freq * Math.Pow(2, 4 / 1200.0);
        int harmonics = Math.Max(1, Math.Min(6, (int)(sr / 2.0 / freq) - 1));
        ForRange(l.Length, sr, start, length + release, (i, t) =>
        {
            double env = Math.Min(1, t / attack) * (t > length ? Math.Max(0, 1 - (t - length) / release) : 1);
            double sL = 0, sR = 0;
            for (int k = 1; k <= harmonics; k++)
            {
                sL += Math.Sin(2 * Math.PI * fL * k * t) / k;
                sR += Math.Sin(2 * Math.PI * fR * k * t) / k;
            }
            l[i] += level * env * sL;
            r[i] += level * env * sR;
        });
    }

    /// <summary>Bass: sine + a touch of 2nd harmonic, re-plucked every beat of the chord.</summary>
    private static void AddBass(double[] l, double[] r, int sr, double freq, double start, double length)
    {
        for (double onset = 0; onset < length - 1e-9; onset += Beat)
        {
            double noteLength = Math.Min(Beat, length - onset) + 0.1;
            ForRange(l.Length, sr, start + onset, noteLength, (i, t) =>
            {
                double env = Math.Min(1, t / 0.005) * Math.Exp(-t / 0.35);
                double s = 0.22 * env * (Math.Sin(2 * Math.PI * freq * t) + 0.3 * Math.Sin(4 * Math.PI * freq * t));
                l[i] += s;
                r[i] += s;
            });
        }
    }

    /// <summary>Plucked square-ish lead (odd harmonics, band-limited) with exponential decay.</summary>
    private static void AddPluck(double[] l, double[] r, int sr, double freq, double start, double pan, double decay, double level)
    {
        var (gL, gR) = Pan(pan);
        ForRange(l.Length, sr, start, decay * 6, (i, t) =>
        {
            double env = Math.Min(1, t / 0.004) * Math.Exp(-t / decay);
            double s = 0;
            for (int k = 1; k <= 15 && freq * k < sr / 2.0; k += 2)
            {
                s += Math.Sin(2 * Math.PI * freq * k * t) / k;
            }
            l[i] += level * env * s * gL;
            r[i] += level * env * s * gR;
        });
    }

    /// <summary>Bell: inharmonic partials (1, 2.76, 5.40, 8.93 × f) — adds the 4–10 kHz sparkle that call-mode
    /// (narrow/wideband speech) audio loses. Panned right.</summary>
    private static void AddBell(double[] l, double[] r, int sr, double freq, double start)
    {
        var (gL, gR) = Pan(0.6);
        double[] ratios = { 1, 2.76, 5.40, 8.93 };
        double[] gains = { 0.5, 0.35, 0.25, 0.18 };
        ForRange(l.Length, sr, start, 1.2, (i, t) =>
        {
            double s = 0;
            for (int p = 0; p < ratios.Length; p++)
            {
                double f = freq * ratios[p];
                if (f >= sr / 2.0) break;
                s += gains[p] * Math.Exp(-t / (0.35 / (p + 1))) * Math.Sin(2 * Math.PI * f * t);
            }
            s *= 0.12 * Math.Min(1, t / 0.002);
            l[i] += s * gL;
            r[i] += s * gR;
        });
    }

    /// <summary>Kick: sine with a fast downward pitch sweep (~120 → 50 Hz).</summary>
    private static void AddKick(double[] l, double[] r, int sr, double start)
    {
        double phase = 0;
        ForRange(l.Length, sr, start, 0.5, (i, t) =>
        {
            double freq = 50 + 70 * Math.Exp(-t / 0.04);
            phase += 2 * Math.PI * freq / sr;
            double s = 0.4 * Math.Exp(-t / 0.15) * Math.Sin(phase);
            l[i] += s;
            r[i] += s;
        });
    }

    /// <summary>Snare: noise burst plus a short 180 Hz body, slightly left of centre.</summary>
    private static void AddSnare(double[] l, double[] r, int sr, double start, Random rng)
    {
        var (gL, gR) = Pan(-0.15);
        ForRange(l.Length, sr, start, 0.3, (i, t) =>
        {
            double noise = rng.NextDouble() * 2 - 1;
            double s = 0.18 * Math.Exp(-t / 0.07) * noise + 0.2 * Math.Exp(-t / 0.05) * Math.Sin(2 * Math.PI * 180 * t);
            l[i] += s * gL;
            r[i] += s * gR;
        });
    }

    /// <summary>Hi-hat: high-passed noise (first difference) with a very short decay, panned left.</summary>
    private static void AddHat(double[] l, double[] r, int sr, double start, double level, Random rng)
    {
        var (gL, gR) = Pan(-0.5);
        double previous = 0;
        ForRange(l.Length, sr, start, 0.12, (i, t) =>
        {
            double noise = rng.NextDouble() * 2 - 1;
            double highPassed = noise - previous;
            previous = noise;
            double s = level * Math.Exp(-t / 0.045) * highPassed;
            l[i] += s * gL;
            r[i] += s * gR;
        });
    }

    private static void ForRange(int frames, int sr, double start, double length, Action<int, double> perSample)
    {
        int first = (int)(start * sr);
        int last = Math.Min(frames, (int)((start + length) * sr));
        for (int i = Math.Max(0, first); i < last; i++)
        {
            perSample(i, (double)(i - first) / sr);
        }
    }

    /// <summary>Normalises to <see cref="PeakLevel"/>, fades the last 0.3 s out, interleaves to stereo floats.</summary>
    private static float[] Finish(double[] l, double[] r, int sr)
    {
        double peak = 1e-9;
        for (int i = 0; i < l.Length; i++)
        {
            peak = Math.Max(peak, Math.Max(Math.Abs(l[i]), Math.Abs(r[i])));
        }
        double gain = PeakLevel / peak;
        int fadeFrames = (int)(0.3 * sr);

        var interleaved = new float[l.Length * 2];
        for (int i = 0; i < l.Length; i++)
        {
            double fade = Math.Min(1.0, (double)(l.Length - i) / fadeFrames);
            interleaved[2 * i] = (float)(l[i] * gain * fade);
            interleaved[2 * i + 1] = (float)(r[i] * gain * fade);
        }
        return interleaved;
    }

    /// <summary>Plays an interleaved stereo float buffer once, mapped to the requested channel count.</summary>
    private sealed class StereoClipSampleProvider : ISampleProvider
    {
        private readonly float[] _stereo;
        private readonly int _channels;
        private int _frame;

        public StereoClipSampleProvider(float[] stereo, int sampleRate, int channels)
        {
            _stereo = stereo;
            _channels = channels;
            WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, channels);
        }

        public WaveFormat WaveFormat { get; }

        public int Read(float[] buffer, int offset, int count)
        {
            int totalFrames = _stereo.Length / 2;
            int frames = Math.Min(count / _channels, totalFrames - _frame);
            for (int f = 0; f < frames; f++, _frame++)
            {
                float left = _stereo[2 * _frame], right = _stereo[2 * _frame + 1];
                int o = offset + f * _channels;
                if (_channels == 1)
                {
                    buffer[o] = 0.5f * (left + right);
                    continue;
                }
                buffer[o] = left;
                buffer[o + 1] = right;
                for (int c = 2; c < _channels; c++)
                {
                    buffer[o + c] = 0;
                }
            }
            return frames * _channels;
        }
    }
}
