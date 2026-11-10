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
- [ ] Show current default device for Render / Multimedia role
- [ ] Show current default device for Render / Communications role
- [ ] Show current default device for Capture / Multimedia role
- [ ] Show current default device for Capture / Communications role
- [ ] Button to set a device as default (Console + Multimedia roles)
- [ ] Button to set a device as default **Communications** device
- [ ] Visual indicator (badge/icon) in the device list for current defaults

## Device details panel
- [ ] Friendly name, description, enumerator/interface name
- [ ] Endpoint ID (GUID string) and adapter/container info
- [ ] Data flow (Render/Capture) and device state
- [ ] Default format: sample rate, bit depth, channel count
- [ ] Shared-mode mix format vs. exclusive-mode supported formats
- [ ] Raw property store dump (all discoverable `PROPERTYKEY` values)

## Testing devices
- [ ] "Test Output" — play a generated tone (sine sweep) through the selected
      output device
- [ ] "Test Input" — record a few seconds from the selected input device and
      play it back through the current default output device
- [ ] Clear pass/fail + error reporting if a device fails to open/start

## Live level metering
- [ ] Real-time peak-level meter per device using `IAudioMeterInformation`
      (works for every device without opening an exclusive stream)
- [ ] Rolling graph (time-series, not just a single bar) for the selected
      device
- [ ] Simultaneous mini-meters next to every device in the list
- [ ] Per-channel peak levels where the endpoint reports more than stereo

## Packaging
- [ ] `dotnet publish` profile for a single self-contained `.exe`
- [ ] App icon / metadata (version, product name)

## Not yet done / follow-ups
- [ ] Manual verification pass on a real Windows machine (blocked: this
      environment is Linux-only, see README "Testing" section)
- [ ] Automated UI tests (no Windows test runner available in this container)
- [ ] Code signing of the published executable
