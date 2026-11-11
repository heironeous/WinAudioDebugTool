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
- [x] Raw capture (IAudioClient2 stream options) to bypass mic-array processing
      and expose the individual capsules: Record A/B, then pick the Raw line
      for the strip (see N4)

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

Five features chosen from the suggestions list (2026-11-10). **Each needs the
owner's go-ahead before implementation** — N2, N1 and N4 are done; N3 and N5
are not started. Suggested
order: N1 → N2 → N3 → N4 → N5. N1 feeds N2 and N5, and N3 feeds N5. The report
(N5) comes last because it collects everything else. Each feature is its own
commit, with this file updated in that commit. Every item ends with
"Decisions needed": questions to settle at approval time.

### N1. "Who's using this device" — implemented (2026-11-10)
Goal: show which apps have a stream open on each device, especially the
headset mic. An app opening the mic is what switches a USB dongle (Jabra Link
390, Poly BT700) into call mode, so this answers "why does my headset suddenly
sound bad?" and "who is holding my mic?".

Decisions used (the owner didn't specify, so the proposed defaults apply):
**read-only** (no control over other apps' volume), and sessions are shown for
**inputs and outputs**.

- [x] **Service** `Services/AudioSessionService.cs` (+ `IAudioSessionService`),
      model `Models/AudioSessionInfo.cs`.
  - One MTA thread; all COM objects live on it (lesson 3).
  - It keeps one NAudio `AudioSessionManager` per active endpoint, and
    re-lists every endpoint's sessions **every 500 ms** with
    `RefreshSessions`. `OnSessionCreated` triggers an immediate rescan. The
    callback only sets a flag, because re-enumerating inside it would
    re-register the notification from within itself.
  - `SessionsUpdated` is raised only when something changed (apps, states,
    volumes).
  - For the selected device, the session controls are kept between scans
    and their peaks are read 10× a second (`PeaksUpdated`).
  - Nothing is reported before the first `SetDevices`, so sessions already
    streaming at startup aren't logged as new.
  - Deviation from the plan: it polls instead of using per-session
    `IAudioSessionEvents`. A full scan of 4 endpoints costs ~0.5 ms, polling
    can't miss a lost notification, and the cost is ~0.5 s latency. A
    stream shorter than that can be missed.
  - Process names are resolved once per PID and cached:
    - `Process.GetProcessById` gives the name;
    - `QueryFullProcessImageName` with `PROCESS_QUERY_LIMITED_INFORMATION`
      gives the path (it works for elevated processes);
    - the app name is the executable's FileDescription (e.g. "Webex"), or
      else the session's display name or the process name;
    - PID 0 / `IsSystemSoundsSession` is shown as "System sounds";
    - a process that has exited is marked.
  - Sessions with `ProcessId == Environment.ProcessId` are flagged
    `IsThisApp`: the selected mic's meter stream and test streams.
- [x] **Device list.** The row subtitle gets " · In use by Webex" (+N),
      accent-coloured, while another app is *streaming* (Active). This app
      and the system-sounds session don't count.
  - The subtitle tooltip lists every app session, streaming first, with
    PIDs.
  - Headset or handset *input* rows that are in use add the call-mode hint.
    These are recognised by `PKEY_AudioEndpoint_FormFactor` 5/6, now read
    into `DeviceInfo.FormFactor` via `EndpointPropertyReader.TryGetUInt32`.
- [x] **Details panel.** Originally a "USED BY" section under the channel strip.
      Since 2026-11-11 it is the **PROCESSES** side-panel tab (see "Side-panel
      tabs" below). It covers active devices only, and adds a PROCESS · PID
      column.
  - The header gives a summary ("2 app sessions, 0 streaming").
  - Each row shows the app, "Streaming"/"Idle", the app's volume in
    Windows' volume mixer, and a live peak meter. Idle rows are dimmed.
  - Order: other apps' streaming sessions, then idle ones, then system
    sounds, then this app.
  - The row tooltip has the process name, PID, path, what Streaming/Idle
    means, and a note for browsers (one audio process, not per tab).
  - Rows are updated in place by session key.
- [x] **Events for N2.** "App" entries ("Webex started streaming (PID …)",
      "… stopped streaming", "(session closed)" / "(device no longer
      active)"), with an "Apps" filter.
  - Streams already running when the log starts are logged once as "was
    already streaming when the log started".
  - This app's own streams and system sounds aren't logged; every
    notification sound would otherwise make a pair of lines.
- **Limitations.**
  - Only streams that go through the Windows audio engine show up;
    exclusive-mode streams may not.
  - Sessions show what is open on the *Windows endpoint*; a dongle's
    Bluetooth link state is still invisible.
  - Peak meters start at −60 dBFS, so in a quiet room a streaming capture
    session can read empty: measured −50…−80 dBFS.
- **Verified** (2026-11-10). Silent; only the Intel mic array was used.
  - Probe of the real machine: Webex (`CiscoCollabHost` "Webex", `atmgr`
    "Cisco Webex Service") holds idle sessions on all 4 active endpoints,
    and there's a system-sounds session on each.
  - A separate process held a silent capture stream on the Intel array for
    5 s. Results:
    - the row showed "In use by n1probe", with the tooltip listing it as
      streaming, and cleared after the process stopped;
    - USED BY listed it first ("3 app sessions, 1 streaming");
    - the log got "started streaming" and "stopped streaming (session
      closed)" lines ~5 s apart;
    - the app's own meter stream showed as "This app … Streaming" and wasn't
      counted;
    - its peak tooltip read −43…−58 dBFS (room noise), so live peaks reach
      the UI.
- **Not verified, needs the owner:**
  - Join a call on the Jabra headset, and check the hint and the "In use by
    Webex/Teams" marker on its mic row.
  - The visual layout: the screenshot came back white (display asleep).

### N2. Event timeline — implemented (2026-11-10)
Goal: a timestamped log of everything that happens to the audio devices, so
an intermittent problem ("my mic died in the meeting at 14:03") can be
reconstructed afterwards.

- [x] **Model and log.**
  - Model: `Models/AudioEvent.cs`, an immutable record with Timestamp,
    LastTimestamp, Kind, Category, Source = Windows | ThisApp, device ID,
    name and direction, Detail, Count, FromValue and Low/High.
  - Log: `ViewModels/EventLogViewModel.cs`, an in-memory ring buffer of 5,000
    entries, newest first, used on the UI thread only. It has per-category
    filters, "selected device only", and `AddOrMerge` for coalescing bursts.
    - Deviation from the plan: it's a view model rather than
      `Services/EventLogService.cs`, because it owns the filtered
      `ICollectionView` the panel binds to.
  - Kinds: Started, DeviceAdded, DeviceRemoved, StateChanged (old → new),
    DefaultChanged, DefaultRequested, VolumeChanged, MuteChanged,
    TestStarted, TestFinished, PropertyChanged, Error.
- [x] **Event sources.**
  - `DeviceEnumerationService`:
    - new `EndpointNotified` event carrying each IMMNotificationClient
      callback's data (`EndpointNotification`), timestamped in the callback,
      before the refresh debounce;
    - `OnPropertyValueChanged` is now forwarded, but only to the log; it
      still doesn't trigger a refresh;
    - new `TryGetFriendlyName` for endpoints not seen before.
  - Volume and mute:
    - external changes come from `EndpointVolumeService.VolumeChanged`
      (Source = Windows);
    - the app's own slider and mute edits are logged in `MainViewModel`
      (Source = ThisApp);
    - both are compared with the last known value per device.
  - The app's own actions in `MainViewModel`: Set Default/Comms/Both, test
    start and outcome (a failure is logged as Error), and load errors. On
    start the log records an entry with the endpoint count and the four
    current defaults.
- [x] **Names for removed devices.** `_knownDevices` (ID → last `DeviceInfo`)
      is filled on every refresh and never pruned. It also supplies the *old*
      state for a state change, and the previous default holder ("was X").
      Endpoints not seen yet are read directly. Their direction comes from the
      ID prefix: `{0.0.0.` is output, `{0.0.1.` is input.
- [x] **Grouping and coalescing.**
  - Default changes: callbacks for the same device and direction within
    500 ms merge into one line, e.g. "Became the default output for Console +
    Multimedia (was X)". Communications is kept as a separate line, because
    its previous holder usually differs.
  - Volume: changes on the same device from the same source with less than
    1 s between them merge, e.g. "Volume 40 % → 65 %". If the value went
    beyond its start and end during the burst, the range is shown, e.g.
    "100 % → 100 % (dipped to 90 %)".
  - Property changes: repeats of the same key within 1 s merge.
  - Each line records how many notifications it merged (Count; shown in the
    tooltip and exports).
    - Deviation from the plan: individual raw callbacks are not kept.
- [x] **UI.** An "EVENTS" strip toggle under PROPERTIES at the right edge.
      Both share the right-hand column, and opening one closes the other
      (`ApplySidePanelLayout`).
  - The panel has a header with its count, and buttons for Copy (plain text
    of the shown events), Export… (CSV / Markdown / JSON Lines; the format
    follows the chosen file extension, `Services/EventLogExporter.cs`) and
    Clear.
  - Filters: Devices, Defaults, Volume, Tests, Properties, and "Selected
    device only". Entries without a device (start, errors) stay visible under
    "Selected device only".
  - Properties is off by default. While it's off, property changes aren't
    *recorded* at all; the check happens on the notification thread.
  - The table follows the flat dark theme (`LogHeader`/`LogCell`/`LogRow` in
    `Theme.xaml`); Error is shown red, Default accent-coloured, and each row's
    tooltip has the full detail.
  - Deviation from the plan: no Pause button. Newest entries go on top, so
    there's no auto-scroll to pause.
- [x] **Session events** ("Webex started streaming …"), added with N1 ("Apps" filter).
- [ ] **Optional persistence.** Not built; events are in memory only until
      the owner decides. The proposal is to append JSON Lines to
      `%LOCALAPPDATA%\AudioDeviceDebugger\events-YYYY-MM-DD.log` and keep 14
      days.
- **Limitation.** The app must be running to record anything. A tray mode
  (suggestion #15) would make it useful all day.
- **Verified** (2026-11-10). Silent; only the Intel mic array was touched,
  and it was restored.
  - Offline harness (`EventLogViewModel` and exporter), 15/15 checks:
    burst merging, merging past an interleaved event, filters, capacity,
    CSV/Markdown escaping, JSON Lines parsing.
  - In the app, via UI Automation:
    - start entry with the four defaults;
    - in-app mute and unmute → two "this app" lines;
    - in-app slider drag → one merged line;
    - level changes from a separate process → "Windows" lines, two bursts
      1.5 s apart giving two lines, with "dipped to" ranges.
  - No property-change notifications arrived in 6 s idle, nor during volume
    changes.
  - Screenshot checked: panel layout and both strips.
- **Not verified, needs the owner:**
  - plugging or unplugging a device (state, added and removed lines);
  - default changes from Windows Settings, and Set Default in the app;
  - test start and finish lines (the tests are audible);
  - the Export… dialog itself (the writer is covered by the harness).

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

### N4. Raw mic capture vs. what the other side hears — implemented (2026-11-11)
Goal: record a mic with the driver's and Windows' processing switched off, and
compare it with the processed signal that call apps receive. Two expected uses:
finding out why the Intel array's channels 2 and 4 read below −60 dB, and why
its level seemed low.

Scope at approval: the owner asked to compare "what is recorded RAW and what
the other side hears". So the main feature is an **A/B recording** that captures
the same sound in several modes at once. A "Raw" checkbox for the plain mic test
was not built.

- [x] **Interop** `Interop/AudioClient2.cs`.
  - `IAudioClient2` (full vtable), `IAudioCaptureClient`,
    `IAudioEffectsManager` (Windows 11), and the `AudioClientProperties` and
    `AUDIO_EFFECT` structs. IIDs, method order and enum values were checked
    against `Windows.Win32.winmd`.
  - `AudioClient2Activator` gets the IMMDevice through an enumerator created
    by CLSID (lesson 2).
  - Effect names come from the `AUDIO_EFFECT_TYPE_*` GUIDs in `ksmedia.h`;
    they aren't in the winmd. Unknown GUIDs are shown as-is.
  - `AudioPropertyKeys.RawProcessingSupported` holds the property key
    `{8943b373-…},2`. It is VT_BOOL, read with the new
    `EndpointPropertyReader.TryGetBool`.
- [x] **Capture** `Services/ModeCapture.cs`. One shared-mode stream per mode:
  - **Standard**: no client properties set; this is what NAudio's
    WasapiCapture gets.
  - **Call**: category `Communications`, which is what Teams and Webex request.
  - **Raw**: `AUDCLNT_STREAMOPTIONS_RAW`.
  - The properties are set with `SetClientProperties` before `Initialize`;
    then `GetMixFormat`, then shared-mode `Initialize` with a 200 ms buffer.
  - Capture is polled, not event-driven: `Drain` every 10 ms.
  - Silent packets are written as zeros. Discontinuities are counted as
    glitches.
  - The effects the engine reports for each stream are read through
    `IAudioEffectsManager`.
  - `SampleFormat` turns WAVE_FORMAT_EXTENSIBLE into the equivalent plain
    format, because NAudio's converters reject extensible headers.
- [x] **Test service.** `StartModeComparison(input, output)`:
  - It opens Raw, Call and Standard together and starts them back to back, so
    every take holds the same sound. It records 5 s, or until Stop.
  - It then plays Raw, then Call, with a 0.6 s gap.
  - `AudioTestSession.ModeTakes`, `ModeErrors` and `PlayingMode` (+ event)
    carry the results.
  - A mode that fails to open is reported and the others still record.
  - `StartTakePlayback(takes, …)` plays whole takes.
  - `RecordedTake` now carries Mode, Effects and Glitches, plus overall and
    per-channel RMS/peak in dBFS.
- [x] **UI.** A **RAW RECORDING** side-panel tab (first built as a RECORDINGS
      section below the channel strip; see "Side-panel tabs" below). It works
      for active inputs; any other selection shows a hint instead.
  - The header shows "· the driver supports raw capture" (or says it doesn't)
    and has a **Record A/B** button, which becomes Stop while a test runs.
  - There's one fixed line each for Raw, Call and Standard. Each line has:
    - ▶ to play the whole take;
    - its level ("−40.2 dB" RMS) and "4 ch · 48 kHz · 3.1 s";
    - the effects Windows reported ("Echo cancellation, Noise suppression" /
      "no effects");
    - a tooltip with per-channel RMS/peak, the full effect list, glitches and
      what the mode means.
  - The line that's playing is accent-coloured.
  - Clicking a line makes the **channel strip** play that take's channels. The
    highlighted line is the strip's source; after an A/B recording it is Raw.
    The strip tooltips name the mode ("Ch 2 of the Raw recording").
  - The row's plain **Test** still records with NAudio. Its take becomes the
    Standard line, marked "effects not checked".
  - The lines are a ListBox, so they're keyboard- and UI Automation-selectable.
  - Each line also shows what its mode means.
  - Log entries: "A/B recording (Raw, Call, Standard) started" and the
    outcome.
- **Limitations.**
  - "What the other side hears" is approximated by the Call take: the driver's
    and Windows' communications processing.
  - The call app's own processing (e.g. Teams noise suppression), the codec and
    the network come on top of that, and can't be captured here.
  - On some drivers Standard and Call are the same processing. On the Intel
    array both report echo cancellation and noise suppression.
  - Raw *output* (playback) isn't covered.
- **Verified** (2026-11-11). Only the Intel array was recorded.
  - Probe (`n4probe`, silent, no playback):
    - both active mics report raw support;
    - Raw, Call and Standard opened *at the same time* on the Intel array;
    - effects: Standard and Call reported echo cancellation and noise
      suppression, Raw reported none;
    - format: 48 kHz, 4 ch in all modes.
    - What the levels show:
      - the processed takes are one signal copied to all 4 channels, about
        −66 dB RMS in a quiet room;
      - Raw is four distinct capsules at about −40 dB;
      - so noise suppression removes about 25 dB of room noise.
  - UI (UI Automation):
    - Record A/B was stopped after ~3 s; Raw playback started and was stopped
      after ~0.5 s;
    - the three lines filled in (Raw −40.2 dB "no effects"; Call and Standard
      −47.5 dB, echo cancellation + noise suppression);
    - selecting a line switched the channel strip between Standard and Raw;
    - the plain mic Test replaced the Standard line;
    - RECORDINGS is hidden for outputs, and the log entries are right.
  - The layout was checked by screenshot at 1200×800 and at a taller size.
- **Not verified, needs the owner:**
  - Listening to the A/B takes.
  - Recording the Jabra mic. Opening it switches the dongle to call mode, so
    the automated checks avoided it. It reports raw support.
- Found along the way: the Jabra Link 390 mic reports form factor 4
  (Microphone), not 5/6 (Headset). So N1's call-mode hint, which is keyed on
  5/6, never shows for it.

### Side-panel tabs (2026-11-11, owner request)
The right-hand column now has four tabs: **PROPERTIES**, **EVENTS**,
**PROCESSES** (the apps using the selected device, from N1) and **RAW
RECORDING** (A/B recording, from N4).
- One panel shows at a time (`MainViewModel.OpenSidePanel`, enum `SidePanel`).
  Clicking the open tab collapses the column.
- Each tab has an icon plus a vertical label. The labels hide when the strip
  is too short for all four (`SideTabsScroll_ScrollChanged`), e.g. at
  1200×800. Tooltips name every tab.
- The open tab is highlighted (accent border and text, via a `FlatStripToggle`
  IsChecked trigger). The chevrons were replaced by the icons.
- Moving PROCESSES and RAW RECORDING out of the left column gives the level
  graph and the channel strip their full height back.
- Verified by UI Automation: exactly one tab is on at a time; each opens its
  own panel; closing collapses the column; PROCESSES lists Webex, the Cisco
  service, system sounds and this app with process names and PIDs; RAW
  RECORDING shows its hint for an output.
- Checked by screenshot at 1200×800 (icons only) and at a taller size (labels
  shown).

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
