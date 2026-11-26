# Done — Windows Audio Device Debugger

Finished features, moved here from [TODO.md](../TODO.md), which keeps the open
items and the roadmap. A checked item is implemented and was tested by hand on
Windows 11 on a development machine (see "Testing" in
[README.md](../README.md)); there are no automated tests yet. Implementation
and verification notes for the larger features are in
[docs/design-notes.md](../docs/design-notes.md).

## Project setup
- [x] Solution + WPF project (`.sln`, `.csproj`), .NET 10 LTS (.NET 8 until
      2026-11-23), NAudio 2.2.1
- [x] `.gitignore` for .NET build artifacts
- [x] `README.md` (features, build, usage), `TODO.md`, `docs/design-notes.md`,
      `CLAUDE.md` (notes for AI coding agents)
- [x] CI: GitHub Actions builds every push to `main` on `windows-latest` and
      tags it `vX.Y.Z` (version bump from conventional commits; see README
      "Versioning"); documentation-only pushes are skipped
- [x] Prebuilt releases on GitHub: each tag gets a release with the
      self-contained `.exe` and its SHA-256 checksum

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
- [x] Names for 7 undocumented endpoint keys whose meaning was confirmed
      against Windows' own PnP data (audio device name, hardware ID and driver;
      the endpoint's device instance ID and interface path), shown as
      "undocumented" with a note on how each was confirmed
      (see docs/design-notes.md, "Property names and values")
- [x] Readable property *values*: form factor ("Microphone (4)"), jack type and
      other GUIDs by their SDK names (264 generated from the Win32 metadata into
      `Interop/KnownGuidNames.g.cs`), channel masks, the 16.16 fixed-point
      default volume ("20 dB"), the microphone-array geometry, wave-format
      blobs ("48 kHz · 32-bit float · 2 ch (FL FR)"), version blocks, SYSTEMTIME
      and UTF-16 text in blobs, dates, and endpoint IDs resolved to names; the
      raw value stays in the tooltip
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

## Event timeline (N2)
- [x] EVENTS side-panel tab: devices added/removed/state changes, default
      changes, volume/mute (Windows vs this app), tests, apps starting and
      stopping streams, optional property changes
- [x] Bursts merged (default-change callbacks, slider drags), "was X" for
      previous defaults, names kept for removed devices
- [x] Filters by category and "selected device only"; Copy; Export… as CSV,
      Markdown or JSON Lines

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

## Side panel
- [x] Four tabs at the right edge — PROPERTIES, EVENTS, PROCESSES, RAW
      RECORDING; one open at a time, clicking the open tab collapses it
- [x] Tab labels hide (icons only) when the strip is too short for all four
- [x] Role column shows "Default" and "Comms" on separate lines (each with a
      tooltip), matching the name/subtitle lines; row height unchanged

## Runtime
- [x] Moved to .NET 10 LTS (2026-11-23): .NET 8 support ended on 2026-11-10.
      The app, the property-key generator, the publish profile and CI target
      .NET 10; no code changes were needed
      (see docs/design-notes.md, ".NET 10 migration")

## Small follow-ups (2026-11-23)
- [x] Call-mode hint for headsets whose mic reports form factor 4
      ("Microphone"), e.g. the Jabra Link 390 — the mic is matched to an
      output with the same container ID (`DEVPKEY_Device_ContainerId`); the
      built-in "this PC" container is ignored (N1)
- [x] Close the selected mic's meter stream while the window is minimised;
      it reopens on restore (live level metering)
- [x] Persist the event log to disk: JSON Lines in
      `%LOCALAPPDATA%\AudioDeviceDebugger\events-YYYY-MM-DD.log`, same format
      as Export… → JSON Lines; files older than 14 days are deleted at
      startup (N2)

## Visual refresh (v0.3.0, 2026-11-25)
Built on `feature/visual-refresh`, one commit per item, merged in PR #1. It
replaces the look described under "Device enumeration" and "Side panel"
above (Show: checkboxes, ROLE column, icon strip at the right edge); see
docs/design-notes.md, "Visual refresh".
- [x] **A1. Finish the dark theme.** Title bar, thin scrollbars, tooltips and
      the PROPERTIES list, which still used the light Windows look
- [x] **A2. One palette and type scale.** Three surface levels, one meter
      green, named text colours and four text sizes instead of hard-coded
      values
- [x] **B3. Role badges.** "Default" / "Comms" pills next to the device name,
      instead of a ROLE column
- [x] **C1. Single-line toolbar.** The Show: filters became "Also show" chips
      on the right; "Selected: …" went (the list and the details already say
      it)
- [x] **B1. OUTPUTS / INPUTS sections** in the device list, so the rows don't
      repeat "Output ·" / "Input ·"
- [x] **B2. Device-type icons.** A monochrome Segoe Fluent glyph per form
      factor (speakers, headphones, headset, microphone, digital, …)
- [x] **D1. Details header card.** Icon, name, badges and format chips; the
      endpoint ID behind a copy button
- [x] **D2. Better level graph.** Soft fill, peak-hold line, yellow and red
      zones near 0 dB, smooth scrolling
- [x] **D3. Horizontal tabs with labels** for the side panel, instead of the
      icon strip at the right edge

## Small fixes (2026-11-26)
- [x] The **By device** chip is labelled with the grouping a click switches
      to: it reads **By input / output** while grouped by device, without the
      chip's "on" highlight (which would make that label look current)
- [x] The **Raw recording** tab is greyed out while an output is selected
      (raw capture is for microphones); selecting an output closes the panel

## Bluetooth connect / disconnect (2026-11-26)
- [x] **Connect / Disconnect** toolbar button for devices paired with Windows
      over Bluetooth, like the Windows Sound settings: Windows' Bluetooth audio
      one-shot reconnect / disconnect (`KSPROPSETID_BtAudio`) sent to every
      Bluetooth endpoint of the headset, so A2DP and Hands-Free go together.
      Enabled only for Bluetooth endpoints that are Active (→ Disconnect) or
      Unplugged (→ Connect); a tooltip says why otherwise. Logged as a
      "Bluetooth" event. Tested with the Jabra Evolve3 75 (Bluetooth Classic)
      by the maintainer (see docs/design-notes.md)
