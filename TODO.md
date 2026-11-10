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
- [ ] "Refresh" button that re-enumerates devices and diffs against current list (service ready; UI wiring in MainWindow task)
- [ ] Auto-refresh on Windows device change notifications (plug/unplug events) (service raises `DevicesChanged`; UI wiring in MainWindow task)

## Default device roles
- [x] Show current default device for Render / Multimedia role
- [x] Show current default device for Render / Communications role
- [x] Show current default device for Capture / Multimedia role
- [x] Show current default device for Capture / Communications role
- [x] Set a device as default (Console + Multimedia roles) — `DefaultDeviceService` (UI button wiring in MainWindow task)
- [x] Set a device as default **Communications** device — `DefaultDeviceService` (UI button wiring in MainWindow task)
- [ ] Visual indicator (badge/icon) in the device list for current defaults (UI task)

## Device details panel
- [x] Friendly name, endpoint ID, data flow, device state
- [x] Default format: sample rate, bit depth, channel count (shared-mode mix format)
- [x] Shared-mode mix format vs. exclusive-mode supported formats
- [x] Raw property store dump (all discoverable `PROPERTYKEY` values, via `device.Properties`)
- [ ] Details panel rendering in the UI (DeviceDetailsService is ready; wiring in MainWindow task)

## Testing devices
- [x] "Test Output" — play a generated 440 Hz tone through the selected
      output device
- [x] "Test Input" — record a few seconds from the selected input device and
      play it back through the current (or chosen) default output device
- [x] Clear pass/fail + error reporting if a device fails to open/start
      (`TestOutcome`; UI surfacing in MainWindow task)

## Live level metering
- [x] Real-time peak-level meter per device using `IAudioMeterInformation`
      (works for every device without opening an exclusive stream) —
      `LevelMeterService`
- [x] Rolling graph (time-series, not just a single bar) control —
      `LevelGraph` (used both as the big detail-panel graph and the small
      per-row meter; wiring into MainWindow pending)
- [x] Simultaneous mini-meters next to every device in the list (same
      `LevelGraph` control, small size; wiring into MainWindow pending)
- [x] Per-channel peak levels where the endpoint reports more than stereo —
      `LevelSample.ChannelPeaks` (UI surfacing pending)

## Packaging
- [ ] `dotnet publish` profile for a single self-contained `.exe`
- [ ] App icon / metadata (version, product name)

## Not yet done / follow-ups
- [ ] Manual verification pass on a real Windows machine (blocked: this
      environment is Linux-only, see README "Testing" section)
- [ ] Automated UI tests (no Windows test runner available in this container)
- [ ] Code signing of the published executable
