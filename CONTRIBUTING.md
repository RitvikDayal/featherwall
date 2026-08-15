# Contributing

Issues and pull requests are welcome. This is a small project, so nothing here is bureaucratic.

## Getting set up

You need the .NET 10 SDK on Windows 10 1809 or newer. There is no other setup, no submodules and
no code generation step.

```powershell
git clone https://github.com/RitvikDayal/featherwall
cd featherwall
dotnet build
dotnet test
```

The test suite is 54 tests and finishes in well under a second, so there is no excuse for not
running it. To try your changes on the actual desktop:

```powershell
dotnet build -c Release
.\src\FeatherWall\bin\Release\net10.0-windows10.0.19041.0\featherwall.exe
```

`featherwall --exit` stops a running instance, which you will want before rebuilding because the
running process holds a lock on the executable.

## Where to start

The [good first issue](https://github.com/RitvikDayal/featherwall/labels/good%20first%20issue)
label is where to look first. Those are scoped small and do not require understanding the
rendering pipeline.

The [help wanted](https://github.com/RitvikDayal/featherwall/labels/help%20wanted) label is mostly
hardware the project does not have. Multi-monitor setups and Windows 10 machines running the
classic desktop topology are genuinely the most useful thing anyone can bring right now, because
those code paths are written and unit-tested but have never run on real hardware.

If you want to understand the engine before touching it, read
[docs/architecture.md](docs/architecture.md). It explains why the wallpaper layer works the way it
does on Windows 11 24H2 and later, which is the part of this codebase that will surprise you.

## Ground rules

Keep it feather-light. No WPF, WinUI, WebView2 or Electron, no player subprocesses, and no new
runtime dependencies without a good argument. Vortice is currently the only third-party package,
and the lightweight claim is the whole product. If a change adds weight, the pull request needs to
say what it buys.

Pure logic goes in a test. Layout maths, pause policy, config handling and manifest parsing all
have unit tests in `tests/FeatherWall.Tests`, and new logic of that kind should arrive with them.

Interop and rendering changes need to say where you ran them. Paste the output of
`featherwall --diag` and your display scaling into the pull request. The classic `WorkerW` layout
and the 24H2+ raised desktop behave very differently, and DPI scaling has already caused one
confidently wrong fix in this repository's history. Tests cannot catch either category, so the
only real check is somebody running it and looking at the screen.

Match the surrounding style. Terse comments where the code cannot explain itself, and none where
it can.

## Contributing gallery wallpapers

Pull requests that edit `src/FeatherWall/Gallery/gallery.json` need all of the following:

1. A licence of CC0 or public domain, verifiable on the linked `sourcePage`. Wikimedia Commons
   and NASA are the usual sources. Pexels, Unsplash and Pixabay all prohibit wallpaper apps in
   their terms, so nothing from those can be accepted regardless of the individual licence.
2. A direct, stable download URL that needs no API key.
3. `sha1` filled in for Commons files, which the Commons API gives you, plus accurate `bytes`,
   `width` and `height`.
4. Content that is calm and loops well, at 720p or better, with no logos or watermarks, and
   `mfNative` set honestly (H.264 in MP4 is true, VP9 and AV1 and WebM are false).

Remember that whatever you add is going to sit behind somebody's work for eight hours at a time.
Restraint is a feature.

## Pull requests

Open an issue first for anything large, so you do not spend a weekend on something that turns out
to conflict with the design. Small fixes can go straight to a pull request.

The template asks which topology you tested on. Please fill it in rather than deleting it.

Everyone participating is expected to follow the [Code of Conduct](CODE_OF_CONDUCT.md).
