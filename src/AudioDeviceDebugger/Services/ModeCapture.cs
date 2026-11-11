using System.IO;
using System.Runtime.InteropServices;
using AudioDeviceDebugger.Interop;
using AudioDeviceDebugger.Models;
using NAudio.Wave;

namespace AudioDeviceDebugger.Services;

/// <summary>
/// One shared-mode capture stream opened with a chosen processing mode (<see cref="CaptureMode"/>): Communications
/// sets the stream category, Raw sets AUDCLNT_STREAMOPTIONS_RAW, both via IAudioClient2::SetClientProperties before
/// Initialize. Polled (no event handle): call <see cref="Drain"/> every ~10 ms. Use from one MTA thread.
/// </summary>
internal sealed class ModeCapture : IDisposable
{
    private const long BufferDuration = 2_000_000; // 200 ms, in 100-ns units
    private const uint FlagDiscontinuity = 0x1;
    private const uint FlagSilent = 0x2;

    private IAudioClient2Native? _client;
    private IAudioCaptureClientNative? _capture;
    private readonly MemoryStream _data = new();
    private byte[] _scratch = Array.Empty<byte>();

    private ModeCapture(CaptureMode mode, IAudioClient2Native client, IAudioCaptureClientNative capture, WaveFormat format,
        IReadOnlyList<AudioEffectInfo>? effects)
    {
        Mode = mode;
        _client = client;
        _capture = capture;
        Format = format;
        Effects = effects;
    }

    public CaptureMode Mode { get; }

    /// <summary>The stream's format: the endpoint's mix format as reported *after* the mode was set.</summary>
    public WaveFormat Format { get; }

    /// <summary><see cref="Format"/> as a plain PCM / IEEE-float format with the same sample layout, for NAudio's
    /// converters (which reject WAVE_FORMAT_EXTENSIBLE headers).</summary>
    public WaveFormat SampleFormat => Format is WaveFormatExtensible extensible ? extensible.ToStandardWaveFormat() : Format;

    /// <summary>Effects the engine applies to this stream, or null if Windows can't report them.</summary>
    public IReadOnlyList<AudioEffectInfo>? Effects { get; }

    /// <summary>Packets the engine flagged as a discontinuity (audio lost because it wasn't read in time).</summary>
    public int Glitches { get; private set; }

    public long BytesCaptured => _data.Length;

    /// <summary>Opens (but doesn't start) a capture stream on <paramref name="deviceId"/> in <paramref name="mode"/>.
    /// Throws if the device or driver refuses the mode.</summary>
    public static ModeCapture Open(string deviceId, CaptureMode mode)
    {
        var client = AudioClient2Activator.Activate(deviceId);
        try
        {
            if (mode != CaptureMode.Standard)
            {
                var properties = new AudioClientPropertiesNative
                {
                    Size = Marshal.SizeOf<AudioClientPropertiesNative>(),
                    Category = mode == CaptureMode.Communications ? AudioStreamCategory.Communications : AudioStreamCategory.Other,
                    Options = mode == CaptureMode.Raw ? AudioClientStreamOptionFlags.Raw : AudioClientStreamOptionFlags.None,
                };
                int hr = client.SetClientProperties(ref properties);
                if (hr < 0)
                {
                    throw new NotSupportedException(
                        $"the driver refused {CaptureModeText.Name(mode).ToLowerInvariant()} capture (0x{hr:X8})");
                }
            }

            Marshal.ThrowExceptionForHR(client.GetMixFormat(out var formatPtr));
            WaveFormat format;
            try
            {
                format = WaveFormat.MarshalFromPtr(formatPtr);
                Marshal.ThrowExceptionForHR(client.Initialize(0 /* shared */, 0, BufferDuration, 0, formatPtr, IntPtr.Zero));
            }
            finally
            {
                Marshal.FreeCoTaskMem(formatPtr);
            }

            var iid = typeof(IAudioCaptureClientNative).GUID;
            Marshal.ThrowExceptionForHR(client.GetService(ref iid, out var service));
            return new ModeCapture(mode, client, (IAudioCaptureClientNative)service, format, ReadEffects(client));
        }
        catch
        {
            Marshal.ReleaseComObject(client);
            throw;
        }
    }

    public void Start() => Marshal.ThrowExceptionForHR(_client!.Start());

    /// <summary>Copies every packet the engine has ready into the recording.</summary>
    public void Drain()
    {
        var capture = _capture!;
        while (capture.GetNextPacketSize(out uint pending) >= 0 && pending > 0)
        {
            if (capture.GetBuffer(out var data, out uint frames, out uint flags, out _, out _) < 0) return;
            int bytes = (int)frames * Format.BlockAlign;
            if (_scratch.Length < bytes) _scratch = new byte[bytes];
            if ((flags & FlagSilent) != 0)
            {
                Array.Clear(_scratch, 0, bytes);
            }
            else
            {
                Marshal.Copy(data, _scratch, 0, bytes);
            }
            if ((flags & FlagDiscontinuity) != 0 && _data.Length > 0) Glitches++;
            _data.Write(_scratch, 0, bytes);
            capture.ReleaseBuffer(frames);
        }
    }

    public void Stop()
    {
        _client?.Stop();
        Drain();
    }

    public byte[] GetRecording() => _data.ToArray();

    public void Dispose()
    {
        if (_capture is not null) Marshal.ReleaseComObject(_capture);
        if (_client is not null) Marshal.ReleaseComObject(_client);
        _capture = null;
        _client = null;
    }

    private static IReadOnlyList<AudioEffectInfo>? ReadEffects(IAudioClient2Native client)
    {
        try
        {
            var iid = typeof(IAudioEffectsManagerNative).GUID;
            if (client.GetService(ref iid, out var service) < 0) return null;
            var manager = (IAudioEffectsManagerNative)service;
            try
            {
                if (manager.GetAudioEffects(out var array, out uint count) < 0) return null;
                try
                {
                    int size = Marshal.SizeOf<AudioEffectNative>();
                    var effects = new List<AudioEffectInfo>((int)count);
                    for (int i = 0; i < count; i++)
                    {
                        var effect = Marshal.PtrToStructure<AudioEffectNative>(array + i * size);
                        effects.Add(new AudioEffectInfo(AudioEffectNames.Describe(effect.Id), effect.State != 0, effect.CanSetState != 0));
                    }
                    return effects;
                }
                finally
                {
                    Marshal.FreeCoTaskMem(array);
                }
            }
            finally
            {
                Marshal.ReleaseComObject(manager);
            }
        }
        catch
        {
            return null; // E_NOINTERFACE before Windows 11, or the driver doesn't report effects
        }
    }
}
