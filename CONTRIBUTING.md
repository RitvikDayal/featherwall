# Contributing

Issues and PRs are welcome.

## Building

.NET 10 SDK on Windows 10 1809+ → `dotnet build`, `dotnet test`. No other setup.

## Ground rules

- **Keep it feather-light.** No WPF/WinUI/WebView2/Electron, no player subprocesses, no new runtime dependencies without a very good reason. The lightweight claim is the product.
- Pure logic (layout math, pause policy, config, manifest handling) gets unit tests in `tests/FeatherWall.Tests`.
- Interop changes must state which desktop topology they were verified on (`featherwall --diag` output) — classic WorkerW and 24H2+ raised desktop behave very differently; see [docs/architecture.md](docs/architecture.md).
- Match the existing style: terse comments only where the code can't speak for itself.

## Contributing gallery wallpapers

PRs editing `src/FeatherWall/Gallery/gallery.json` must satisfy all of:

1. License is **CC0 or public domain**, verifiable on the linked `sourcePage` (Wikimedia Commons or NASA preferred).
2. Direct, stable, keyless download URL.
3. `sha1` filled in for Commons files (the API provides it); `bytes`, `width`, `height` accurate.
4. Calm, loopable content; ≥720p; no logos or watermarks; `mfNative` set honestly (H.264 MP4 = true, VP9/AV1/WebM = false).
