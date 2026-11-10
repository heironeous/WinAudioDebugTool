# TODO — Windows Audio Device Debugger

Feature checklist. Boxes are checked once the corresponding code is written and
committed. Because development happens inside a Linux container, "checked"
means **implemented and committed**, not "verified on real hardware" — see
`README.md` for the manual Windows test pass this still needs.

## Project scaffolding
- [x] Git repository initialized (`Aral Kaymaz` / `aralcan@hotmail.com`)
- [x] `.gitignore` for .NET build artifacts
- [x] Solution + WPF project scaffolding (`.sln`, `.csproj`)
- [x] NAudio dependency wired up for Core Audio access
- [x] `README.md` describing layout, build, run, and test instructions

## Device enumeration
- [x] Enumerate all active output (render) devices
- [x] Enumerate all active input (capture) devices
- [x] Include disabled / unplugged / not-present devices (with state shown)
- [x] "Refresh" button that re-enumerates devices and diffs against current list
- [x] Toolbar "Show:" checkboxes, one per non-active state — Disabled,
      Not present, Unplugged — each with its device count (Disabled and
      Not present hidden by default, Unplugged shown; Active always shown)
- [x] Auto-refresh on Windows device change notifications (plug/unplug events)

- [x] Flat, dark device list (`Views/Theme.xaml`): name + "Output ·
      Active" subtitle, quiet uppercase headers, hairline row separators,
      subtle hover/selection, dimmed inactive rows, thin meter/slider, flat
      mute and Test buttons; no images or shadows
- [x] Same flat theme for the toolbar buttons, Show: checkboxes, the
      PROPERTIES strip toggle, the level graph panel and the per-channel bars

## Default device roles
- [x] Show current default device for Render / Multimedia role
- [x] Show current default device for Render / Communications role
- [x] Show current default device for Capture / Multimedia role
- [x] Show current default device for Capture / Communications role
- [x] "Set Default" toolbar button for the selected device (Console + Multimedia roles)
- [x] "Set Comms" toolbar button for the selected device (Communications role)
- [x] "Set Both" toolbar button (Default + Communications in one click);
      all three are disabled unless the selected device is Active
- [x] Visual indicator in the device list for current defaults ("Role" column badge)

## Device details panel
- [x] Friendly name, endpoint ID, data flow, device state
- [x] Default format: sample rate, bit depth, channel count (shared-mode mix format)
- [x] Shared-mode mix format vs. exclusive-mode supported formats
- [x] Raw property store dump (all discoverable `PROPERTYKEY` values, via `device.Properties`)
- [x] Details panel rendering in the UI (name, ID, formats, property grid)
- [x] Layout: device list on top, details below (resizable); property table
      collapses to a strip at the right edge (collapsed by default) and the
      property store is only read while the table is open
- [x] Human-readable property names: compiled-in table of all ~1,550 documented
      PKEY_/DEVPKEY_ keys generated from Win32 metadata
      (`tools/PropertyKeyNameGenerator` → `Interop/PropertyKeyNames.g.cs`) plus a
      manual table for confirmed undocumented keys; unknown keys keep their UID
- [ ] Name more undocumented endpoint keys (~185 distinct on the dev machine,
      e.g. `{b3f8fa53-0004-438e-9003-51a46e139bfc},*`) once their meaning is confirmed

## Testing devices
- [x] "Test" button per device row — plays a short generated music clip (TestMusic: drums, bass, chords, melody, bells; full-band, panned) for outputs, records 5s
      and plays it back for inputs
- [x] Start/stop toggle on the Test button (▶ / ■): stopping an output test
      ends the music; stopping an input test ends recording early and still
      plays the capture back (stop again to cut the playback)
- [x] Input tests show their phase on the button: "Test ■" while recording,
      "Playback ■" while playing the recording back
- [x] Test audio is converted to the output's shared-mode mix format (channel
      count + sample rate), so e.g. a 4-channel mic array plays back on stereo
      speakers (previously failed with E_INVALIDARG)
- [x] Clear pass/fail + error reporting if a device fails to open/start
      (shown in the bottom status bar)

## Volume control
- [x] Per-device volume slider (0–100%) + mute toggle in every active row,
      via `IAudioEndpointVolume` (`EndpointVolumeService`) — same setting as
      Windows' volume flyout; mic level for inputs
- [x] Stays in sync with external changes (flyout, media keys, other apps)
      via `OnVolumeNotification`; own changes are tagged with an event-context
      GUID so they don't echo back into the slider while dragging

## Per-channel testing
- [x] Channel strip in the details panel names each channel from the mix
      format's speaker mask (FL, FR, C, LFE, …; inputs "Ch n") with a live
      meter, dB readout and its own play button (`ChannelLayout`)
- [x] Outputs: test music on one channel only (others silent; LFE gets a
      120 Hz low-passed version), or "Cycle all" — a 1.5 s excerpt per
      channel in turn, with the sounding channel highlighted
- [x] Inputs: the last mic-test recording is kept per device; play any one
      channel (mono, on every speaker of the default output) or cycle
      through all of them; buttons disabled until a recording exists
- [ ] Optional: "raw" capture (IAudioClient2 stream options) to bypass mic-array
      processing and expose the individual capsules

## Live level metering
- [x] Real-time peak-level meter per device using `IAudioMeterInformation`
      — `LevelMeterService`. All Core Audio objects live on one dedicated
      MTA polling thread (IMMDevice can't cross from the UI STA: E_NOINTERFACE).
      Windows only feeds an input's meter while something records from it,
      so the *selected* input (only) gets a silent shared-mode capture
      stream; it closes when the selection moves. Opening a headset mic
      switches it into call/hands-free mode, so no mic is held open otherwise
      (a running mic test keeps its own capture open while recording)
- [x] Rolling graph (time-series) for the selected device — `LevelGraph`
      control in the detail panel
- [x] Simultaneous mini-meters next to every device in the list — a live
      `ProgressBar` per row, all updating independently and concurrently
- [x] Per-channel peak levels where the endpoint reports more than stereo
      (`SelectedDeviceChannelPeaks` in `MainViewModel`, rendered as a row of
      vertical bars below the rolling graph in the detail panel)

- [x] All meters (row bars, level graph, per-channel bars) on a dB scale,
      −60…0 dBFS (`Models/LevelScale`), with dBFS tooltips, a live readout
      and −12/−24/−36/−48 dB gridlines on the graph

## Packaging
- [x] `dotnet publish` profile for a single self-contained `.exe`
      (`Properties/PublishProfiles/win-x64-selfcontained.pubxml`)
- [x] App icon / metadata (version, product name) — `Assets/app.ico` +
      `app.manifest` (DPI awareness, OS compatibility, no elevation) wired
      up via `ApplicationIcon`/`ApplicationManifest` in the `.csproj`

## Next steps — planned, awaiting approval

Five features chosen from the suggestions list (2026-11-10). **Nothing below is
started; each needs the owner's go-ahead before implementation.** Suggested
order: N1 → N2 → N3 → N4 → N5. N1 feeds N2 and N5, and N3 feeds N5. The report
(N5) comes last because it collects everything else. Each feature is its own
commit, with this file updated in that commit. Every item ends with
"Decisions needed": questions to settle at approval time.

### N1. "Who's using this device" — audio sessions per endpoint
Goal: show which apps have a stream open on each device, especially the
headset mic. An app opening the mic is what switches a USB dongle (Jabra Link
390, Poly BT700) into call mode, so this answers "why does my headset suddenly
sound bad?" and "who is holding my mic?".

- [ ] **Service** `Services/AudioSessionService.cs`
      (+ `IAudioSessionService`), with models `Models/AudioSessionInfo.cs`
      (DeviceId, ProcessId, ProcessName, ExecutablePath, DisplayName, State
      Active/Inactive/Expired, IsSystemSounds, IsThisApp, Volume, Muted, Peak).
  - NAudio already wraps the needed APIs (checked in 2.2.1):
    - `MMDevice.AudioSessionManager.Sessions` lists the sessions (via
      IAudioSessionManager2).
    - `AudioSessionControl` provides `GetProcessID`, `State`, `DisplayName`,
      `IsSystemSoundsSession`, `SimpleAudioVolume` and `AudioMeterInformation`.
    - `AudioSessionManager.OnSessionCreated` reports new sessions, and
      `AudioSessionControl.RegisterEventClient` reports state changes,
      disconnects and volume changes.
  - **Threading (lesson 3).** Every session object must be created and used on
    one MTA thread. Either give the service its own MTA worker thread, built
    like `LevelMeterService`, or share that thread. Getting devices through the
    enumerator on that thread is fine. Callbacks arrive on system threads, so
    marshal them to the UI with the dispatcher.
  - **Notifications plus a fallback poll.** Register `OnSessionCreated` per
    active endpoint. Windows only starts sending these notifications after the
    sessions have been listed once, which `RefreshSessions` and `Sessions` do.
    Register per-session events for state changes. Also re-list the sessions
    every 2 s as a safety net, because session notifications are known to be
    unreliable (e.g. after an endpoint is reset). Re-register when devices
    change (`DevicesChanged`), and release everything when an endpoint goes
    away.
  - **Process names.** Use `Process.GetProcessById(pid)` for the name and
    `MainModule.FileName` for the path. Elevated or protected processes throw
    *access denied*, so fall back to `QueryFullProcessImageName` with
    `PROCESS_QUERY_LIMITED_INFORMATION`, then to "PID 1234". If the process
    has already exited, show "(exited)".
    - Browsers show up under their audio-service process (e.g. a
      `chrome.exe`/`msedge.exe` utility process), not per tab. Say so in the
      tooltip.
    - PID 0, or the system-sounds session, is labelled "System sounds".
  - **Mark our own streams.** Sessions with
    `ProcessId == Environment.ProcessId` are this app's: the silent meter
    capture on the selected mic, and test streams. Show them as "this app
    (meter)" / "this app (test)", and leave them out of the "in use" count.
    Otherwise selecting a mic would always show it as in use.
  - Cost: only active endpoints have sessions. Enumerate per device, never
    from the UI thread.
- [ ] **Device list.** Each row gets an "in use" marker: a small glyph plus a
      count of active sessions from other apps. Its tooltip lists the apps,
      e.g. "Teams (active), Chrome (inactive)". On input rows also add a
      "mic open → headset likely in call mode" hint when the device is a
      headset. Use the form factor property or the shared container ID to
      tell.
- [ ] **Details panel.** Add a "USED BY" section listing every session: app
      name, PID, state, session volume % and mute, and a mini level meter
      using `AudioSessionControl.AudioMeterInformation` polled on the same
      thread. Inactive sessions are dimmed. Expired sessions are removed.
- [ ] **Events for N2.** Raise `SessionStarted`, `SessionStopped` (on
      Active↔Inactive changes) and `SessionRemoved`, with device and app.
- **Limitations to document in the UI.**
  - Only streams that go through the Windows audio engine show up;
    exclusive-mode streams may not.
  - Sessions show what is open on the *Windows endpoint*. A dongle's
    Bluetooth link state is still invisible; this only shows what usually
    triggers it.
- **Verification.** All silent:
  - Build a scratch harness that opens a silent `WasapiCapture` on the Intel
    array from a separate process. The row should show "in use (1)" with the
    harness's process name, and the marker should disappear when the harness
    exits.
  - Select a mic in the app and check the app's own meter session is shown as
    "this app" and not counted.
  - Use UI Automation to read the tooltips.
- **Decisions needed:**
  - Should sessions be controllable from the app, with a per-session volume
    and mute (Windows' volume mixer)? That changes other apps' audio. The
    default proposal is read-only.
  - Should output rows show sessions as well, or only inputs?

### N2. Event timeline
Goal: a timestamped log of everything that happens to the audio devices, so
an intermittent problem ("my mic died in the meeting at 14:03") can be
reconstructed afterwards.

- [ ] **Model and service.**
  - Model: `Models/AudioEvent.cs` (Timestamp, Kind, DeviceId, DeviceName,
    Direction, Detail, Source = Windows | ThisApp).
  - Service: `Services/EventLogService.cs`, an in-memory ring buffer of
    5,000 entries that raises `EventAdded`.
  - Kinds: DeviceAdded, DeviceRemoved, StateChanged (old → new),
    DefaultChanged (flow + role), PropertyChanged, VolumeChanged, MuteChanged,
    SessionStarted, SessionStopped, TestStarted, TestFinished, Error.
- [ ] **Event sources** (changes to existing code):
  - `DeviceEnumerationService`: today every IMMNotificationClient callback
    becomes a bare `_onChanged()`. Add a detailed event carrying each
    callback's data, before the 150 ms refresh debounce:
    - added/removed with the device ID;
    - `OnDeviceStateChanged` with the new state (the old state comes from a
      cached state per ID);
    - `OnDefaultDeviceChanged` with flow, role and the new default ID;
    - `OnPropertyValueChanged` with the key. Keep it but hide it by default,
      because it's very chatty.
  - `EndpointVolumeService.VolumeChanged`: volume and mute changes. Own
    changes are already tagged with the event-context GUID; log those as
    Source = ThisApp. Coalesce bursts: consecutive volume changes on the same
    device within 500 ms merge into one entry, "Volume 40 % → 65 %", so a
    slider drag isn't hundreds of lines. Note this only covers devices whose
    volume is monitored, which today means active ones.
  - `AudioSessionService` (N1): "Teams started using Headset Microphone",
    "… stopped".
  - The app's own actions in `MainViewModel`: Set Default/Comms/Both (with
    the previous default), test start/stop/outcome, and errors already shown
    in the status bar.
- [ ] **Names for removed devices.** When a device is removed it may no longer
      enumerate, so keep an ID → name cache (filled on every refresh, never
      pruned) to label its entries.
- [ ] **Grouping.** One default change fires several callbacks (lesson 7). Log
      each raw callback, but show callbacks that arrive within 200 ms with the
      same kind as one entry ("Default output (Console, Multimedia) →
      Speakers"). Raw entries stay available in the export.
- [ ] **UI.** An "EVENTS" panel:
  - Proposal: a second collapsible strip next to PROPERTIES on the right edge
    of the details area, reusing `FlatStripToggle`. Only one of the two is
    open at a time.
  - Columns: time (`HH:mm:ss.fff`), kind, device, detail.
  - Toolbar: filter by kind, "selected device only", Pause, Clear, Copy, and
    Export (CSV / Markdown / JSON Lines).
  - Newest entry on top, with auto-scroll unless the user has scrolled away.
- [ ] **Optional persistence.** Also append to
      `%LOCALAPPDATA%\AudioDeviceDebugger\events-YYYY-MM-DD.log` (JSON Lines),
      so a session survives an app restart or crash. Keep the last 14 days.
- **Limitation.** The app must be running to record anything. A tray mode
  (suggestion #15) would make it useful all day.
- **Verification.**
  - Toggle mute on Speakers (Realtek) twice through the app (net change
    none) and nudge a volume and restore it. Expect the coalesced entries
    with Source = ThisApp.
  - Change the same volume from the Windows volume flyout. Expect
    Source = Windows. This is manual and needs the owner.
  - Plugging or unplugging the Jabra dongle needs the owner. Never change
    defaults just to test.
  - Check the export files open and parse.
- **Decisions needed:**
  - Persist to disk (on or off by default)?
  - Should an always-on log record device names and IDs? They include USB
    serial numbers.
  - Panel placement: right-edge strip as proposed, or a bottom tab?

### N3. Call-mode detection from the recording (bandwidth analysis)
Goal: after a mic test, report the *audio bandwidth* actually reaching
Windows, which shows the Bluetooth codec in use even through a USB dongle,
where Windows sees no format change.

- **Background.**
  - Typical bandwidth limits:

    | Mode | Sample rate | Bandwidth |
    |---|---|---|
    | Narrowband call (HFP CVSD) | 8 kHz | ≈ 4 kHz |
    | Wideband call (HFP mSBC) | 16 kHz | ≈ 8 kHz |
    | Super-wideband (LE Audio LC3 at 32 kHz) | 32 kHz | ≈ 16 kHz |
    | Full band (wired/USB mics, laptop arrays) | — | ≈ 20 kHz |

  - A Bluetooth headset *mic* only works in a call codec, so for headset
    mics this tells narrowband from wideband or better call quality.
  - The headset's *playback* degradation can't be measured: we can't record
    what the headset plays. It is inferred from N1 instead (mic open ⇒ the
    dongle switches to call mode).
  - The endpoint's own sample rate caps the result (Nyquist = rate / 2). A
    16 kHz endpoint can never show more than 8 kHz, so the report has to say
    "limited by the device format" rather than "call mode" in that case.
- [ ] **Analysis** `Services/BandwidthAnalyzer.cs`. A pure function, unit
      testable: `Analyze(RecordedTake take) → BandwidthResult`.
  - Mix the channels to mono, or analyse the loudest channel.
  - Average the power spectrum over the recording (Welch's method). Use
    4,096-point FFT frames (NAudio `NAudio.Dsp.FastFourierTransform`), a Hann
    window and 50 % overlap.
  - **Only use frames with signal.** Skip frames whose RMS is below the
    median frame + 10 dB (silence and room noise). Require at least about
    1 s of such frames; otherwise return "not enough signal — speak during
    the test".
  - Smooth the spectrum into 1/6-octave bands. As the reference, take the
    level of the 300 Hz–3 kHz speech band.
  - **Find the cutoff.** It is the highest band that is within 45 dB of the
    reference and above the noise floor, where the noise floor is measured
    from the quiet frames in the same band. A *steep* drop (≥ 25 dB within
    one 1/6-octave step) marks a codec low-pass ("brick wall") rather than
    natural roll-off; report which of the two it was.
  - **Classify:** ≤ 4.5 kHz narrowband; ≤ 8.5 kHz wideband; ≤ 16.5 kHz
    super-wideband; else full band. Treat a cutoff within 10 % of Nyquist as
    "limited by device format".
  - The thresholds above are starting values, to be tuned on real recordings
    from the owner's Jabra, the Intel array and any other mic.
- [ ] **Hook-up.** Run the analysis in `RunTestAsync` when an input test
      completes, using the kept `RecordedTake`. Run it on a pool thread (it's
      pure math), and store the result per device next to `_lastTakes`.
- [ ] **UI.**
  - Details panel line, e.g. "Bandwidth: ≈ 7.9 kHz (sharp cutoff) — wideband
    call quality (e.g. HFP mSBC). Format 16 kHz." The status bar gets a short
    form.
  - New `Views/SpectrumView` control: the averaged spectrum on a log
    frequency axis from 50 Hz to Nyquist and a dB scale, with markers at 4, 8
    and 16 kHz and the detected cutoff. Draw it like `LevelGraph` (flat
    theme, no chrome).
- [ ] **Optional: analyse without playback.** Add a "Record only" option to
      the mic test, so the analysis can run without the audible playback.
- **Verification.**
  - Unit tests and an offline harness: white noise and speech-like noise,
    low-passed at 3.4, 7, 14 and 20 kHz, classified correctly. Plus a
    silence case and a too-short-signal case.
  - On hardware (needs the owner to speak during a mic test): the Intel
    array should read full band, and the Jabra headset mic presumably
    wideband. Record the measured values in CLAUDE.md.
- **Decisions needed:**
  - Is the spectrum plot wanted, or only the one-line verdict?
  - Should "Record only" be added?

### N4. Raw mic capture (bypass Windows audio processing)
Goal: record a mic with the driver's/Windows' signal processing switched off
(beamforming, noise suppression, AGC, echo cancellation), to compare with the
processed signal and to expose individual mic-array capsules. Two expected
uses: finding out why the Intel array's channels 2 and 4 read below −60 dB,
and why its level seemed low.

- **Background.** WASAPI's `AUDCLNT_STREAMOPTIONS_RAW` (Windows 8.1+), set
  via `IAudioClient2::SetClientProperties` *before* `Initialize`, asks for the
  raw signal path. Whether a device supports it is reported by the endpoint
  property `System.Devices.AudioDevice.RawProcessingSupported`, key
  `{8943b373-388c-4395-b557-bc6dbaffafdb},2` (confirm on the dev machine).
- **NAudio gap (checked in 2.2.1).**
  - The `AudioClientProperties` and `AudioClientStreamOptions.Raw` types
    exist.
  - But `AudioClient` has no `SetClientProperties`, and `WasapiCapture`
    can't set it, so raw capture needs its own interop.
- [ ] **Interop** `Interop/AudioClient2.cs`.
  - A `[ComImport]` `IAudioClient2`, IID
    `726778CD-F60A-4EDA-82DE-E47610CD78AA`, declaring the full vtable in
    order: the IAudioClient methods (Initialize … GetService), then
    `IsOffloadCapable`, `SetClientProperties`, `GetBufferSizeLimits`.
  - Plus `IAudioCaptureClient`, and the `AudioClientProperties` struct
    (`cbSize`, `bIsOffload`, `eCategory`, `Options`).
  - Activate it through the existing `EndpointPropertyReader`
    `IMMDeviceRaw.Activate`, which already gets an IMMDevice via the
    enumerator created by CLSID (lesson 2).
  - Don't use reflection into NAudio's private fields.
- [ ] **Capture loop** `Services/RawCapture.cs`, run on an MTA worker.
  - Activate, then `SetClientProperties` (category Other, options RAW), then
    `GetMixFormat`. The raw format may differ from the processed one, e.g.
    more channels; use whatever the device reports after the call.
  - Then `Initialize` (shared, event-driven, 100 ms buffer),
    `GetService(IAudioCaptureClient)`, `Start`, and a
    GetNextPacketSize/GetBuffer/ReleaseBuffer loop into a float buffer.
  - Convert to float the same way the processed path does, honour the
    session's Stop token, and produce the same `RecordedTake` (tagged
    `IsRaw = true`).
  - If `SetClientProperties` fails, report "raw capture not supported by this
    device/driver".
- [ ] **Test service.** Extend `StartRecordAndPlayback(inputId, outputId,
      bool raw)`, or add a separate start method. Playback and per-channel
      playback work unchanged on the raw take, since the format conversion is
      shared.
- [ ] **UI.**
  - For inputs, a "Raw (bypass processing)" checkbox next to the channel
    strip. It is enabled only if the property says raw is supported;
    otherwise it's disabled with a tooltip explaining why.
  - The mic test honours the checkbox. The kept take is labelled
    "raw"/"processed", and so is the strip (e.g. tooltip "Ch 2 — raw
    recording").
  - Optional "Compare" button: record 5 s processed, then 5 s raw, and keep
    both takes for A/B playback. Since N3 exists by then, show both bandwidth
    results too.
- [ ] **Details panel.** Show "Raw processing: supported / not supported"
      alongside the format info.
- **Verification.**
  - Read the raw-support property for every input.
  - In a harness, record 3 s raw and 3 s processed from the Intel array
    *without playback* (silent). Compare the channel count and per-channel
    RMS. If channels 2 and 4 are at room-noise level in raw, that explains
    the processed result.
  - The Jabra will probably report no raw support. Check that the UI
    disables the option cleanly.
  - Listening to the A/B takes needs the owner.
- **Decisions needed:**
  - Checkbox (applies to every mic test) or a separate "Raw test" button?
  - Is the "Compare" button wanted?

### N5. Diagnostic report export
Goal: one file describing the complete audio setup and its recent history.
Useful for IT support or a vendor ticket, or to compare a "working" with a
"broken" state.

- [ ] **Toolbar.** A "Report" button opens a menu: Save as Markdown…, Save as
      JSON…, Copy as Markdown. The file goes through a `SaveFileDialog`
      with a default name `audio-report-YYYYMMDD-HHmmss`.
- [ ] **Service** `Services/DiagnosticReportService.cs` builds the report
      data, a plain DTO tree, *not* NAudio objects (`Models/Report/*`).
      Writers:
  - `MarkdownReportWriter`: headings per device, and tables for formats,
    sessions and properties.
  - JSON through `System.Text.Json`, indented, with enums as strings.
- [ ] **Contents.**
  - **Header:** app version, report time with time zone, Windows edition,
    version, display version (e.g. 24H2) and build.UBR (from
    `HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion`), .NET runtime
    version.
  - **Defaults:** all four roles (render/capture × multimedia/communications),
    with device names.
  - **Per device:** every endpoint including disabled, not-present and
    unplugged, regardless of the UI filters.
    - Name, ID, direction, state, roles.
    - Volume % and dB plus mute (active devices only).
    - Shared-mode mix format with channel layout, and exclusive formats.
    - Raw-capture support (N4).
    - Sessions using the device (N1).
    - Last test outcome and the last bandwidth analysis (N3), if any.
  - **Property stores:** the full dump with readable names, behind an
    "Include all properties" option (large: ~150 keys × ~29 endpoints).
  - **Event timeline:** the last 500 entries, or everything since the app
    started (N2).
- [ ] **Performance and threading.**
  - Gather on a background thread: names via `EndpointPropertyReader`, never
    `MMDevice.FriendlyName` (lesson 1, ~450 ms per device). Create the COM
    objects on the thread that uses them (lesson 3). Sessions come from N1's
    thread.
  - Show progress in the status bar ("Collecting device 12/29…") and keep
    the UI responsive. Target under 5 s with properties included.
- [ ] **Privacy option.** "Redact identifiers", on by default. It masks:
  - USB serial numbers inside endpoint and instance IDs, such as the trailing
    serial in `USB\VID_0B0E&PID_2E56\…`;
  - the machine and user names;
  - Bluetooth addresses.

  This lets the report be shared without leaking hardware serials.
- **Verification.**
  - Generate both formats on the dev machine and check every endpoint is
    present: the counts must match the "Show:" checkbox totals.
  - Check the JSON round-trips (deserialises back), and that redaction
    removes serials while keeping devices distinguishable.
  - Time the run with and without properties.
  - Check the UI never freezes, for example with a UI Automation ping during
    generation.
- **Decisions needed:**
  - Default format (Markdown or JSON)?
  - Should "Include all properties" be on by default?
  - Should redaction be on by default?
  - Include driver details (suggestion #17: driver version/date, USB VID/PID)
    now, or later?

## Not yet done / follow-ups
- [ ] Manual verification pass on a real Windows machine (blocked: this
      environment is Linux-only, see README "Testing" section)
- [ ] Automated UI tests (no Windows test runner available in this container)
- [ ] Code signing of the published executable
