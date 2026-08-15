## What this changes

<!-- One or two sentences. Link the issue if there is one. -->

## Verified on

<!-- Interop, rendering and DPI changes MUST fill this in — these paths behave differently
     per topology and per scaling, and unit tests cannot cover them. -->

- `featherwall --diag` topology: <!-- RaisedDesktop / ClassicWorkerW -->
- Display(s) and scaling: <!-- e.g. 2560x1600 @ 150%, single monitor -->
- Wallpaper used: <!-- e.g. 1080p H.264 MP4 / PNG still -->

## Checks

- [ ] `dotnet test` is green
- [ ] `dotnet build -c Release` has no new warnings
- [ ] Pure logic changes have unit tests in `tests/FeatherWall.Tests`
- [ ] I ran the app and looked at the actual desktop, not just the tests
- [ ] No new runtime dependency (see the ground rules in CONTRIBUTING.md)
