# TODO — Windows Audio Device Debugger

Feature checklist and roadmap. A checked item is implemented and was tested by
hand on Windows 11 on the development machine (see "Testing" in
[README.md](README.md)); there are no automated tests yet. Implementation and
verification notes for the larger features are in
[docs/design-notes.md](docs/design-notes.md).

## Project setup
- [x] Solution + WPF project (`.sln`, `.csproj`), .NET 8, NAudio 2.2.1
- [x] `.gitignore` for .NET build artifacts
- [x] `README.md` (features, build, usage), `TODO.md`, `docs/design-notes.md`,
      `CLAUDE.md` (notes for AI coding agents)
- [ ] License file
- [x] CI: GitHub Actions builds every push to `main` on `windows-latest` and
      tags it `vX.Y.Z` (version bump from conventional commits; see README
      "Versioning"); documentation-only pushes are skipped
- [ ] Prebuilt releases on GitHub (self-contained `.exe` attached to each tag)
- [ ] Code signing of the published executable

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
      side-panel tab strip, the level graph panel and the per-channel bars

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
- [x] Layout: device list on top, details below (resizable); the property
      table is the PROPERTIES side-panel tab (collapsed by default) and the
      property store is only read while it is open
- [x] Human-readable property names: compiled-in table of all ~1,550 documented
      PKEY_/DEVPKEY_ keys generated from Win32 metadata
      (`tools/PropertyKeyNameGenerator` → `Interop/PropertyKeyNames.g.cs`) plus a
      manual table for confirmed undocumented keys; unknown keys keep their UID
- [ ] Name more undocumented endpoint keys (~185 distinct on the development
      machine, e.g. `{b3f8fa53-0004-438e-9003-51a46e139bfc},*`) once their meaning
      is confirmed
- [ ] Decode property *values* to names (e.g. form factor 4 → "Microphone")
- [x] Raw-capture support (`RawProcessingSupported`) shown for inputs

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
      for the strip (see "Raw recording" below)

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

## Apps using a device (N1)
- [x] Per-endpoint audio sessions, polled every 500 ms (`AudioSessionService`)
- [x] Device row subtitle "· In use by Webex" (+N) while another app streams;
      tooltip lists every app session with PIDs
- [x] Call-mode hint on headset mic rows that are in use (form factor 5/6)
- [x] PROCESSES side-panel tab: app, process · PID, Streaming/Idle, app
      volume (volume mixer) and a live level per session
- [ ] Call-mode hint for headsets whose mic reports form factor 4
      ("Microphone"), e.g. the Jabra Link 390 — match the mic to a headset
      output by container ID

## Event timeline (N2)
- [x] EVENTS side-panel tab: devices added/removed/state changes, default
      changes, volume/mute (Windows vs this app), tests, apps starting and
      stopping streams, optional property changes
- [x] Bursts merged (default-change callbacks, slider drags), "was X" for
      previous defaults, names kept for removed devices
- [x] Filters by category and "selected device only"; Copy; Export… as CSV,
      Markdown or JSON Lines
- [ ] Persist the log to disk (proposal: JSON Lines in
      `%LOCALAPPDATA%\AudioDeviceDebugger\events-YYYY-MM-DD.log`, kept 14 days)
- [ ] Tray mode, so events are recorded all day without the window open

## Raw recording (N4)
- [x] Own IAudioClient2 interop (NAudio can't set stream categories or the
      RAW option) — `Interop/AudioClient2.cs`, `Services/ModeCapture.cs`
- [x] RAW RECORDING side-panel tab: **Record A/B** captures the selected mic
      as Raw, Call (communications category, what Teams/Webex receive) and
      Standard *at the same time*, then plays Raw and Call back to back
- [x] Per take: level, format, duration, per-channel RMS/peak, and the effects
      Windows reports for the stream (echo cancellation, noise suppression, …;
      Windows 11)
- [x] Play any take whole, or pick it for the channel strip to hear single
      channels / mic-array capsules
- [ ] Raw *playback* (render) path — not covered

## Side panel
- [x] Four tabs at the right edge — PROPERTIES, EVENTS, PROCESSES, RAW
      RECORDING; one open at a time, clicking the open tab collapses it
- [x] Tab labels hide (icons only) when the strip is too short for all four
- [ ] Cosmetic: the Role column truncates "Default + Comms" to
      "Default + Comm" (column width 95)

## Roadmap

Planned features, specified in enough detail to implement. If you'd like to
work on one, please open an issue first to discuss it. Each feature should land
as its own commit, with this file (and the README, if user-visible) updated in
the same commit.

### N3. Call-mode detection from a recording (bandwidth analysis)
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
    from real headsets, laptop mic arrays and USB mics.
- [ ] **Hook-up.** Run the analysis in `RunTestAsync` when an input test
      completes, using the kept `RecordedTake`. Run it on a pool thread (it's
      pure math), and store the result per device next to the kept takes (`_takes`).
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
  - On hardware (someone has to speak during a mic test): the Intel
    array should read full band, and the Jabra headset mic presumably
    wideband. Record the measured values in docs/design-notes.md.
- **Open questions:**
  - Is the spectrum plot wanted, or only the one-line verdict?
  - Should "Record only" be added?

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
- **Open questions:**
  - Default format (Markdown or JSON)?
  - Should "Include all properties" be on by default?
  - Should redaction be on by default?
  - Include driver details (driver version/date, USB VID/PID)
    now, or later?

### Other ideas
- [ ] Automated tests for the pure-logic parts that don't need audio hardware:
      event log merging and filters, exporters, `LevelScale`, `ChannelLayout`
      (an offline harness covered the event log during development)
- [ ] A/B listening test for headsets in media vs call mode (pair speaker and
      mic by `DEVPKEY_Device_ContainerId`)
- [ ] Disable a headset's mic endpoint so apps can't switch it into call mode
      (`IPolicyConfig::SetEndpointVisibility`; check whether it needs
      elevation)
- [ ] Toggle for Windows' "communications activity" ducking
- [ ] Close the selected mic's meter stream while the window is minimised
