# Design notes

Implementation and verification notes for the larger features, kept for contributors: what was built, why, where it
lives, what was measured, and what still hasn't been checked. User-facing documentation is in
[README.md](../README.md); the feature checklist and roadmap are in [TODO.md](../TODO.md).

"Lesson n" refers to the numbered *Hard-won lessons* in [CLAUDE.md](../CLAUDE.md). Hardware named in the
verification notes is the development machine's: Realtek laptop speakers, an Intel Smart Sound Technology 4-channel
mic array, and a Jabra Link 390 USB dongle (headset speakers and mic). Other hardware hasn't been tested.

Feature numbers (N1–N5) are the order the features were planned in; N3 and N5 are still on the roadmap in TODO.md.

## N1. "Who's using this device" — implemented (2026-11-10)
Goal: show which apps have a stream open on each device, especially the
headset mic. An app opening the mic is what switches a USB dongle (Jabra Link
390, Poly BT700) into call mode, so this answers "why does my headset suddenly
sound bad?" and "who is holding my mic?".

Decisions (the proposed defaults):
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
- **Verified** (2026-11-10, on the development machine). Silent; only the Intel mic array was used.
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
- **Not yet verified (needs manual testing):**
  - Join a call on the Jabra headset, and check the hint and the "In use by
    Webex/Teams" marker on its mic row.
  - The visual layout: the screenshot came back white (display asleep).

## N2. Event timeline — implemented (2026-11-10)
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
- [ ] **Optional persistence.** Not built; events are in memory only (see the
      roadmap in TODO.md). The proposal is to append JSON Lines to
      `%LOCALAPPDATA%\AudioDeviceDebugger\events-YYYY-MM-DD.log` and keep 14
      days.
- **Limitation.** The app must be running to record anything. A tray mode
   would make it useful all day.
- **Verified** (2026-11-10, on the development machine). Silent; only the Intel mic array was touched,
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
- **Not yet verified (needs manual testing):**
  - plugging or unplugging a device (state, added and removed lines);
  - default changes from Windows Settings, and Set Default in the app;
  - test start and finish lines (the tests are audible);
  - the Export… dialog itself (the writer is covered by the harness).

## N4. Raw mic capture vs. what the other side hears — implemented (2026-11-11)
Goal: record a mic with the driver's and Windows' processing switched off, and
compare it with the processed signal that call apps receive. Two expected uses:
finding out why the Intel array's channels 2 and 4 read below −60 dB, and why
its level seemed low.

Scope: compare "what is recorded raw and what the other side of a call
hears". So the main feature is an **A/B recording** that captures
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
- **Verified** (2026-11-11, on the development machine). Only the Intel array was recorded.
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
- **Not yet verified (needs manual testing):**
  - Listening to the A/B takes.
  - Recording the Jabra mic. Opening it switches the dongle to call mode, so
    the automated checks avoided it. It reports raw support.
- Found along the way: the Jabra Link 390 mic reports form factor 4
  (Microphone), not 5/6 (Headset). So N1's call-mode hint, which is keyed on
  5/6, never shows for it.

## Side-panel tabs (2026-11-11)
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

## Property names and values (2026-11-11)
Goal: give the ~180 unnamed endpoint property keys readable names where possible, and show property *values* in a
readable form.

- **Research.** On the development machine, the 29 endpoints carry 207 distinct keys, of which 181 had no name.
  - **Online:** Microsoft documents none of them. Community sources only show example values for a few keys:
    - [alax.info](https://alax.info/blog/1279): `{b3f8fa53-…},6` holds a device name;
    - [sogen PR #1502](https://github.com/momo5502/sogen/pull/1502): `{9c119480-…},1` holds the endpoint's device
      interface path.
  - **Windows property schema:** none of the 181 resolve through `PSGetNameFromPropertyKey`. The lookup was checked
    with known keys, e.g. `System.Devices.ContainerId`.
  - **Windows' own PnP data:** this is how keys were confirmed. Each candidate's value was compared, per endpoint,
    with `Get-PnpDeviceProperty` on the endpoint device (`SWD\MMDEVAPI\<id>`) and on the audio device behind it.

  | Key | Holds | Matched |
  |---|---|---|
  | `{b3f8fa53-0004-438e-9003-51a46e139bfc},6` | audio device name | 27/29 (1 stale after a driver change, 1 console-encoding artefact) |
  | `{9c119480-ddc2-4954-a150-5bd240d454ad},1` and `,9` | endpoint device interface path | 6/6 and 20/20 |
  | `{9c119480-ddc2-4954-a150-5bd240d454ad},2` and `,10` | endpoint device instance ID | 6/6 and 20/20 |
  | `{80f111c3-b103-42e1-afb6-db7a6fa8be1f},0` | audio device's first hardware ID | 13/13 (USB devices) |
  | `{83da6326-97a6-4088-9453-a1923f573b29},3` | driver record: INF:hash:section:version:hardware ID | 25/29 (4 recorded before a driver update) |

  `{83da6326-…},3` is *not* `DEVPKEY_Device_ConfigurationId`. Windows lists that key separately, with a different
  format.
- **Names** live in `PropertyKeyNames.Undocumented`: a display name plus a note on how it was confirmed.
  - The note is the name's tooltip, and the Key column says "undocumented".
  - No PKEY_ symbol is invented.
- **GUID names.** The PropertyKeyNameGenerator now also writes `Interop/KnownGuidNames.g.cs` with 264 audio GUID
  constants from the Win32 metadata (jack/node types, categories, sample formats, processing modes, effect types).
  - In the metadata these are mostly empty structs with a GuidAttribute, which is why an earlier field-only scan
    missed them.
  - `AudioEffectNames` (N4) now uses this table, which adds the two newer effect types: far-field beamforming and
    deep noise suppression.
- **Values.** `Services/PropertyValueFormatter.cs` decodes in two ways.
  - **By key**, for documented properties:
    - form factor;
    - jack type and association (GUID names);
    - speaker masks;
    - Disable_SysFx;
    - event-driven support;
    - default/max volume as 16.16 fixed-point dB;
    - `KSAUDIO_MIC_ARRAY_GEOMETRY`: angles in 1/10000 rad, mm, Hz, per Microsoft Learn.
  - **By shape**, for any value:
    - WAVEFORMATEX/EXTENSIBLE, only when every field is consistent;
    - VS_FIXEDFILEINFO;
    - 16-byte SYSTEMTIME with every field in range;
    - Latin UTF-16 text in a blob, including fixed-size text fields such as the spatial-sound format names;
    - dates as ISO;
    - known GUIDs;
    - endpoint IDs resolved to the endpoint's name.
  - Shape decoding says what a value *contains*, never what an undocumented key *means*.
- **Result on the development machine:**
  - 33 of 207 keys named (26 from the SDK and existing tables, 7 undocumented); 44 keys with decoded values.
  - Examples: "Microphone (4)", "Microphone array", "20 dB", "48 kHz · 16-bit PCM · 4 ch", "Version 10.0.26100.9711",
    and the spatial-sound name "Windows Sonic for Headphones".
  - The Intel array's geometry decodes as a linear array of 4 subcardioid mics at y = −91, −55, 55, 91 mm.
- **Still unnamed (about 170):** most belong to driver/APO-private sets, such as `{f112024a-…}` (58 keys) and
  `{5510c7ab-…}` (14). No public source documents them, so they stay raw.
- **Verified:**
  - A probe that calls the app's `DeviceDetailsService` checked every endpoint.
  - UI Automation and a screenshot checked the PROPERTIES tab for the Intel mic: named rows, decoded values, and raw
    values in the tooltips.

