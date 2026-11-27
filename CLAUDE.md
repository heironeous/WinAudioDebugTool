# CLAUDE.md — notes for AI coding agents (and curious contributors)

This file is read automatically by [Claude Code](https://claude.com/claude-code) and is written for AI coding agents
working on this repo. Human visitors: start with [README.md](README.md); the *Hard-won lessons* below are worth
reading too before changing services or interop.

Agents: read this first, then `README.md` (features, build, layout, regenerating property names), `TODO.md` (open
items and roadmap; finished items are in `planning/DONE-TODO.md`) and `docs/design-notes.md` (how the larger
features were built and verified). This file records **where the project stands, how the maintainer likes to work,
and the non-obvious things learned the hard way**. Keep it current: when you finish a piece of work, update "Current state" and "Open items".

## What this is

**Audio Device Debugger** — a Windows WPF tool (C# / .NET 10 LTS, NAudio 2.2.1 + raw Core Audio COM interop) for
inspecting and driving audio endpoints: list every input/output (incl. disabled / not-present / unplugged), set the
default / communications / both roles, per-device volume + mute, live dB level meters, test playback (generated music)
and mic record-and-playback tests, per-channel tests, an event timeline, the apps using each device, raw vs
call-processed mic recording (A/B), and a full property-store dump with human-readable key names.

## Current state (as of v0.4.1 + Bluetooth connect, 2026-11-26)

- Public on GitHub: <https://github.com/heironeous/WinAudioDebugTool>, branch `main` (work is committed straight to
  `main`, or on a feature branch merged by pull request when the maintainer asks for one, as for the visual refresh).
  The maintainer usually pushes; agents push only when asked to (e.g. "commit and push to main").
- Builds cleanly (0 warnings) and runs on Windows 11; every feature in `planning/DONE-TODO.md` was exercised on
  real hardware during development (the maintainer tests manually; see "Verifying changes"). No license file yet —
  the maintainer's decision. Unit tests (xUnit) in `tests/AudioDeviceDebugger.Tests` cover the hardware-free logic;
  run `dotnet test` from the repo root (CI runs them too). Add tests with new pure logic.
- CI: `.github/workflows/build-and-tag.yml` runs the unit tests and publishes every push to `main` (windows-latest, .NET 10) as the
  single-file self-contained exe (publish profile `win-x64-selfcontained`, version stamped in) and, on success,
  pushes an annotated tag `vX.Y.Z` and creates a GitHub Release for it with `AudioDeviceDebugger-vX.Y.Z-win-x64.exe`
  + `.sha256` attached (`gh release create --generate-notes`; a re-run of a tagged commit re-uploads the files). The bump comes from the commit subjects since the last tag
  (`.github/scripts/next-version.sh`): `type!:` / `BREAKING CHANGE` → major, `feat:` → minor, anything else → patch.
  Documentation-only pushes (`**.md`, `docs/**` — `paths-ignore`) don't run the workflow and get no tag.
  So **the commit type matters** — use `feat:` only for user-visible features. Don't create version tags by hand
  and don't edit `<Version>` in the `.csproj` to release (it is only the first tag's version and the local-build
  version). `.gitattributes` keeps `*.sh` LF — bash on the Windows runner fails on CRLF. Pull requests into `main`
  run the same tests and build, but the tag and release steps are skipped for them.
- Docs: `README.md` is for users (features, requirements, build, caveats) — update it when a user-visible feature
  changes; `TODO.md` holds the open items + roadmap (N3, N5 specified) and `planning/DONE-TODO.md` the finished
  items (move an item there once it's done); `docs/design-notes.md` holds implementation and verification notes for
  N1, N2, N4, the side-panel tabs and the visual refresh.
- **Visual refresh** (2026-11-25): merged into `main` from `feature/visual-refresh` (PR #1, released as v0.3.0); its
  items are in `planning/DONE-TODO.md`. CI only runs on `main`, so feature branches get no CI build.
- **Tests, Driver tab, grouping, themes** (2026-11-26): merged into `main` from
  `feature/tests-inventory-grouping-theme` (PR #2, released as v0.4.0): unit tests + CI test step (which also runs on
  pull requests into `main`, without tag or release), the Driver tab (device inventory), grouping by device, and the
  light / high-contrast themes. Their items are still ticked in place in `TODO.md` ("Ideas from the 2026-11-23
  brainstorm"); they have not been moved to `planning/DONE-TODO.md` yet.
- v0.4.1 (2026-11-26): the grouping chip names the grouping a click switches to ("By device" / "By input / output",
  style `SwitchChip`, no checked highlight), and the Raw recording tab is disabled while an output is selected
  (`MainViewModel.CanOpenRecordingsPanel`; selecting an output closes the panel).
- Bluetooth **Connect / Disconnect** toolbar button (2026-11-26, `feat:`): see docs/design-notes.md. Verified by the
  maintainer on the Jabra Evolve3 75 over Bluetooth Classic; LE Audio untried (open item in `TODO.md`).
- **Disable / Enable** toolbar button (2026-11-26): `IPolicyConfig::SetEndpointVisibility` on the selected endpoint;
  no admin needed; Windows moves a disabled default away and back on Enable. A USB dongle's mic is untried.
- **Row colours by state** (2026-11-27): `DeviceRowViewModel.RowState` drives a soft red outline (muted), a red
  outline + tint (disabled) and a yellow tint (unplugged); not present keeps only the inactive-row dimming. Tints are
  in `ListCell`, the outline in `ListRow`'s template (a copy of DataGridRow's default plus an overlay border). Seen on
  the desktop in dark; muted, light and high contrast not yet seen.
- The maintainer's hardware (laptop): Jabra Link 390 USB dongle (headset speakers + mic; not plugged in on
  2026-11-26), Realtek laptop speakers, Intel Smart Sound Technology 4-channel mic array, an HP 325 FHD webcam mic,
  plus many NotPresent endpoints from old USB headsets/dongles (Jabra Link 380, Poly BT700, Poly Voyager Focus 2, HP
  dock). Through a dongle the A2DP/HFP ("call mode") switch happens inside the dongle, invisible to Windows. Since
  2026-11-26 the laptop also has the **Jabra Evolve3 75 paired directly over Bluetooth Classic**: a "Headphones"
  endpoint (A2DP, `BTHENUM\…`, form factor 3) and a "Headset … Hands-Free" output + mic (`BTHHFENUM\…`, form factor 5),
  all in one container; Unplugged while the headset is off.
  A second machine (desktop) is also used. It has Realtek USB Audio (only its digital output is active), an ASUS
  VP28U monitor on NVIDIA HDMI audio and a Logitech C922 webcam mic. Its Jabra Evolve3 75, Link 370 and Link 380 are
  not present as USB devices; since 2026-11-25 the Evolve3 75 also shows up paired directly over Bluetooth LE Audio
  (`APXENUM\…` endpoints under `BTHLE\…`, Unplugged while off). Check which machine you are on (`Get-PnpDevice -Class AudioEndpoint`) before assuming hardware.

## Environment & commands

- The app targets .NET 10 (LTS) since 2026-11-23; .NET 8 support ended 2026-11-10. Builds need the .NET 10 SDK
  (10.0.404, installed via winget — on the desktop 2026-11-23, on the laptop 2026-11-26). Older SDKs (3.1 and 8.0.428
  on the desktop, 8.0.428 on the laptop) are harmless; the newest wins.
  `dotnet` may not be on PATH in a fresh shell — use `"C:\Program Files\dotnet\dotnet.exe"`.
- No Windows SDK is installed (no C++ headers); property-key names come from the Win32 metadata NuGet package instead.
- Shells: PowerShell is primary; Git Bash is also available (and handy for heredocs / python one-off edits).

```powershell
cd src\AudioDeviceDebugger
dotnet build -c Release                                   # output: bin\Release\net10.0-windows\win-x64\AudioDeviceDebugger.exe
Start-Process bin\Release\net10.0-windows\win-x64\AudioDeviceDebugger.exe
dotnet run --project tools/PropertyKeyNameGenerator       # (from repo root) regenerate Interop/PropertyKeyNames.g.cs
dotnet test                                               # (from repo root) unit tests
```

The running app locks its exe: stop it before rebuilding (`Get-Process AudioDeviceDebugger | Stop-Process`).

## How the maintainer works (follow this)

- Requests are small and iterative; the maintainer tests each change by hand in the running app. After a change:
  build, **relaunch the app**, verify what you can, report briefly, and say what you could *not* verify.
- **Commit only when the maintainer says "commit"** (they always ask explicitly, in a separate message). One commit per
  feature/fix, conventional-commit subject (`feat:`, `fix:`, `perf:`), a body explaining *why* (root cause for fixes),
  ending with the `Co-Authored-By:` trailer given in the session's system prompt. Push only when asked. Keep `TODO.md` /
  `planning/DONE-TODO.md` (and `README.md` / `docs/design-notes.md` where relevant) updated in the same commit as the
  feature. Never commit `bin/` or `obj/` output, and keep scratch harnesses out of the repo.
- When the maintainer asks to *explain first* (e.g. the call-mode question), explain and propose options — don't build.
- Be careful with side effects on their real audio setup: don't switch default devices, change volumes, unmute, or
  play audible sound just to test something. Prefer silent checks (stream volume 0, muted devices, reading state via
  UI Automation). If a check must be audible, keep it short and say so. Restore anything you change.
- The maintainer may be clicking in the app while you run UI Automation; if results look inconsistent, re-check once
  before concluding there's a bug.

## Architecture map

`src/AudioDeviceDebugger/` (MVVM, no DI container — services are new'd up in `Views/MainWindow.xaml.cs`):

- `Interop/` — `IPolicyConfig` (undocumented default-endpoint setter), `EndpointPropertyReader` (fast keyed property
  reads), `PropertyKeyNames.cs` (+ generated `.g.cs`: ~1,550 SDK PKEY/DEVPKEY names; `ManualNames` / `Undocumented`
  for confirmed undocumented keys), `KnownGuidNames.cs` (+ generated `.g.cs`: 264 audio GUID constants).
- `Services/` — `DeviceEnumerationService` (enumeration + IMMNotificationClient), `DefaultDeviceService`,
  `DeviceDetailsService` (formats; `GetProperties` separately, values made readable by `PropertyValueFormatter`), `EndpointVolumeService` (volume/mute + external-change
  notifications), `LevelMeterService` (single MTA polling thread), `AudioTestService` + `AudioTestSession` (phased
  tests with Stop), `TestMusic` (procedural test clip), `ChannelMatchingSampleProvider`, `EventLogExporter`
  (CSV / Markdown / JSON Lines / text), `AudioSessionService` (per-endpoint app sessions — "used by" — polled
  every 500 ms on its own MTA thread, plus session peaks for the selected device), `ModeCapture` (one capture
  stream in a chosen processing mode — Standard / Call (communications category) / Raw — via our own
  `Interop/AudioClient2.cs` IAudioClient2 interop, plus the stream's effects from IAudioEffectsManager),
  `DeviceInventoryService` (Driver tab: device tree via `Interop/DeviceNode.cs` (cfgmgr32), USB speed via
  `Interop/UsbHub.cs`, APOs from the endpoint's `FxProperties` registry key; pure helpers in `InventoryFormat`),
  `BluetoothConnectionService` (Connect / Disconnect via `Interop/BluetoothAudioControl.cs`: device topology →
  `IKsControl` → `KSPROPSETID_BtAudio` one-shot reconnect / disconnect); the test
  service's A/B recording runs three of them at once. `DeviceEnumerationService.EndpointNotified` carries each
  IMMNotificationClient callback in detail (for the event log) alongside the coarse `DevicesChanged`.
- `ViewModels/` — `MainViewModel` (refresh/debounce, filters, grouping, selection, tests, side panels, event logging),
  `DeviceRowViewModel`, `DeviceListGroup` (a list group; cached per key, carries the collapsed state), `ChannelPeakViewModel`, `EventLogViewModel` (event timeline ring buffer + filters + merging),
  `SessionRowViewModel` (PROCESSES tab), `TakeRowViewModel` (RAW RECORDING tab: last take per processing mode). `Models/` — plain data + `LevelScale` (dB conversion).
- `Views/` — `MainWindow` (toolbar 1 line, filter chips on the right; device grid top, grouped into OUTPUTS / INPUTS
  or by physical device (`Services/DeviceGrouping.cs`, the "By device" chip in the DEVICE header);
  details bottom: header card + `LevelGraph` + channel bars left, collapsible right-hand side panel showing one of four
  horizontal tabs — Properties, Events, Processes (apps using the device), Raw recording (A/B takes), Driver), `LevelGraph`,
  `Theme.xaml` (type scale and all control styles; merged in `App.xaml`), `Themes/Dark.xaml` + `Themes/Light.xaml`
  (the colour palettes), `ThemeManager` (picks dark / light / high contrast from Windows, follows changes, `--theme=`).
- `tests/AudioDeviceDebugger.Tests/` — xUnit tests of the hardware-free logic. It references the app (a self-contained
  exe, hence `ValidateExecutableReferencesMatchSelfContained=false`; internals via `InternalsVisibleTo`), sets the
  invariant culture for every test (`TestCulture.cs`), and uses `UseWPF` for the event log's CollectionView.
- `tools/PropertyKeyNameGenerator/` — reads `Windows.Win32.winmd` from Microsoft.Windows.SDK.Win32Metadata and writes
  both generated tables (property keys; audio GUID constants).

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
11. **The UI uses a flat theme in dark, light and high contrast** (styles and sizes in `Views/Theme.xaml`, colours in
    `Views/Themes/*.xaml`). Use the named colours and sizes, never literals, and refer to palette colours with
    **`DynamicResource`** (a `StaticResource` keeps the startup palette when Windows switches); a new palette key goes
    into Dark.xaml, Light.xaml, `ThemeManager.HighContrastPalette` and `ThemeManager.PaletteKeys` (a test checks the
    XAML ones). Code that reads palette brushes must listen to `ThemeManager.ThemeChanged`. The DataGrid coerces
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
16. **Property names and values** (`PropertyValueFormatter`, `KnownGuidNames`): in the Win32 metadata most GUID
    constants (KSNODETYPE_*, AUDIO_SIGNALPROCESSINGMODE_*, AUDIO_EFFECT_TYPE_*, …) are *empty structs* carrying a
    GuidAttribute (DEFINE_GUIDSTRUCT), not fields — scan type definitions too, or they look absent. None of the ~180
    undocumented endpoint keys are in the Windows property schema (`PSGetNameFromPropertyKey`) or in public docs; the
    reliable way to confirm one is to compare its value with `Get-PnpDeviceProperty` on the endpoint
    (`SWD\MMDEVAPI\<endpoint ID>`) and on its audio device. Many keys are *snapshots* taken when the endpoint was
    registered (device name, driver version), so a few mismatches can be stale data rather than a wrong guess. Value
    decoders must be strict: loose "UTF-16 text" detection turned binary blobs into CJK-looking garbage.
17. **Container IDs** (`DEVPKEY_Device_ContainerId`) group the endpoints of one physical device, but every built-in
    device shares `{00000000-0000-0000-ffff-ffffffffffff}` ("this PC"). Ignore that container when you pair a mic with
    an output, or a laptop mic pairs with the headphone jack. Form factors don't help: the Jabra Link 390 reports
    "Speakers" (1) and "Microphone" (4), and so does the Evolve3 75 over LE Audio (desktop), while over Bluetooth
    Classic HFP (laptop) the same headset reports "Headset" (5). A container match alone can't tell a Bluetooth
    dongle from a wired USB headset either: the HP dock's headset jack also has Headphones (3) + Microphone (4) in
    one USB container.
18. **A grouped DataGrid stops recomputing star column widths** when its viewport changes (window resize, scrollbar shown
    or hidden), which clips the last columns: `DeviceGrid_ScrollChanged` sets the DEVICE column's width instead.
19. **A `ContentPresenter`'s own DataContext is its Content**: style triggers on it that bind to the outer view model
    silently get the fallback value. Use a `ContentControl` when it needs triggers.
20. **Device containers can't be named through DevQuery here**: `DevGetObjectProperties` on a container took 60 s per
    call and failed (desktop, 2026-11-25). Group names come from the endpoints (`DeviceGrouping.DeviceNameOf`).
21. **A GroupItem only exposes expand / collapse to UI Automation when its template holds an `Expander`**; a plain
    ToggleButton heading is invisible to UIA and screen readers.
22. **`Refresh()` rebuilds the rows (`Devices.Clear()`), and the DataGrid pushes a null selection into
    `SelectedDevice` meanwhile.** Logic that reacts to "the user picked another device" must ignore changes while
    `_isRebuildingRows` is set (the Disable button's pinned row was lost this way).

## Verifying changes (techniques that worked)

- **UI Automation from PowerShell** (`UIAutomationClient`): find the DataGrid's `DataItem` rows by name, `Select()`
  rows, `Invoke()` buttons, `Toggle()` checkboxes/toggle buttons, read `RangeValuePattern` (sliders, level bars) and
  `HelpText` (tooltips). DataGrid virtualises rows, so only on-screen rows are visible to UIA. PowerShell variable
  names ignore case: `$t = …` overwrites a `$T = [TreeScope]` alias, so name such aliases distinctly (`$Scope`).
  Selecting a headset *mic* row opens it (lesson 5) and can switch the headset into call mode — select the Intel
  array or an output when a test only needs "some input".
- **Screenshots**: `PrintWindow(hwnd, hdc, 2)` via a small Add-Type helper works even when the window is covered;
  `CopyFromScreen` returns black when the screen is locked, and WPF windows capture as plain white while the display
  is asleep (maintainer idle for minutes) — don't mistake that for a rendering bug. Re-declare the helper type in each new PowerShell process
  (or keep a script file in the session scratchpad).
- **Meters with a signal but no sound**: a *muted* output's peak meter still shows what is played on it, so Test on a
  muted device moves every meter silently. Check the mute state right before and after.
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
- N1's call-mode hint covers form factor 4 mics through container IDs since 2026-11-23, but that was only checked on a
  not-present Jabra Evolve3 75. Still to check with the Jabra Link 390 in a real call. Expect it to fire for wired
  USB headsets and docks too (see lesson 17).
- ~170 undocumented endpoint property keys still show as raw `{fmtid},pid` (most belong to driver/APO-private sets
  such as `{f112024a-…}` and `{5510c7ab-…}`); add to `PropertyKeyNames.Undocumented` only with a confirmed meaning —
  see lesson 16 and docs/design-notes.md.
- The maintainer asked for explanations, not code, on the headset call-mode ideas (media-vs-call listening test,
  disabling a headset mic, communications ducking) — they're listed in TODO under "Other ideas".
- Bluetooth Connect / Disconnect: don't press it in automated checks without asking — it drops the maintainer's
  headset (often their default device). Check it silently by its label, enabled state and tooltip.
- Disable / Enable: test only on endpoints the maintainer agreed to (the Realtek speakers and Intel array, never the
  Jabra), and have the script check the *selected row* before every click — a blind second click once disabled the
  Jabra Headphones.
- Not started: license, code signing (releases are unsigned, so SmartScreen warns).
