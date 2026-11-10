# Windows Audio Device Debugger

A Windows desktop tool for inspecting and debugging audio input/output
devices: enumerate every endpoint, inspect its properties, switch the
system-wide default (regular and "communications") device, play test tones,
record/play back from microphones, and watch live level meters for every
device at once.

## Why this exists

Windows' own Sound control panel only gives you a name and a single default
toggle. This tool is meant for the "why isn't my mic/speaker working" / "which
of these five USB audio devices is actually active" situations — it surfaces
the full Core Audio picture (endpoint IDs, formats, roles, property store)
and lets you drive the device straight from the UI.

## Tech stack

- **C# / .NET 8, WPF** — chosen because it's the most direct way to get a
  native Windows UI with deep Core Audio integration (COM interop for
  `IMMDeviceEnumerator`, `IPolicyConfig`, `IAudioMeterInformation`, etc.)
  without going through a cross-platform abstraction that would hide the
  Windows-specific behavior this tool exists to expose.
- **NAudio** — wraps WASAPI (`MMDeviceEnumerator`, `WasapiOut`,
  `WasapiCapture`) so we don't hand-roll every COM interface, while still
  allowing raw interop where NAudio doesn't expose something (notably
  `IPolicyConfig`, which is how Windows itself changes the default device —
  it's undocumented but has been stable across Windows versions).

## Repository layout

```
/workspace
├── TODO.md                     Feature checklist, updated as work lands
├── README.md                   This file
├── AudioDeviceDebugger.sln     Visual Studio / dotnet solution
└── src/
    └── AudioDeviceDebugger/    WPF application (net8.0-windows)
        ├── App.xaml(.cs)       Application entry point
        ├── Interop/            Raw COM interop: IPolicyConfig, PROPERTYKEY
        │                       definitions, PKEY constants not exposed by NAudio
        ├── Models/             Plain data models (DeviceInfo, LevelSample, ...)
        ├── Services/           Business logic, no UI:
        │                         - DeviceEnumerationService: lists endpoints
        │                         - DefaultDeviceService: reads/sets default roles
        │                         - DeviceDetailsService: property-store dump,
        │                           format inspection
        │                         - AudioTestService: tone playback, mic record
        │                           + playback round-trip
        │                         - LevelMeterService: polls
        │                           IAudioMeterInformation per device
        ├── ViewModels/          MVVM glue between Services and Views
        ├── Views/               XAML windows/controls:
        │                         - MainWindow: device list + details + meters
        │                         - LevelGraph: custom rolling-graph control
        └── Assets/              App icon, etc.
```

## Building (on Windows — this cannot be built or run in this Linux container)

Prerequisites: [.NET 8 SDK](https://dotnet.microsoft.com/download) on Windows
10/11.

```powershell
cd src\AudioDeviceDebugger
dotnet restore
dotnet build -c Release
```

To produce a single self-contained `.exe` (no .NET runtime required on the
target machine):

```powershell
dotnet publish -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

The output executable will be at:

```
src\AudioDeviceDebugger\bin\Release\net8.0-windows\win-x64\publish\AudioDeviceDebugger.exe
```

## Running

Just launch the published `.exe`, or run `dotnet run` from
`src\AudioDeviceDebugger` during development. No installer, no admin rights
required (changing the default audio device and reading levels are
user-level operations in Windows' Core Audio API).

## Testing

This tool was written inside a Linux Docker container, which has **no**
Windows audio stack, so none of the code here has been compiled or run yet.
There is no CI for this repo for the same reason. Treat the current state as
"should build" code that needs a first real pass on Windows. When you build
it there:

1. **Smoke test**: launch the app, confirm the device list populates with
   your actual output/input devices and matches what Windows' Sound settings
   shows.
2. **Enumeration correctness**: unplug/plug a USB headset or disable a device
   in Windows Sound settings, hit **Refresh**, and confirm the list and state
   (Active/Disabled/Unplugged) update correctly.
3. **Default switching**: use the app to set a different default and
   communications device, then confirm Windows Sound settings agrees, and
   that an actual call app (Teams/Discord) and a media player pick the roles
   up correctly.
4. **Output test**: hit "Test" on each output device and confirm you hear the
   tone from the correct physical device.
5. **Input test**: hit "Test" on each input device, speak into it, and
   confirm the recorded playback is audible and the live meter moved while
   you spoke.
6. **Level meters**: confirm meters move for all devices simultaneously
   (e.g., play music and talk into a mic at the same time) without needing to
   select a device first.
7. **Property/detail dump**: spot check a couple of raw property values
   (e.g., sample rate, endpoint GUID) against `Get-AudioDevice`/`pnputil`/
   Device Manager output to sanity-check correctness.

Once you've done a pass, update `TODO.md`'s "Not yet done / follow-ups"
section with results, and file issues for anything broken.

Unit tests are not included for the Service layer because nearly all of it
is a thin wrapper over live Core Audio COM calls — there is no meaningful way
to exercise `IMMDeviceEnumerator`/`IPolicyConfig` without a real Windows audio
stack, so a mocked unit test would mostly test the mocks. If useful, a
`tests/AudioDeviceDebugger.Tests` project using xUnit can be added later for
any pure-logic pieces (e.g., formatting helpers, diffing enumeration
results) that don't need live devices.
