# Windows Audio Device Debugger

[![Build, tag and release](https://github.com/heironeous/WinAudioDebugTool/actions/workflows/build-and-tag.yml/badge.svg)](https://github.com/heironeous/WinAudioDebugTool/actions/workflows/build-and-tag.yml)

A Windows desktop tool for finding out what's really going on with your audio devices: every input and output
Windows knows about, which one is the default, who is using it, what format it runs at, what processing Windows
applies to your microphone — and quick tests to check that a speaker or mic actually works.

Built for the "why isn't my mic/speaker working", "which of these five USB headsets is actually active" and "why
does my headset sound terrible in calls" situations, where Windows' own Sound settings only give you a name and a
default toggle.

![Audio Device Debugger: the device list with OUTPUTS and INPUTS sections, device icons, Default and Comms badges, live levels and volume; below it the selected output while a test clip plays, with its header card, level graph and channel meters, and the Events tab open](docs/images/screenshot.png)

*The device list (device icons, Default and Comms badges, live levels, volume and mute, Test buttons) and, below it,
the selected output while a test clip plays: its format, rolling dB level graph with peak hold, channel meters, and the
Events tab with the timeline.*

## Features

**Device list**
- Every audio endpoint in two sections, **OUTPUTS** and **INPUTS**, including disabled, not-present and unplugged ones
  (the **Also show** chips at the top right), with an icon for its type, its role badges (Default · Comms), a live level
  meter, volume slider and mute button.
- **By device** (in the list's header) groups the endpoints by physical device instead: a headset's speaker and
  microphone together, a monitor's outputs together, everything built in under **This PC**. Click a group's heading to
  collapse it. The button then reads **By input / output**, which switches back.
- Updates by itself when devices are plugged in or removed, or when another app changes a volume.
- "· In use by *App*" when another application is streaming to or from the device.

**Defaults** — make the selected device the default, the default *communications* device, or both.

**Bluetooth** — **Connect** / **Disconnect** a headset paired with Windows over Bluetooth, like the button in the
Windows Sound settings (it stays paired). It acts on the whole headset, so its Headphones and Headset endpoints go up
or down together. Only enabled for Bluetooth devices: a headset behind a USB dongle (Jabra Link, Poly BT700, …) is a
USB device to Windows, and the dongle's own link can't be switched from here.

**Disable / Enable** — switch off the selected input or output, like Disable in the Windows Sound settings: apps can't
use it until it's enabled again, while the device's other endpoints keep working (disable a headset's mic and its
headphones still play). Works for every kind of device and needs no admin rights. If it was a default device, Windows
moves the default to another device, and back when it's enabled again. A row you disable stays in the list while it's
selected, even with the **Disabled** chip off.

**Tests**
- **Outputs:** play a short generated music clip (drums, bass, chords, melody), or play it on one speaker channel at
  a time (front left, centre, LFE, …) to check wiring and channel order.
- **Inputs:** record 5 seconds and play it back; then listen to each microphone channel on its own.

**Details** for the selected device: a header card (roles, state, shared- and exclusive-mode formats, whether the driver
supports raw (unprocessed) capture, a button that copies the endpoint ID), a rolling dB level graph with peak hold and
colour zones (yellow from −12 dB, red from −3 dB), and per-channel meters.

**Side panel tabs** (beside the header card; one open at a time — click the open tab to close it)
- **Properties** — the endpoint's full property store, with readable names for ~1,550 documented keys (plus a few
  undocumented ones whose meaning was confirmed) and readable values where the format is known: "Headset (5)",
  "Communication speaker", "48 kHz · 32-bit float · 2 ch (FL FR)", "20 dB", a mic array's microphone positions, … —
  the raw value is in the tooltip.
- **Events** — a timeline of device changes, default switches, volume/mute changes (by Windows or by this app), apps
  starting and stopping streams, and tests. Filter it, copy it, or export it as CSV, Markdown or JSON Lines — handy
  for "my mic died in the meeting at 14:03". The timeline is also saved to disk, one JSON Lines file per day in
  `%LOCALAPPDATA%\AudioDeviceDebugger`, kept for 14 days.
- **Processes** — which applications have the selected device open (app, process and PID, streaming or idle, the app's
  own volume, live level). Opening a Bluetooth/USB headset's mic is what switches it into low-quality "call mode", so
  this shows who is doing it.
- **Raw recording** — **Record A/B** records the selected mic three ways *at the same time*: **Raw** (no
  processing), **Call** (what call apps such as Teams or Webex receive — i.e. what the other side hears, before the
  app's own processing) and **Standard** (what most apps get), then plays Raw and Call back to back. Each take shows
  its level, format, per-channel levels and the effects Windows applied (echo cancellation, noise suppression, …), and
  you can listen to each channel of each take — for example the individual capsules of a laptop mic array.
- **Driver** — what is behind the endpoint: the audio device (name, manufacturer, how it's connected — USB,
  HD Audio, Bluetooth, …, and whether Device Manager reports a problem), its driver (provider, version, date, INF, and
  whether it's Windows' generic class driver), for USB devices the vendor and product ID and the speed the device is
  connected at (with a warning when a faster device runs slower because of a USB 2 port or cable), and the audio
  effects (APOs) Windows runs on the endpoint, named, with the driver package that installed them. **Copy** puts it on
  the clipboard as text for a bug report (it contains device instance IDs).

All levels are shown in dBFS (−60…0 dB).

**Light, dark and high contrast** — the app follows Windows: light or dark after the app mode in Settings →
Personalisation → Colours, and Windows' own colours when a contrast theme is on. It switches while running. To force
one (for screenshots, say), start it with `--theme=light`, `--theme=dark` or `--theme=high-contrast`.

## Requirements

- Windows 10 or 11, x64. Developed and tested on Windows 11.
- No admin rights needed.
- The effects list in Raw recording needs Windows 11 (`IAudioEffectsManager`); raw capture needs a driver that
  supports it (the app tells you).
- To run the released exe: nothing else — it includes the .NET runtime.
- To build it yourself: the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).

## Getting it

### Download

Each version is published on the [Releases](https://github.com/heironeous/WinAudioDebugTool/releases) page.
Download `AudioDeviceDebugger-vX.Y.Z-win-x64.exe` from the latest release and run it — it's a single, self-contained
file (about 70 MB, because it includes the .NET runtime): no installer, no admin rights, nothing else to install.

- **Windows SmartScreen** may warn "Windows protected your PC", because the exe isn't code-signed. Click **More
  info → Run anyway** if you trust the download. Company-managed PCs may block unsigned executables altogether.
- To check the download wasn't corrupted or altered, compare its hash with the `.sha256` file next to it in the
  release:

  ```powershell
  Get-FileHash .\AudioDeviceDebugger-vX.Y.Z-win-x64.exe -Algorithm SHA256
  ```

### Build from source

```powershell
git clone https://github.com/heironeous/WinAudioDebugTool.git
cd WinAudioDebugTool\src\AudioDeviceDebugger
dotnet build -c Release
.\bin\Release\net10.0-windows\win-x64\AudioDeviceDebugger.exe
```

Or run it directly with `dotnet run` from `src\AudioDeviceDebugger`.

To produce a single self-contained `.exe` that runs on machines without .NET installed:

```powershell
cd src\AudioDeviceDebugger
dotnet publish -p:PublishProfile=win-x64-selfcontained
# → bin\Release\net10.0-windows\win-x64\publish\AudioDeviceDebugger.exe
```

(The same profile can be picked in Visual Studio's Publish dialog.)

To run the unit tests (from the repo root):

```powershell
dotnet test
```

## Good to know

- **Selecting a microphone opens it.** Windows only reports an input's level while something records from it, so
  the app keeps a silent capture stream open on the *selected* input (and only that one). On many Bluetooth/USB
  headsets, opening the mic switches the headset into call mode, which lowers playback quality while the mic is
  selected. Select an output device, or minimise the window, to release it.
- **Tests make sound.** Output tests and mic playback play through your speakers/headset at the current volume.
- **Your audio stays on your machine.** Recordings are kept in memory only and are gone when you close the app. The
  event log is saved locally in `%LOCALAPPDATA%\AudioDeviceDebugger` (device names and IDs, apps and their PIDs, no
  audio); files older than 14 days are deleted. Nothing is sent anywhere.
- **Bluetooth Connect / Disconnect** was tried with a Bluetooth Classic headset (Jabra Evolve3 75). LE Audio
  headsets get the button too, but haven't been tried; if Windows refuses, the status line shows why.
- **Disabling a USB headset dongle's mic** (to keep the headset out of call mode) hasn't been tried yet.
- **Set Default and Disable / Enable use an undocumented Windows interface** (`IPolicyConfig`) — the same one Windows'
  own Sound settings use. It has been stable for many Windows versions, but Microsoft doesn't guarantee it.
- **Call ≠ exactly what the other side hears.** The Call take is what Windows and the driver deliver to call apps;
  Teams, Webex & co. add their own processing, a codec and the network on top. On many drivers Standard and Call use
  the same processing.
- **Processes only sees shared-mode streams** going through the Windows audio engine; exclusive-mode streams may not
  show up. Very short streams (under ~0.5 s) can be missed.
- In a quiet room a microphone can read below the −60 dB meter floor and look dead — hover the meter for the exact
  dBFS value.

## Testing status

Every finished item in [planning/DONE-TODO.md](planning/DONE-TODO.md) was tested by hand on a Windows 11 laptop with Realtek speakers, an Intel
Smart Sound Technology 4-channel mic array and a Jabra Link 390 USB headset dongle. Other hardware — surround setups,
pro audio interfaces, directly paired Bluetooth headsets — hasn't been tried; reports are welcome.

A GitHub Actions workflow builds every push to `main` on Windows (except documentation-only pushes), runs the unit
tests, tags it with the next version and publishes it as a release (see "Versioning" below). Pull requests into
`main` get the same tests and build, without a tag or release.

The unit tests (`tests/AudioDeviceDebugger.Tests`, xUnit) cover the parts that need no audio hardware: the event
log (merging bursts, filters, what gets written to disk), the exporters and the daily log files, the dB scale,
channel names, the property-value decoders, the channel conversion for test playback and the device rows' "in use"
text. Most of the rest is a thin layer over live Windows Core Audio calls, which can't be meaningfully unit-tested
without real devices; that part is checked by hand. A quick manual check after building:

1. The device list matches what Windows Sound settings shows; plugging/unplugging a USB headset updates it.
2. **Set Default / Set Comms** change the defaults, and Windows Sound settings agrees.
   **Disconnect** / **Connect** on a Bluetooth headset drops and restores it (its rows go Unplugged and back).
3. **Test** on an output plays from the right device; **Test** on a mic records and plays back what you said.
4. Meters move for several devices at once; the channel strip plays one speaker at a time.
5. Events logs your volume changes and device switches; Processes shows a call app or browser that has the mic open.
6. Raw recording → **Record A/B**: the Raw and Call takes sound different and list different effects.

## Versioning

Versions follow [semantic versioning](https://semver.org/) and are derived from
[conventional commit](https://www.conventionalcommits.org/) messages by
`.github/workflows/build-and-tag.yml` (logic in `.github/scripts/next-version.sh`). On each push to `main` the
workflow publishes the self-contained exe and, if that succeeds, tags the commit with the next version and creates a
GitHub Release for the tag with the exe (`AudioDeviceDebugger-vX.Y.Z-win-x64.exe`) and its SHA-256 checksum
attached. The release notes list the commits since the previous release. The bump is chosen like this:

| Commits since the last tag include… | Bump | Example |
|---|---|---|
| a breaking change (`feat!:`, `fix!:`, … or a `BREAKING CHANGE:` footer) | major | v1.4.2 → v2.0.0 |
| a `feat:` | minor | v1.4.2 → v1.5.0 |
| only other types (`fix:`, `perf:`, `docs:`, `chore:`, …) | patch | v1.4.2 → v1.4.3 |

Pushes that only change documentation — Markdown files (`*.md`) and anything under `docs/` — don't run the
workflow, so they get no tag of their own; their commits are included in the next tag. A push that changes
documentation *and* anything else is built and tagged as usual.

The first tag uses the `<Version>` in `src/AudioDeviceDebugger/AudioDeviceDebugger.csproj` (0.1.0). The CI build
stamps the version into the executable (file and product version); local builds keep the `.csproj` version.

## Reporting problems

Please open an issue on GitHub with your Windows version, the device(s) involved, and — if relevant — the Events log
exported as Markdown (Export… → `.md`). Note that endpoint IDs in the log can contain hardware serial numbers.

## How it works

- **C# / .NET 10 (LTS), WPF** — a native Windows UI with direct access to the Core Audio COM APIs (`IMMDeviceEnumerator`,
  `IAudioEndpointVolume`, `IAudioMeterInformation`, `IAudioSessionManager2`, …).
- **[NAudio](https://github.com/naudio/NAudio) 2.2.1** for WASAPI playback/capture and most Core Audio wrappers.
- **Own COM interop** where NAudio has no API or is too slow: `IPolicyConfig` (setting defaults), `IAudioClient2`
  (raw and communications capture), `IAudioEffectsManager` (effects per stream), and direct property-store reads
  (NAudio's `FriendlyName` scans the whole store and takes ~0.5 s per device).

## Repository layout

```
├── .github/                     CI: build-and-tag workflow and its versioning script
├── README.md                    This file
├── TODO.md                      Open items and roadmap
├── planning/DONE-TODO.md        Finished features (checked items moved out of TODO.md)
├── CLAUDE.md                    Notes for AI coding agents (architecture, pitfalls, conventions)
├── docs/design-notes.md         Implementation and verification notes for the larger features
├── docs/images/                 Screenshots used in this README
├── AudioDeviceDebugger.sln
├── src/AudioDeviceDebugger/     The WPF app (net10.0-windows)
│   ├── Interop/                 Raw COM interop (IPolicyConfig, IAudioClient2, property reads, property-key names)
│   ├── Models/                  Plain data (devices, formats, events, sessions, recordings)
│   ├── Services/                Core Audio work, no UI: enumeration, defaults, details, volume, level meters,
│   │                            tests and recordings, app sessions, event-log export
│   ├── ViewModels/              MVVM glue between services and views
│   ├── Views/                   MainWindow, LevelGraph, Theme.xaml (control styles), Themes/ (dark and light
│   │                            palettes), ThemeManager (follows the Windows app mode and high contrast)
│   └── Assets/                  App icon
├── tests/AudioDeviceDebugger.Tests/  Unit tests (xUnit) for the parts that need no audio hardware
└── tools/PropertyKeyNameGenerator/   Regenerates the property-key name table
```

### Property names and values

The Properties tab shows each endpoint property by a readable name ("Device: Friendly Name") instead of its raw
`{fmtid},pid`, and decodes values whose format is known (`Services/PropertyValueFormatter.cs`). The names come from
tables compiled into the app:

- `src/AudioDeviceDebugger/Interop/PropertyKeyNames.g.cs`: **generated** — every documented `PKEY_*` / `DEVPKEY_*` in
  the Windows SDK (~1,550 keys), read from Microsoft's
  [Win32 metadata](https://www.nuget.org/packages/Microsoft.Windows.SDK.Win32Metadata) package. Don't edit by hand;
  regenerate from the repo root with `dotnet run --project tools/PropertyKeyNameGenerator` (bump the package version in
  `tools/PropertyKeyNameGenerator/PropertyKeyNameGenerator.csproj` to pick up newer SDKs).
- `src/AudioDeviceDebugger/Interop/KnownGuidNames.g.cs`: **generated** by the same tool — the audio-related GUID
  constants (jack/node types, categories, sample formats, signal-processing modes, effect types), used to show GUID
  values such as the jack type by name.
- `src/AudioDeviceDebugger/Interop/PropertyKeyNames.cs`: **hand-maintained** names for undocumented keys
  (`ManualNames`, `Undocumented`). Only add a key once its meaning is confirmed — see "Property names and values" in
  [docs/design-notes.md](docs/design-notes.md) for how the current ones were checked; unknown keys are shown by their
  raw `{fmtid},pid`.

## Contributing

Issues and pull requests are welcome. See [TODO.md](TODO.md) for the roadmap (bandwidth-based call-mode detection
and a diagnostic report export are specified there) and [CLAUDE.md](CLAUDE.md) for the hard-won lessons about
Core Audio, COM apartments and WPF that the code relies on — worth reading before changing services or interop.
