# TODO — Windows Audio Device Debugger

Open items and roadmap. Finished features — everything already implemented
and tested by hand — are listed in
[planning/DONE-TODO.md](planning/DONE-TODO.md). Implementation and
verification notes for the larger features are in
[docs/design-notes.md](docs/design-notes.md).

## Project setup
- [ ] License file
- [ ] Code signing of the published executable

## Device details panel
- [ ] Name more undocumented keys — ~170 still show as raw `{fmtid},pid`; no
      public source documents them, so each needs its own confirmation

## Event timeline (N2)
- [ ] Tray mode, so events are recorded all day without the window open

## Raw recording (N4)
- [ ] Raw *playback* (render) path — not covered

## Roadmap

Planned features, specified in enough detail to implement. If you'd like to
work on one, please open an issue first to discuss it. Each feature should land
as its own commit, with this file (and the README, if user-visible) updated in
the same commit; once it is done, its items move to
[planning/DONE-TODO.md](planning/DONE-TODO.md).

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

### Ideas from the 2026-11-23 brainstorm
Not specified yet. Each needs a short spec (like N3 / N5) before it is built.

**Visual**
- [ ] **Mini meter window.** A small, always-on-top window with one level bar
      per active device, for watching during a call.
- [x] **Follow the Windows theme.** A light theme next to the dark one, chosen
      from the Windows app mode, plus high-contrast support.
      Done: palettes in `Views/Themes/Dark.xaml` and `Light.xaml`; with high
      contrast on, a palette from Windows' system colours (no translucency,
      the selected row outlined). `ThemeManager` follows changes while the
      app runs (title bar and level graph too); `--theme=light|dark|
      high-contrast` forces one. Unit tests check that the palettes define
      the same keys and that text meets 4.5:1 contrast (dark tertiary text
      was brightened from #8F8F96 to #9C9CA3 to pass on raised surfaces).
      Not tested with a real Windows contrast theme switched on.
- [x] **Row colours by state.** Muted, disabled, unplugged and not-present
      devices easy to tell apart in the list.
      Done (`DeviceRowViewModel.RowState`): muted → soft red outline;
      disabled → stronger red outline plus red tint; unplugged → soft yellow
      tint; not present → only the existing dimming (opacity 0.55, kept for
      every inactive row). The tint is drawn in the cell between the
      hover/selection background and the text, the outline in a copy of
      DataGridRow's template on top of the cells (no layout shift). High
      contrast: outlines only, no tints. Muted not yet seen on real hardware.
- [x] **Device grouping.** Group the endpoints of one physical device by
      container ID (`DEVPKEY_Device_ContainerId`), e.g. "Jabra Evolve3 75"
      with its Speakers and Microphone rows. Switch the device list from a
      flat list to a grouped view (collapsible groups; built-in devices in a
      "This PC" group).
      Done: a **By device** chip in the DEVICE header switches between
      OUTPUTS / INPUTS and one group per container; while grouped it reads
      **By input / output**, the grouping a click returns to (state lines then say
      "Output ·" / "Input ·"). Group names come from the endpoints: the
      interface name ("Jabra Link 380"), or the monitor name for HDMI /
      DisplayPort ("ASUS VP28U", not "NVIDIA High Definition Audio"). Groups
      with an active endpoint come first. Every group heading (sections too)
      collapses on click and stays collapsed across refreshes. Not kept
      between app starts (the app has no settings file yet).

**Functional**
- [ ] **Setup snapshots.** Save the whole audio setup (defaults, volumes, mute,
      formats, enhancements) as JSON, restore it, and diff two snapshots
      ("what differs between the working and the broken state?"). Overlaps
      with N5.
- [ ] **Latency numbers.** Show the device period and the engine period
      (`IAudioClient` / `IAudioClient3`), and add a round-trip test: play a
      click on an output, detect it on a mic, report the delay in ms.
      Explain what the number means for people, for example:
  - under ~10 ms: not noticeable, even when you hear yourself (monitoring);
  - ~20–30 ms: hearing yourself starts to sound like an echo; musicians
    playing along feel it;
  - audio more than ~45 ms behind the picture: lip-sync errors become
    noticeable (ITU-R BT.1359);
  - one-way delay in a call: up to ~150 ms is fine, beyond ~400 ms
    conversation breaks down (ITU-T G.114).
- [x] **Device inventory.** Driver name, version and date, USB VID/PID and
      USB speed, and the processing modules (APOs) installed per endpoint
      (`FxProperties` keys).
      Done as a fifth side-panel tab, **Driver** (read only while it is open):
  - Device: name, manufacturer, connection (the first real bus in the device
    tree, so a Bluetooth LE Audio headset reads "Bluetooth", not the USB
    adapter its radio hangs off), Device Manager status, hardware and
    instance ID.
  - Driver: description, provider, version, date, INF file and section,
    service, generic class driver or not.
  - USB (USB audio devices only): product, VID / PID, revision, composite
    interface, speed from the hub (IOCTL_USB_GET_NODE_CONNECTION_INFORMATION
    _EX and _EX_V2; works unelevated), declared USB version, port and hub.
  - Audio effects: every SFX / MFX / EFX (and composite, legacy, keyword and
    offload) slot in the endpoint's `FxProperties` key, named from the APO
    registration, the COM class or the driver package that installed it
    (`{APO CLSID},100`); effect packs; supported processing modes; whether
    enhancements are switched off.
  - Copy as text.
- [ ] **Bluetooth Connect / Disconnect on LE Audio.** The button (see
      planning/DONE-TODO.md) is enabled for LE Audio headsets too, but was only
      tried over Bluetooth Classic; check on the desktop's Evolve3 75.
- [ ] **Bluetooth battery level** for headsets paired directly with Windows
      (from the Bluetooth device's PnP properties). Doesn't apply to headsets
      behind a USB dongle.

**Measurement**
- [ ] **Signal generator.** Sine, sine sweep, pink noise, a polarity test and
      spoken "left"/"right" channel labels, per output and per channel.
- [ ] **Mic quality report.** After a mic test: noise floor, signal-to-noise
      ratio, clipped samples and DC offset, next to N3's bandwidth verdict.

**Privacy and security**
- [ ] **Mic and camera access history.** Which apps used the microphone (and
      camera) and when, from the `CapabilityAccessManager\ConsentStore`
      registry data Windows keeps, shown as a per-app timeline.
- [ ] **Live alert when an app opens the mic.** A notification (tray) when an
      app starts capturing, built on N1's session tracking.
- [ ] **Release hardening.** Code signing (see "Project setup"), GitHub
      build-provenance attestation for the exe, GitHub Actions pinned to
      commit SHAs, Dependabot, CodeQL for C#.
- [ ] **Least privilege.** The app runs unelevated. Detect whether it runs as
      administrator, and show features that need elevation only when they
      can work (or show them disabled, with the reason in the tooltip).
      If a feature needs admin rights, prefer a small elevated helper that
      runs one narrow command and exits.

**Code**
- [ ] **CsWin32 source generator** for new Win32/COM interop: declarations
      generated from the Win32 metadata instead of written by hand.
      Undocumented interfaces (`IPolicyConfig`) stay hand-written, and
      existing working interop is only converted when it is touched anyway.

### Other ideas
- [x] Automated tests for the pure-logic parts that don't need audio hardware:
      event log merging and filters, exporters, `LevelScale`, `ChannelLayout`
      (an offline harness covered the event log during development).
      Done: `tests/AudioDeviceDebugger.Tests` (xUnit, 114 tests), also the
      property-value decoders, the daily log files, the channel conversion and
      the device rows' "in use" text; CI runs them on every push to `main` and
      on pull requests, which are built but not released
- [ ] A/B listening test for headsets in media vs call mode (pair speaker and
      mic by `DEVPKEY_Device_ContainerId`)
- [ ] Check that disabling a USB dongle's mic (Jabra Link 390) keeps the
      headset out of call mode. Disable / Enable itself is done (see
      planning/DONE-TODO.md); it needs no elevation, and a disabled Bluetooth
      Hands-Free mic leaves the headphones working.
- [ ] Toggle for Windows' "communications activity" ducking
