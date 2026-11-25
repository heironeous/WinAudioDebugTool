# Design notes

Implementation and verification notes for the larger features, kept for contributors: what was built, why, where it
lives, what was measured, and what still hasn't been checked. User-facing documentation is in
[README.md](../README.md); open items and the roadmap are in [TODO.md](../TODO.md), finished features in
[planning/DONE-TODO.md](../planning/DONE-TODO.md).

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
- [x] **Persistence** (2026-11-23). See "Small follow-ups" below.
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
  5/6, never showed for it. Fixed 2026-11-23 by matching container IDs (see
  "Small follow-ups" below).

## Light theme and high contrast (2026-11-25)
`Views/ThemeManager.cs`, `Views/Themes/Dark.xaml`, `Views/Themes/Light.xaml`; `Theme.xaml` keeps the sizes and styles.

- **Mechanism.** The palette is the first merged dictionary of `App.Resources` (Dark.xaml in `App.xaml`, so the XAML
  designer still has colours). Every palette reference in the views is a `DynamicResource` (163 of them), so
  replacing that dictionary repaints the running app. Brushes in setters can't simply be recoloured instead: WPF
  freezes them when a style is sealed. Code that builds brushes from palette colours (the level graph's gridlines and
  gradient brushes, the title bar) listens to `ThemeManager.ThemeChanged`.
- **Choice**: `--theme=` override, then `SystemParameters.HighContrast`, then
  `HKCU\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize\AppsUseLightTheme` (missing = light, as in
  Windows). Re-evaluated on `SystemEvents.UserPreferenceChanged` (General / Color / VisualStyle / Accessibility) and
  when `SystemParameters.HighContrast` changes.
- **High contrast** builds the palette from `SystemColors` (Window, WindowText, Highlight, HotTrack, GrayText):
  no translucent overlays, every text level in WindowText, the selected row outlined in Highlight (a 2 px top/bottom
  border on each cell that is transparent in the other palettes) rather than filled, and the title bar left to
  Windows (`DWMWA_CAPTION_COLOR` = default).
- **Title bar**: `DWMWA_USE_IMMERSIVE_DARK_MODE` 1 for dark, 0 for light and high contrast; caption colour = the
  palette's `SurfaceBase`.
- **Light palette**: white panels on a light grey window; the meaning colours are darker than in the dark palette
  (accent #0F6CBD, positive #107C41, caution #8A5300, negative #C42B1C) so they read as text on white.
  `ThemeTests` checks both palettes: same keys as `ThemeManager.PaletteKeys`, primary and secondary text ≥ 4.5:1 on
  every surface and on hovered / selected rows, tertiary text and the meaning colours ≥ 4.5:1 on the surfaces. The dark
  palette's tertiary text failed on raised surfaces (3.97:1) and is now #9C9CA3.
- Verified: screenshots with `--theme=light`, `dark` and `high-contrast` (the last with this desktop's normal system
  colours, since contrast themes are off here), and the live switch dark → light → dark in a scratch harness that
  opens the real main window and swaps the palette (everything repainted, `ThemeChanged` raised twice). Not checked:
  a real Windows contrast theme, the app-mode change coming from Windows itself (the registry value wasn't changed on
  the maintainer's machine), Windows 10.

## Device grouping (2026-11-25)
`Services/DeviceGrouping.cs` (pure, unit-tested), `ViewModels/DeviceListGroup.cs`, `ListSection` / `SectionExpander`
in `Theme.xaml`.

- **Key**: `DEVPKEY_Device_ContainerId`; Windows' "this PC" container (`00000000-0000-0000-ffff-ffffffffffff`) is
  "This PC", endpoints without one are "Other". On the desktop each old HDMI output has a container of its own (one per
  monitor ever connected), so several "High Definition Audio Device" groups appear when Not present is shown.
- **Names**: the container's own name (`DEVPKEY_ItemNameDisplay` through `DevGetObjectProperties`,
  DevObjectTypeDeviceContainer) would be ideal, but on the desktop every call took 60 s and then failed with
  ERROR_FILE_NOT_FOUND, so the name comes from the endpoints: `PKEY_DeviceInterface_FriendlyName` (the part in brackets
  of "Speakers (Jabra Link 380)", a "2- " duplicate prefix removed), except for display endpoints (form factor 9),
  whose interface is the graphics card's audio: there the endpoint part ("ASUS VP28U") is used, unless it's generic
  ("Digital Output", "NVIDIA Output"). The most common name among the active endpoints wins.
- **Grouping in WPF**: the view is grouped by a `DeviceListGroup` object per row (`SectionGroup` or `DeviceGroup`,
  swapped by `MainViewModel.ApplyGrouping`), sorted by `GroupDescription.CustomSort`. Refresh rebuilds every row, so
  the group objects are cached by key in the view model; they carry `IsExpanded`, which is why a collapsed group stays
  collapsed. The group template is an `Expander`: with a plain ToggleButton heading the GroupItem exposed no
  expand / collapse pattern to UI Automation (screen readers couldn't open a group).
- Verified on the desktop with all states shown: 14 groups, each collapsed through UI Automation; collapsed state
  survives Refresh; switching back restores OUTPUTS / INPUTS. Not checked: the laptop (Jabra Link 390 should group
  its speaker and mic, the Intel array and Realtek speakers under This PC), keyboard navigation across collapsed groups.

## Device inventory: the Driver tab (2026-11-25)
`Services/DeviceInventoryService.cs`, `Interop/DeviceNode.cs` (cfgmgr32), `Interop/UsbHub.cs`; pure helpers in
`Services/InventoryFormat.cs` (unit-tested).

- **Endpoint → device.** Every endpoint is a device node `SWD\MMDEVAPI\{endpoint ID}`; its parent is the audio device
  whose driver created it (e.g. `USB\VID_046D&PID_085C&MI_02\…`, `HDAUDIO\FUNC_01&VEN_10DE…`). Nodes of devices
  that aren't connected are found with `CM_LOCATE_DEVNODE_PHANTOM` and still carry their driver properties. Old
  endpoints whose device was removed from Device Manager have no node at all; the tab says so.
- **Connection** is the first real bus walking up from the device. A Bluetooth LE Audio headset (the desktop's Jabra
  Evolve3 75, 2026-11-25) is `APXENUM\{2D68507B…}&AUDIO\…` (Windows' audio proxy) under `BTHLE\DEV_…`, whose chain
  ends at the USB Bluetooth adapter: without that rule the tab showed the adapter's USB details as the headset's.
- **USB speed** comes from the hub: `IOCTL_USB_GET_NODE_CONNECTION_INFORMATION_EX` on the hub's interface
  (`GUID_DEVINTERFACE_USB_HUB`) with the port number (`DEVPKEY_Device_Address` of the USB device), opened with
  `GENERIC_WRITE` like USBView; no admin rights needed. `_EX_V2` adds "SuperSpeed capable", used for the "connected
  slower than it could" note. Checked on the desktop: C922 high speed / USB 2.0, the MSI lighting controller and the
  ASUS Bluetooth adapter full speed / USB 1.1, which matches their descriptors.
- **APOs** are in `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\MMDevices\Audio\{Render|Capture}\{guid}\FxProperties`,
  values named `{fmtid},pid`: `PKEY_FX_*` (d04e05a6…: 5 SFX, 6 MFX, 7 EFX, 13–15 their composite lists, 1/2 legacy LFX
  / GFX, 3 the enhancements page), `PKEY_*FX_ProcessingModes_Supported_For_Streaming` (d3993a3f…, 5–7) and
  `PKEY_AudioEndpoint_Disable_SysFx` (1da5d803…,5). Names: `HKLM\SOFTWARE\Classes\AudioEngine\AudioProcessingObjects`
  (`FriendlyName`), else the COM class name, else the driver package. Packaged APOs (Realtek's on this desktop) are in
  neither registry list; their `FxProperties` hold `{APO CLSID},100` = the instance ID of the package's software device
  (`SWD\DRIVERENUM\…#REALTEKAPO…`, "Realtek Audio Effects Component"), which also names Windows 11 effect packs that
  sit in no slot (Voice Clarity on the C922).
- Reading one endpoint takes ~7 ms (27 not-present endpoints measured from PowerShell through the app's assembly).
- Not checked: the laptop (Jabra Link 390, Intel array, Realtek HD Audio), a device with a Device Manager problem, a
  SuperSpeed device held back by a USB 2 port, classic (non-LE) Bluetooth headsets.

## Visual refresh (2026-11-25)
Branch `feature/visual-refresh`, one commit per item of "Visual refresh" in [TODO.md](../TODO.md). The layout stays:
device list on top, details below.

- **Theme.** `Theme.xaml` is merged in `App.xaml`, not in the window: tooltips live in their own popup window. It holds
  the palette (three surface levels, three text colours, accent / positive / caution / negative), the type scale and
  the icon sizes. Views use only those names. Tracks and outlines are translucent white, so they also show up on a
  selected row.
- **Title bar.** `Interop/WindowTheme.cs` sets DWM attributes 20 (dark mode, Windows 10 2004+) and 35 (caption colour,
  Windows 11). Older versions return an error, which is ignored.
- **PROPERTIES** was an unstyled `ListView`; now a `DataGrid` in the event log's style, with resizable columns
  (`LogHeaderResizable` has the `PART_RightHeaderGripper`).
- **Sections.** The default view is grouped by `DeviceRowViewModel.SectionText`. A grouped DataGrid doesn't recompute
  star columns when its viewport changes, so `DeviceGrid_ScrollChanged` sets the DEVICE column's width.
- **Icons** come from the form factor (`DeviceRowViewModel.IconGlyph`), so they are only as good as the driver: the Jabra
  Evolve3 75 dongle reports "Speakers" and "Microphone".
- **Header card.** The role pills are a `ContentControl`, because a `ContentPresenter`'s own data context is its content
  and triggers on it would resolve against the wrong object.
- **Level graph.** The gradient brushes use absolute mapping, so a colour always means the same dB value. Peak hold:
  1.5 s, then 20 dB/s. Smooth scrolling animates one `TranslateTransform` per sample.
- **Tabs.** With no tab open, the side-panel column shrinks to the tabs (Auto width) and the graph spans all columns
  (`ApplySidePanelLayout`).
- Verified by screenshot at 1200×800 and at 900 px width (the minimum), with the side panel open and closed; filters,
  tabs and selection by UI Automation; tooltips and the graph's loud levels in a scratch harness with synthetic
  samples; the test clip on a *muted* output (its meters still move). CPU use with the graph running: 4.7 % of one core
  before, 3.8 % after (the difference is noise).
- Not checked: dragging the splitters and the property columns, Windows 10, Windows high
  contrast mode, "Copy ID" (it would have overwritten the maintainer's clipboard).

## Side-panel tabs (2026-11-11)
Superseded in looks by "Visual refresh" above: the tabs are horizontal and labelled now; the rest still applies.

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

## Small follow-ups (2026-11-23)
Built on a second machine: a desktop with Realtek USB Audio, an ASUS VP28U
monitor (NVIDIA HDMI audio), a Logitech C922 webcam mic, and not-present
Jabra Evolve3 75 / Link 370 / Link 380 endpoints.

- **Call-mode hint for form-factor-4 headset mics (N1).**
  - `DeviceEnumerationService` reads `DEVPKEY_Device_ContainerId`
    (`{8c7ed206-…},2`, VT_CLSID) for every endpoint. A capture endpoint with
    form factor 4 gets `DeviceInfo.LinkedOutputName` when a render endpoint
    has the same container ID; an active one is preferred.
  - The container `{00000000-0000-0000-ffff-ffffffffffff}` is ignored. Windows
    gives it to every built-in device, so on this machine the Realtek mic
    (form factor 4) and the Realtek headphones (form factor 3) share it.
  - The output's form factor isn't checked. The Jabra Evolve3 75 reports
    "Speakers" (1) and "Microphone" (4) for one headset.
  - The tooltip names the linked output and says "if that is a headset".
    A USB monitor with a webcam mic would also match, so the wording is
    hedged.
  - Probe through the app's own `DeviceEnumerationService`: 31 endpoints in
    21 ms. "Microphone (Jabra Evolve3 75)" was linked to "Speakers (Jabra
    Evolve3 75)". The Realtek mics and the C922 webcam mic were not linked.
    Headsets with form factor 5 were flagged as before.
- **Mic released while minimised.** `MainViewModel.IsWindowMinimized`, set
  from `Window.StateChanged`, makes `UpdateCaptureStream` pass null to
  `SetCaptureStreamDevice`. Tests and A/B recordings open their own streams,
  so they are not affected.
  - Verified with UI Automation and a session probe of the app's PID: with the
    C922 mic selected, its capture session was active; after minimising there
    was none; after restoring it was active again.
- **Event log on disk (N2).** `Services/EventLogFile.cs`: one JSON Lines file
  per local day, written by `EventLogExporter.ToJsonLine`.
  - Merging replaces entries, so `EventLogViewModel` keeps an "unsaved" list
    (merged entries replace theirs by reference: `AudioEvent` is a record).
    Every 2 s, the leading entries whose last merge is more than 3 s old are
    written. Merge windows are at most 1 s, so a written entry is final, and
    stopping at the first unsettled entry keeps the file in order.
  - Writes are chained tasks on the thread pool. On close, everything left is
    written and the app waits up to 2 s. The first write error goes to the
    status bar; later ones are ignored.
  - "Clear" empties the panel only. The file keeps everything.
  - At startup, files dated more than 13 days before today are deleted (14
    days are kept).
  - Verified: the start entry was written after ~5 s; closing the app within
    1 s of starting still wrote its line; a file dated 14 days ago was
    deleted and one dated 13 days ago was kept.
- **Not yet verified (needs manual testing):**
  - The hint with a form-factor-4 headset mic in a real call (no such headset
    was connected).
  - The log across midnight (the file is chosen per entry, so it should split).

## .NET 10 migration (2026-11-23)
.NET 8 support ended on 2026-11-10; .NET 10 is the current LTS (supported until November 2028).

- **Changes.** Only the target frameworks changed: `net10.0-windows` for the app and the publish profile (also its
  `PublishDir`), `net10.0` for `tools/PropertyKeyNameGenerator`, and `dotnet-version: 10.0.x` plus the publish path
  in CI. No code changes, no package updates: NAudio 2.2.1 and the Win32 metadata package work as they are.
- **Build.** SDK 10.0.404: 0 warnings, 0 errors for the app and the generator. The generator's output
  (`PropertyKeyNames.g.cs`, `KnownGuidNames.g.cs`) is byte-identical. A CI-style publish
  (`-p:PublishProfile=win-x64-selfcontained -p:Version=…`) gave a 75.9 MB single-file exe with the version stamped
  in.
- **Verified in the app** (desktop machine, UI Automation and screenshots; silent: both outputs were already muted at
  0 %, only the C922 webcam mic was opened):
  - the process loads `coreclr.dll` from the `net10.0-windows` output;
  - 31 endpoints enumerated; volume and mute read back as the real endpoint state (checked separately with NAudio);
  - PROPERTIES, EVENTS, PROCESSES and RAW RECORDING tabs open; the property table and live levels fill in;
  - selecting the C922 mic opens its meter stream, minimising releases it, restoring reopens it;
  - Record A/B captured Raw, Call and Standard at the same time (our IAudioClient2 interop), then playback was
    stopped;
  - the event log file got its start line; the app exits with code 0.
  - Screenshots of the .NET 8 and .NET 10 builds in the same state were identical.
- **Verified by the maintainer:** recording and playback, and Set Both (default + communications). The CI workflow
  built, tagged and released the .NET 10 exe. It warned that `actions/checkout@v4` and `actions/setup-dotnet@v4`
  target the deprecated Node.js 20, so they were moved to `checkout@v7` and `setup-dotnet@v6` (both Node.js 24). Our
  inputs (`fetch-depth`, `dotnet-version`) didn't change. Checkout v6+ keeps the credentials in a separate file,
  which still works for the workflow's `git push` of the tag.
- **Not verified:** volume changes, and the laptop hardware (Intel array, Jabra Link 390).
