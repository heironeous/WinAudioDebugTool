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
- [x] Auto-refresh on Windows device change notifications (plug/unplug events)

## Default device roles
- [x] Show current default device for Render / Multimedia role
- [x] Show current default device for Render / Communications role
- [x] Show current default device for Capture / Multimedia role
- [x] Show current default device for Capture / Communications role
- [x] "Set Default" button per device row (Console + Multimedia roles)
- [x] "Set Comms" button per device row (Communications role)
- [x] Visual indicator in the device list for current defaults ("Role" column badge)

## Device details panel
- [x] Friendly name, endpoint ID, data flow, device state
- [x] Default format: sample rate, bit depth, channel count (shared-mode mix format)
- [x] Shared-mode mix format vs. exclusive-mode supported formats
- [x] Raw property store dump (all discoverable `PROPERTYKEY` values, via `device.Properties`)
- [x] Details panel rendering in the UI (name, ID, formats, property grid)

## Testing devices
- [x] "Test" button per device row — plays a tone for outputs, records 3s
      and plays it back for inputs
- [x] Clear pass/fail + error reporting if a device fails to open/start
      (shown in the bottom status bar)

## Live level metering
- [x] Real-time peak-level meter per device using `IAudioMeterInformation`
      (works for every device without opening an exclusive stream) —
      `LevelMeterService`
- [x] Rolling graph (time-series) for the selected device — `LevelGraph`
      control in the detail panel
- [x] Simultaneous mini-meters next to every device in the list — a live
      `ProgressBar` per row, all updating independently and concurrently
- [x] Per-channel peak levels where the endpoint reports more than stereo
      (`SelectedDeviceChannelPeaks` in `MainViewModel`, rendered as a row of
      vertical bars below the rolling graph in the detail panel)

## Packaging
- [x] `dotnet publish` profile for a single self-contained `.exe`
      (`Properties/PublishProfiles/win-x64-selfcontained.pubxml`)
- [x] App icon / metadata (version, product name) — `Assets/app.ico` +
      `app.manifest` (DPI awareness, OS compatibility, no elevation) wired
      up via `ApplicationIcon`/`ApplicationManifest` in the `.csproj`

## Not yet done / follow-ups
- [ ] Manual verification pass on a real Windows machine (blocked: this
      environment is Linux-only, see README "Testing" section)
- [ ] Automated UI tests (no Windows test runner available in this container)
- [ ] Code signing of the published executable
