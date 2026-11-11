# CLAUDE.md — notes for AI coding agents (and curious contributors)

This file is read automatically by [Claude Code](https://claude.com/claude-code) and is written for AI coding agents
working on this repo. Human visitors: start with [README.md](README.md); the *Hard-won lessons* below are worth
reading too before changing services or interop.

Agents: read this first, then `README.md` (features, build, layout, regenerating property names), `TODO.md` (feature
checklist and roadmap) and `docs/design-notes.md` (how the larger features were built and verified). This file
records **where the project stands, how the maintainer likes to work, and the non-obvious things learned the hard
way**. Keep it current: when you finish a piece of work, update "Current state" and "Open items".

## What this is

**Audio Device Debugger** — a Windows WPF tool (C# / .NET 8, NAudio 2.2.1 + raw Core Audio COM interop) for
inspecting and driving audio endpoints: list every input/output (incl. disabled / not-present / unplugged), set the
default / communications / both roles, per-device volume + mute, live dB level meters, test playback (generated music)
and mic record-and-playback tests, per-channel tests, an event timeline, the apps using each device, raw vs
call-processed mic recording (A/B), and a full property-store dump with human-readable key names.

## Current state (as of commit `16b86d5`, 2026-11-11)

- Public on GitHub: <https://github.com/heironeous/WinAudioDebugTool>, branch `main` (work is committed straight to
  `main`). The maintainer pushes; agents don't.
- Builds cleanly (0 warnings) and runs on Windows 11; every feature in `TODO.md` that is checked was exercised on
  real hardware during development (the maintainer tests manually; see "Verifying changes"). No license file yet —
  the maintainer's decision. No CI, no automated tests, no releases.
- Docs: `README.md` is for users (features, requirements, build, caveats) — update it when a user-visible feature
  changes; `TODO.md` is the checklist + roadmap (N3, N5 specified); `docs/design-notes.md` holds implementation and
  verification notes for N1, N2, N4 and the side-panel tabs.
- The maintainer's hardware: Jabra Link 390 USB dongle (headset speakers + mic), Realtek laptop speakers, Intel Smart
  Sound Technology 4-channel mic array, plus many NotPresent endpoints from old USB headsets/dongles (Jabra Link 380,
  Poly BT700, Poly Voyager Focus 2, HP dock). **All headsets connect through USB dongles** — none are paired with
  Windows Bluetooth directly, so the A2DP/HFP ("call mode") switch happens inside the dongle, invisible to Windows.

## Environment & commands

- .NET 8 SDK 8.0.427 was installed via winget (only the runtime was present before). `dotnet` may not be on PATH in a
  fresh shell — use `"C:\Program Files\dotnet\dotnet.exe"`.
- No Windows SDK is installed (no C++ headers); property-key names come from the Win32 metadata NuGet package instead.
- Shells: PowerShell is primary; Git Bash is also available (and handy for heredocs / python one-off edits).

```powershell
cd src\AudioDeviceDebugger
dotnet build -c Release                                   # output: bin\Release\net8.0-windows\win-x64\AudioDeviceDebugger.exe
Start-Process bin\Release\net8.0-windows\win-x64\AudioDeviceDebugger.exe
dotnet run --project tools/PropertyKeyNameGenerator       # (from repo root) regenerate Interop/PropertyKeyNames.g.cs
```

The running app locks its exe: stop it before rebuilding (`Get-Process AudioDeviceDebugger | Stop-Process`).

## How the maintainer works (follow this)

- Requests are small and iterative; the maintainer tests each change by hand in the running app. After a change:
  build, **relaunch the app**, verify what you can, report briefly, and say what you could *not* verify.
- **Commit only when the maintainer says "commit"** (they always ask explicitly, in a separate message). One commit per
  feature/fix, conventional-commit subject (`feat:`, `fix:`, `perf:`), a body explaining *why* (root cause for fixes),
  ending with the `Co-Authored-By:` trailer given in the session's system prompt. Never push. Keep `TODO.md` (and
  `README.md` / `docs/design-notes.md` where relevant) updated in the same commit as the feature. Never commit
  `bin/` or `obj/` output, and keep scratch harnesses out of the repo.
- When the maintainer asks to *explain first* (e.g. the call-mode question), explain and propose options — don't build.
- Be careful with side effects on their real audio setup: don't switch default devices, change volumes, unmute, or
  play audible sound just to test something. Prefer silent checks (stream volume 0, muted devices, reading state via
  UI Automation). If a check must be audible, keep it short and say so. Restore anything you change.
- The maintainer may be clicking in the app while you run UI Automation; if results look inconsistent, re-check once
  before concluding there's a bug.

## Architecture map

`src/AudioDeviceDebugger/` (MVVM, no DI container — services are new'd up in `Views/MainWindow.xaml.cs`):

- `Interop/` — `IPolicyConfig` (undocumented default-endpoint setter), `EndpointPropertyReader` (fast keyed property
  reads), `PropertyKeyNames.cs` (+ generated `.g.cs`: ~1,550 SDK PKEY/DEVPKEY names; `ManualNames` for confirmed
  undocumented keys).
- `Services/` — `DeviceEnumerationService` (enumeration + IMMNotificationClient), `DefaultDeviceService`,
  `DeviceDetailsService` (formats; `GetProperties` separately), `EndpointVolumeService` (volume/mute + external-change
  notifications), `LevelMeterService` (single MTA polling thread), `AudioTestService` + `AudioTestSession` (phased
  tests with Stop), `TestMusic` (procedural test clip), `ChannelMatchingSampleProvider`, `EventLogExporter`
  (CSV / Markdown / JSON Lines / text), `AudioSessionService` (per-endpoint app sessions — "used by" — polled
  every 500 ms on its own MTA thread, plus session peaks for the selected device), `ModeCapture` (one capture
  stream in a chosen processing mode — Standard / Call (communications category) / Raw — via our own
  `Interop/AudioClient2.cs` IAudioClient2 interop, plus the stream's effects from IAudioEffectsManager); the test
  service's A/B recording runs three of them at once. `DeviceEnumerationService.EndpointNotified` carries each
  IMMNotificationClient callback in detail (for the event log) alongside the coarse `DevicesChanged`.
- `ViewModels/` — `MainViewModel` (refresh/debounce, filters, selection, tests, side panels, event logging),
  `DeviceRowViewModel`, `ChannelPeakViewModel`, `EventLogViewModel` (event timeline ring buffer + filters + merging),
  `SessionRowViewModel` (PROCESSES tab), `TakeRowViewModel` (RAW RECORDING tab: last take per processing mode). `Models/` — plain data + `LevelScale` (dB conversion).
- `Views/` — `MainWindow` (toolbar 2 lines; device grid top; details bottom: info + `LevelGraph` + channel bars left,
  collapsible right-hand side panel showing one of four tabs — PROPERTIES, EVENTS, PROCESSES (apps using the
  device), RAW RECORDING (A/B takes) — picked from a vertical tab strip whose labels hide when it's too short),
  `LevelGraph`.
- `tools/PropertyKeyNameGenerator/` — reads `Windows.Win32.winmd` from Microsoft.Windows.SDK.Win32Metadata.

## Hard-won lessons (don't regress these)

1. **NAudio `MMDevice.FriendlyName` is ~450 ms per device** (it linearly scans the ~150-entry property store, twice).
   Enumerating 29 endpoints took ~3.8 s and froze Set Default. Always read names via `EndpointPropertyReader`.
2. **Don't create a second `[ComImport]` class for a CLSID NAudio already declares** (MMDeviceEnumerator): the RCW
   comes back as NAudio's type and the cast throws. Create by CLSID (`Type.GetTypeFromCLSID` + `Activator`).
3. **Core Audio objects can't cross COM apartments**: an `IMMDevice` obtained on the WPF UI (STA) thread fails with
   E_NOINTERFACE when used from a pool thread. `LevelMeterService` creates and uses everything on its own MTA thread.
   Getting a device *via the enumerator on the worker thread* is fine (the enumerator is free-threaded).
4. **WPF DataGrid cell bindings join the row's BindingGroup, making the default UpdateSourceTrigger Explicit** — a
   TwoWay slider/toggle in a cell never writes back. Use `UpdateSourceTrigger=PropertyChanged`.
5. **An input endpoint's peak meter reads 0 unless something is recording from it.** But opening a headset mic puts
   Bluetooth headsets/dongles into call (hands-free) mode, degrading playback. So only the *selected* input gets a
   silent capture stream (`SetCaptureStreamDevice`); a mic test opens its own. Never hold all mics open.
6. **Shared-mode WASAPI rejects a stream whose channel count differs from the endpoint mix format** (E_INVALIDARG,
   "Value does not fall within the expected range") — e.g. the 4-ch Intel array played on stereo speakers. Always
   render/convert to the output's `AudioClient.MixFormat` (see `AudioTestService.ConvertToMixFormat`, `TestMusic`).
7. **One default-device change fires several IMMNotificationClient callbacks** — refreshes are debounced (150 ms).
8. `IPolicyConfig` string params must be `[MarshalAs(UnmanagedType.LPWStr)]`.
9. Linear peak meters understate levels badly (−30 dBFS speech = 3 % of a bar); all meters use `LevelScale`
   (−60…0 dBFS). The property store is only read while the property panel is open (it's the expensive part of
   selection).
10. WPF projects' implicit usings exclude `System.IO`; `Timer` is ambiguous (System.Timers vs System.Threading).
11. **The UI uses a flat dark theme** (`Views/Theme.xaml`: list, buttons, checkboxes, meters, strip toggle). The DataGrid coerces
    `DataGridRow.Background` to its `RowBackground`, so row-level hover/selection triggers do nothing — those colours
    are drawn in the `ListCell` template instead. Setting `CellStyle` doesn't remove the theme style's triggers, so
    pin `Foreground` or the selected row's text turns near-black ("inactive selection" colour). Thin icon glyphs get
    ClearType colour fringes on dark backgrounds; the list uses `TextOptions.TextRenderingMode="Grayscale"`.
12. **NAudio's `WaveFormat.Serialize` prepends a 4-byte length**, so in its output the WAVEFORMATEXTENSIBLE
    `dwChannelMask` is at offset 24, not 20 (see `ChannelLayout.GetChannelMask`).
13. Per-channel tests (`ChannelSegmentsSampleProvider`) report the sounding channel from the audio thread via
    `AudioTestSession.SetChannel` — use `SetChannel`, not `BeginPhase`, for that: `BeginPhase` swaps the stop token,
    so a Stop click would no longer reach the playback loop that is waiting on the original token.
14. **Audio sessions** (`AudioSessionService`): don't re-enumerate (`RefreshSessions`) inside NAudio's
    `OnSessionCreated` callback — it re-registers the notification from within it; the callback only requests a
    rescan. The app's own streams (the selected mic's meter capture, tests) appear as sessions of its own PID —
    exclude them from "in use" or every selected mic looks busy. Capture-session peaks are real but tiny in a quiet
    room (−50…−80 dBFS), so the −60 dB meters can look dead; check the dB tooltip before suspecting a bug.
15. **Raw / call-mode capture** (`ModeCapture`, `Interop/AudioClient2.cs`): NAudio 2.2.1 can't set
    `AudioClientProperties`, hence our own IAudioClient2 interop. An `[ComImport]` enumerator created by CLSID
    returns whatever RCW already exists for that singleton — if *ours* is created first, a later `new
    MMDeviceEnumerator()` (NAudio) throws InvalidCastException; the app is fine because NAudio's enumerators are
    created at startup and kept, but a harness must create NAudio's first. The mix format comes back as
    WAVE_FORMAT_EXTENSIBLE, which NAudio's `ToSampleProvider` rejects — use `ToStandardWaveFormat()`. Several
    modes can capture from one mic at the same time (verified on the Intel array). On that array the processed
    paths (Standard and Call: echo cancellation + noise suppression) output one signal copied to all 4 channels,
    ~25 dB below the raw room noise — Raw is the only way to hear the individual capsules.

## Verifying changes (techniques that worked)

- **UI Automation from PowerShell** (`UIAutomationClient`): find the DataGrid's `DataItem` rows by name, `Select()`
  rows, `Invoke()` buttons, `Toggle()` checkboxes/toggle buttons, read `RangeValuePattern` (sliders, level bars) and
  `HelpText` (tooltips). DataGrid virtualises rows, so only on-screen rows are visible to UIA.
- **Screenshots**: `PrintWindow(hwnd, hdc, 2)` via a small Add-Type helper works even when the window is covered;
  `CopyFromScreen` returns black when the screen is locked, and WPF windows capture as plain white while the display
  is asleep (maintainer idle for minutes) — don't mistake that for a rendering bug. Re-declare the helper type in each new PowerShell process
  (or keep a script file in the session scratchpad).
- **Scratch harness projects** (in the session scratchpad, never in the repo) referencing NAudio to measure things
  directly: endpoint volume/mute readback, per-device active audio sessions of the app's PID (to prove which mics are
  open), timing of COM calls, FFT band energy of `TestMusic`, silent playback with `WasapiOut.Volume = 0`.

## Open items / ideas (not started unless the maintainer asks)

The roadmap and smaller open items live in `TODO.md` (N3 bandwidth analysis and N5 diagnostic report are fully
specified there; N3/N5 still need the maintainer's go-ahead). Things only an agent needs to know:

- Unconfirmed observations: the Jabra Link 390 mic read 0 / −∞ in every automated check (likely headset muted or
  boom up — never confirmed); the test music read 1.0 peak on the Realtek output meter (driver enhancements?).
  The Intel array's "channels 2 and 4 below −60 dB" is explained by N4: the processed paths output one
  noise-suppressed signal on all four channels, far below the raw room noise.
- N1's call-mode hint keys on form factor 5/6, but the Jabra Link 390 mic reports 4 (Microphone) — see TODO.
- ~185 undocumented endpoint property keys still show as raw `{fmtid},pid`; add to `PropertyKeyNames.ManualNames`
  only with a confirmed meaning.
- The maintainer asked for explanations, not code, on the headset call-mode ideas (media-vs-call listening test,
  disabling a headset mic, communications ducking) — they're listed in TODO under "Other ideas".
- Not started: license, automated tests, CI, releases, code signing.
