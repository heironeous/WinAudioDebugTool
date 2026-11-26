using AudioDeviceDebugger.Interop;

namespace AudioDeviceDebugger.Services;

public interface IBluetoothConnectionService
{
    /// <summary>
    /// Asks Windows to connect (or disconnect) the Bluetooth audio device behind the given endpoints — like Connect /
    /// Disconnect in the Windows Sound settings. Pass every endpoint of the headset: endpoints that share a Bluetooth
    /// profile are signalled once. Runs off the UI thread and returns the number of profiles signalled; the endpoints
    /// change state later, when the link is up or down. Throws if no profile could be signalled.
    /// </summary>
    Task<int> SetConnectedAsync(IReadOnlyCollection<string> endpointIds, bool connect);
}

/// <inheritdoc cref="IBluetoothConnectionService"/>
public sealed class BluetoothConnectionService : IBluetoothConnectionService
{
    public Task<int> SetConnectedAsync(IReadOnlyCollection<string> endpointIds, bool connect) =>
        // A pool thread (MTA): every COM object is created and used there (lesson 3).
        Task.Run(() =>
        {
            var errors = new List<string>();
            var audioDevices = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var id in endpointIds)
            {
                try
                {
                    audioDevices.Add(BluetoothAudioControl.GetAudioDeviceId(id));
                }
                catch (Exception ex)
                {
                    errors.Add(ex.Message);
                }
            }

            int signalled = 0;
            foreach (var audioDevice in audioDevices)
            {
                try
                {
                    BluetoothAudioControl.Send(audioDevice, connect);
                    signalled++;
                }
                catch (Exception ex)
                {
                    errors.Add(ex.Message);
                }
            }

            if (signalled == 0)
            {
                throw new InvalidOperationException(errors.Count > 0
                    ? string.Join("; ", errors.Distinct())
                    : "No Bluetooth audio device found for this endpoint");
            }
            return signalled;
        });
}
