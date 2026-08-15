<div align="center">

# FeatherWall 🪶

### Live wallpapers for Windows that don't eat your laptop.

Any video or image on your desktop. A clock that actually looks designed.
One process, no browser engine, no launcher, no account.

[![CI](https://github.com/RitvikDayal/featherwall/actions/workflows/ci.yml/badge.svg)](https://github.com/RitvikDayal/featherwall/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)
[![Platform](https://img.shields.io/badge/platform-Windows%2010%20%2F%2011-0078d4)](#install)
[![.NET](https://img.shields.io/badge/.NET-10-512bd4)](https://dotnet.microsoft.com/download)

</div>

![FeatherWall running a still wallpaper with the clock widget](docs/media/hero.jpg)

---

## Look at it

Every screenshot below is a real capture off a 2560×1600 display at 150% scaling. No mockups,
no renders, no Photoshop. The wallpapers are the ones shipped in FeatherWall's own gallery, so
you can reproduce any of these in about four clicks.

### Video, playing live on the desktop

![Aurora time-lapse playing as a live wallpaper](docs/media/live-video.jpg)

<sub>*Time-lapse of Aurora Borealis in Norway* — Christer Olsen, CC0, via the built-in gallery.</sub>

### The clock takes any font you own

Nine anchor positions, any installed typeface, any size, any colour, optional seconds, optional
date, optional hairline rule. Pick a font and it renders in that font while you're picking it.

| Bahnschrift Light · bottom-left · seconds | Cascadia Mono · top-right |
|---|---|
| ![Clock in Bahnschrift Light](docs/media/clock-bahnschrift.jpg) | ![Clock in Cascadia Mono](docs/media/clock-cascadia.jpg) |

| Georgia · centred |
|---|
| ![Clock in Georgia, centred](docs/media/clock-georgia.jpg) |

<sub>Hubble Ultra Deep Field (NASA/ESA, public domain) · *The Earth seen from Apollo 17* (NASA, public domain) · *Alone in the unspoilt wilderness* — David Marcu, CC0.</sub>

### Settings that apply while you watch

A live miniature of the real clock renderer sits at the top of the panel. It is driven by the
same drawing code that paints your desktop, so what you see there is what lands on the wall.
It follows your Windows light/dark setting too.

| Clock | Wallpaper | Behaviour |
|---|---|---|
| ![Clock settings](docs/media/settings-clock.png) | ![Wallpaper settings](docs/media/settings-wallpaper.png) | ![Behaviour settings](docs/media/settings-behaviour.png) |

### Everything from the tray

<div align="center">

![FeatherWall tray menu](docs/media/tray-menu.png)

</div>

---

## What it costs you

Measured, not estimated. Every row came out of a sampled 30–40 second window on a running
instance, on this machine:

> **Test rig** — Windows 11 Pro 26200, RTX 5090 laptop, 24 logical cores, 2560×1600 @ 150%
> scaling, .NET 10, Release build, single monitor.

| State | CPU (whole machine) | CPU (one core) | Memory (working set) | GPU |
|---|---|---|---|---|
| **Still image** | **0.03 %** | 0.8 % | **151 MB** | 0 % |
| **Video, auto-paused** | **0.01 %** | 0.2 % | 246 MB | 0 % |
| **Video, 1080p H.264 playing** | **1.0 %** | 24 % | 199 MB | 50 % |
| **Video, 4K H.264 playing** | **0.9 %** | 22 % | 270 MB | 73 % |

The number in bold is what Task Manager shows you. The per-core column is the same measurement
divided differently, because a 1 % figure on a 24-thread laptop deserves the honest denominator
next to it.

Three things worth pulling out of that table:

**A still wallpaper is genuinely free.** It's drawn once and then nothing happens. 0.03 % CPU,
no GPU, no timers. If you only ever use images, FeatherWall costs you 151 MB of RAM and nothing else.

**Pausing works, and it's the whole battery story.** Put a fullscreen app in front, lock the
session, connect over RDP or flip on battery saver, and playback stops dead: 0.01 % CPU, 0 % GPU.
Not throttled — stopped. Rendering is fully event-driven, so a paused wallpaper has no timers,
no frames and no wakeups to schedule.

**It stays where it starts.** Memory and threads were flat across 30 consecutive full re-applies
(the display-change path — docking, monitor hot-plug, explorer restart): 250 → 278 MB, 56 → 63
threads. This used to be the opposite; see [Honest notes](#honest-notes).

<sub>On battery specifically: the *system* draw sat at 31–33 W across all three states on this laptop, which is within noise for isolating one process. FeatherWall's contribution isn't separable from that figure, so there's no watts number here — the mechanism above is the honest version of the claim.</sub>

---

## Why this exists

Wallpaper engines have a habit of being enormous. Multiple processes, mixed runtimes, an
embedded browser to draw a looping MP4, a launcher, an account, a store. And then Windows 11
24H2 rewrote how the desktop is composed and a lot of them started painting black rectangles.

FeatherWall is the other bet.

**One process. One runtime.** Raw Win32, Direct3D 11 and DirectComposition, with WinRT
MediaPlayer in frame-server mode for decode. No WPF, no WinUI, no WebView2, no CEF, no player
subprocess. `featherwall.exe` is the whole application.

**Built for the current Windows desktop.** It detects and supports both desktop topologies —
the classic `WorkerW` layout and the Windows 11 24H2+ "raised desktop" where Progman opts out
of GDI redirection — and renders through DirectComposition, the same path the shell itself uses.
[`docs/architecture.md`](docs/architecture.md) is the interesting part of this repo.

**No account, no telemetry, no network unless you ask.** The only time FeatherWall touches the
internet is when you click a gallery item to download it.

---

## Install

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download) on Windows 10 1809 or newer.

```powershell
git clone https://github.com/RitvikDayal/featherwall
cd featherwall
dotnet build -c Release
.\src\FeatherWall\bin\Release\net10.0-windows10.0.19041.0\featherwall.exe
```

A feather appears in your tray. Right-click it → **Set wallpaper…** for your own file, or
**Gallery** to pull something public-domain. Double-click the tray icon for the settings panel.

Useful flags:

| Flag | Does |
|---|---|
| `--settings` | Open the settings panel |
| `--diag` | Print the detected desktop topology and monitor layout |
| `--exit` | Stop the running instance |

> **SmartScreen:** releases are unsigned today, and a program that reaches into shell windows
> is exactly the shape heuristics dislike. Build from source, or *More info → Run anyway*.
> Code signing via SignPath Foundation is on the roadmap.

---

## Features

- 🎞 **Video wallpapers** — MP4/H.264 out of the box, plus MOV/AVI/WMV/WebM. Seamless
  pipeline-level looping, muted by default, hardware-decoded.
- 🖼 **Image wallpapers** — PNG, JPEG, BMP, TIFF, animated GIF.
- 🕐 **A clock worth looking at** — large light-weight time, hairline rule, small dimmed date.
  Any installed font, nine anchors, 12/24-hour, optional seconds, adjustable size, colour,
  opacity and shadow. Click-through by construction.
- ⚙️ **Live settings panel** — every control applies instantly, with a true preview. Dark and
  light, following Windows.
- 🌄 **Public-domain gallery** — curated CC0 and public-domain media from NASA and Wikimedia
  Commons. No API keys, no server, no tracking.
- ⏸ **Auto-pause** — fullscreen apps, session lock, remote desktop, battery saver.
- 🖥 **Per-monitor wallpapers**, Fill / Fit / Stretch, DPI-aware to the pixel.
- 🧰 **Tray-first** — full right-click menu; JSON config for everything else.
- ♻️ **Self-healing** — re-attaches after explorer.exe restarts or the shell rebuilds the
  wallpaper layer, and puts your original wallpaper back when it exits.

---

## The gallery is deliberately boring

Pexels, Unsplash and Pixabay all explicitly prohibit wallpaper apps in their terms. So
FeatherWall doesn't touch them. The gallery is a static manifest of genuinely public-domain and
CC0 media — NASA imagery, Wikimedia Commons — downloaded straight from the source when you click
it. No API key, no backend, no grey zone.

8 of the 11 entries carry a SHA-1 that is verified after download, and a mismatch is a hard
failure. The other three — the NASA-hosted videos — are unpinned today, so they're downloaded
without checksum verification. Fixing that is on the list.

Contributions of CC0 or public-domain loops are welcome.

---

## Configuration

The tray menu and settings panel cover the common cases. Everything lives in
`%APPDATA%\FeatherWall\config.json`:

```json
{
  "wallpapers": [{ "monitor": "*", "path": "C:\\wallpapers\\aurora.webm" }],
  "fit": "Fill",
  "muteVideo": true,
  "volume": 0.3,
  "clock": {
    "enabled": true,
    "anchor": "TopCenter",
    "marginX": 48,
    "marginY": 96,
    "twentyFourHour": true,
    "showSeconds": false,
    "showDate": true,
    "separator": true,
    "fontSize": 150,
    "fontFamily": "Segoe UI Light",
    "color": "#F0FFFFFF",
    "shadow": true
  },
  "pause": { "onFullscreen": true, "onBatterySaver": true, "onRemoteSession": true }
}
```

`monitor` is a device name (`\\.\DISPLAY1`) or `"*"` for all of them. Logs are at
`%LOCALAPPDATA%\FeatherWall\featherwall.log`.

**Codecs.** FeatherWall uses the OS media pipeline, so H.264/MP4 works everywhere. HEVC needs
the paid *HEVC Video Extensions*; VP9 and AV1 need the free extensions from the Microsoft Store.
Gallery entries are labelled when they need one.

---

## Honest notes

Things that are true and worth saying out loud.

**CPU while a video plays is ~1 % of the machine, which is ~24 % of one core.** That's the cost
of copying every decoded frame onto the composition surface and presenting it. It is not zero.
If that bothers you, a still image is free, and auto-pause means the video isn't running most of
the time anyway.

**The re-apply leak is fixed, and it was bad.** Until recently every full re-apply — display
change, monitor hot-plug, explorer restart — stranded about 27 threads and 24 MB permanently,
because a disposed WinRT `MediaPlayer` keeps its Media Foundation worker threads until the
runtime collects it. A laptop that docked a few times a day would drift past 500 MB and 129
threads. Now the pipeline is reclaimed explicitly at teardown, and 30 back-to-back re-applies
move memory by under 30 MB.

**Quitting used to hang.** `SystemParametersInfo(SPI_SETDESKWALLPAPER)` with `SPIF_SENDCHANGE`
broadcasts to every top-level window and waits for each one to answer, and it ran on the UI
thread during shutdown. One busy app that wasn't pumping messages and `featherwall.exe` would
never exit. It now writes the setting without the blocking broadcast and notifies asynchronously.

**What hasn't been verified on real hardware:** multi-monitor layouts, the classic `WorkerW`
topology (this box is 24H2+ raised desktop), and recovery from an explorer.exe restart. The code
paths exist and are unit-tested where they can be; they have not been exercised on hardware here.

---

## Roadmap

- Slideshows and playlists; more widgets (battery, now-playing)
- Drag-to-position widget editing
- `IMFMediaEngine` backend and NativeAOT single-exe publish
- Optional mpv backend for exotic codecs
- winget package, signed releases, auto-update
- ARM64 CI leg

---

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md). `dotnet test` should be green before you open a PR —
it's 54 tests and takes under a second.

## License

[MIT](LICENSE). Gallery media is public domain or CC0, documented per entry in
[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
