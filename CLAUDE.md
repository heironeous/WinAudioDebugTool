# CLAUDE.md — handoff notes for future sessions and agents

Read this first, then `README.md` (layout, build, regenerating property names) and `TODO.md` (feature checklist).
This file records **where the project stands, how the owner likes to work, and the non-obvious things learned the
hard way**. Keep it current: when you finish a piece of work, update the "Current state" and "Open items" sections.

## What this is

**Audio Device Debugger** — a Windows WPF tool (C# / .NET 8, NAudio 2.2.1 + raw Core Audio COM interop) for
inspecting and driving audio endpoints: list every input/output (incl. disabled / not-present / unplugged), set the
default / communications / both roles, per-device volume + mute, live dB level meters, test playback (generated music)
and mic record-and-playback tests, and a full property-store dump with human-readable key names.

## Current state (as of commit `3f5fa49`, 2026-11-10)

- Builds cleanly (0 warnings) and runs on Windows 11; every feature in `TODO.md` that is checked was exercised on
  real hardware during development (the owner tests manually; see "Verifying changes").
- Branch `master` (the repo's default/PR branch is `main`, but all work so far has been committed to `master`).
  Nothing has been pushed. Working tree was clean at handoff.
- The owner's hardware: Jabra Link 390 USB dongle (headset speakers + mic), Realtek laptop speakers, Intel Smart
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

## How the owner works (follow this)

- Requests are small and iterative; the owner tests each change by hand in the running app. After a change:
  build, **relaunch the app**, verify what you can, report briefly, and say what you could *not* verify.
- **Commit only when the owner says "commit"** (they always ask explicitly, in a separate message). One commit per
  feature/fix, conventional-commit subject (`feat:`, `fix:`, `perf:`), a body explaining *why* (root cause for fixes),
  ending with the `Co-Authored-By:` trailer given in the session's system prompt. Never push. Keep `TODO.md` updated
  in the same commit as the feature.
- When the owner asks to *explain first* (e.g. the call-mode question), explain and propose options — don't build.
- Be careful with side effects on their real audio setup: don't switch default devices, change volumes, unmute, or
  play audible sound just to test something. Prefer silent checks (stream volume 0, muted devices, reading state via
  UI Automation). If a check must be audible, keep it short and say so. Restore anything you change.
- The owner may be clicking in the app while you run UI Automation; if results look inconsistent, re-check once
  before concluding there's a bug.

## Architecture map

`src/AudioDeviceDebugger/` (MVVM, no DI container — services are new'd up in `Views/MainWindow.xaml.cs`):

- `Interop/` — `IPolicyConfig` (undocumented default-endpoint setter), `EndpointPropertyReader` (fast keyed property
  reads), `PropertyKeyNames.cs` (+ generated `.g.cs`: ~1,550 SDK PKEY/DEVPKEY names; `ManualNames` for confirmed
  undocumented keys).
- `Services/` — `DeviceEnumerationService` (enumeration + IMMNotificationClient), `DefaultDeviceService`,
  `DeviceDetailsService` (formats; `GetProperties` separately), `EndpointVolumeService` (volume/mute + external-change
  notifications), `LevelMeterService` (single MTA polling thread), `AudioTestService` + `AudioTestSession` (phased
  tests with Stop), `TestMusic` (procedural test clip), `ChannelMatchingSampleProvider`.
- `ViewModels/` — `MainViewModel` (refresh/debounce, filters, selection, tests, properties panel),
  `DeviceRowViewModel`, `ChannelPeakViewModel`. `Models/` — plain data + `LevelScale` (dB conversion).
- `Views/` — `MainWindow` (toolbar 2 lines; device grid top; details bottom: info + `LevelGraph` + channel bars left,
  collapsible property table right), `LevelGraph`.
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
11. **The device list is restyled** (`Views/DeviceListStyles.xaml`, flat dark theme). The DataGrid coerces
    `DataGridRow.Background` to its `RowBackground`, so row-level hover/selection triggers do nothing — those colours
    are drawn in the `ListCell` template instead. Setting `CellStyle` doesn't remove the theme style's triggers, so
    pin `Foreground` or the selected row's text turns near-black ("inactive selection" colour). Thin icon glyphs get
    ClearType colour fringes on dark backgrounds; the list uses `TextOptions.TextRenderingMode="Grayscale"`.

## Verifying changes (techniques that worked)

- **UI Automation from PowerShell** (`UIAutomationClient`): find the DataGrid's `DataItem` rows by name, `Select()`
  rows, `Invoke()` buttons, `Toggle()` checkboxes/toggle buttons, read `RangeValuePattern` (sliders, level bars) and
  `HelpText` (tooltips). DataGrid virtualises rows, so only on-screen rows are visible to UIA.
- **Screenshots**: `PrintWindow(hwnd, hdc, 2)` via a small Add-Type helper works even when the window is covered;
  `CopyFromScreen` returns black when the screen is locked. Re-declare the helper type in each new PowerShell process
  (or keep a script file in the session scratchpad).
- **Scratch harness projects** (in the session scratchpad, never in the repo) referencing NAudio to measure things
  directly: endpoint volume/mute readback, per-device active audio sessions of the app's PID (to prove which mics are
  open), timing of COM calls, FFT band energy of `TestMusic`, silent playback with `WasapiOut.Volume = 0`.

## Open items / ideas (not started unless the owner asks)

- From the call-mode discussion: an A/B "media vs call mode" listening test for headsets (pair speaker+mic by
  `DEVPKEY_Device_ContainerId`); disabling a headset's mic endpoint (`IPolicyConfig::SetEndpointVisibility` — check
  whether it needs elevation); a toggle for Windows "communications ducking". The owner asked for explanations first;
  only the test music and mic-open policy were built.
- Offered, not decided: close the selected mic's capture stream when the app window is inactive/minimised.
- Unconfirmed observations: Jabra Link 390 mic read 0 / −∞ in every automated check (likely headset muted/boom up —
  never confirmed by the owner); the test music read 1.0 peak on the Realtek output meter (driver enhancements?);
  the Intel array's channels 2 and 4 read below −60 dB while 1 and 3 show room noise.
- Cosmetic: the Role column truncates "Default + Comms" to "Default + Comm" (column width 95).
- ~185 undocumented endpoint property keys still show as raw `{fmtid},pid` (e.g. `{b3f8fa53-0004-438e-9003-51a46e139bfc},*`);
  add to `PropertyKeyNames.ManualNames` only with a confirmed meaning. Property *values* (e.g. form factor 3) are not
  decoded to names yet.
- **Stale docs**: README's "Building"/"Testing" sections and TODO's "Not yet done" still say the code was written in a
  Linux container and never built/verified — no longer true; update them when next touching the docs.
- Not started: automated tests, code signing, a CI build.
