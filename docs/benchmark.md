# Benchmark

What FeatherWall costs, measured by a script in this repository rather than by hand, so anyone
can reproduce it and so a regression is caught by a command instead of by a comment on Reddit.

```powershell
.\scripts\bench\Run-Bench.ps1 -Video <a video file> -Image .\docs\media\wall-hubble.jpg
```

**An empty cell means nobody has measured it.** It does not mean zero, and it does not mean
unknown-but-probably-fine. Rows are filled in only from a run that the harness accepted.

## The rule that makes these numbers worth anything

FeatherWall stops decoding when a window covers the desktop. That is the product working, and it
is also the easiest way to publish a flattering lie: measure while a window is in the way, record
a paused wallpaper, and call it the playing figure.

`Measure-App.ps1` therefore reads FeatherWall's own log and **refuses to report a row** unless the
pause state was the expected one for the entire sampling window — both that no transition happened
inside it, and that the state going in was the right one. A refused row throws instead of printing.

This is not hypothetical. The first run of this harness reported *"video playing — 3.3% video
decode"* while the log showed the wallpaper pausing and resuming three times inside the window.
The check exists because it caught that.

## Test rig

| | |
|---|---|
| OS | Windows 11 Pro, build 26200 |
| CPU | Intel Core Ultra 9 275HX, 24 logical cores |
| RAM | 63 GB |
| Display | 2560x1600 @ 240 Hz, 150% scaling, single monitor |
| GPU driving the display | Intel Arc integrated (2048 MB adapter RAM) |
| Other GPU | NVIDIA RTX 5090 Laptop, idle — FeatherWall never appears on it |
| Build | .NET 10, Release |

## FeatherWall

| State | Processes | Private MB | Working set MB | GPU dedicated MB | GPU shared MB | CPU (machine) | CPU (one core) | Busiest GPU engine | Verified |
|---|---|---|---|---|---|---|---|---|---|
| **Still image** | **1** | **110.5** | **108.5** | 48.6 † | 9.7 † | **0.008 %** | **0.2 %** | **0 %** | playing, full window |
| Video 1080p60 playing | | | | | | | | | |
| **Video 4K60 playing** | **1** | **199.4** | **188.6** | **220.6** | **58.5** | **0.318 %** | **7.6 %** | **5.8 % (videodecode)** | playing, full window |
| **Video 4K60, auto-paused** | **1** | **219.5** | **204.2** | **231.6** | **68.1** | **0.034 %** | **0.8 %** | **0 % (copy)** | paused, full window |
| Settings panel open | | | | | | | | | |

*Playing rows measured 2026-08-22 on build 26200, 24 cores, Intel Arc, single 2560×1600 display at
150 %. Source `scripts/bench/results/featherwall-playing-verified.json`. Paused row measured
2026-08-20, 32.4 s window, source `scripts/bench/results/featherwall-paused-verified.json`.*

**† The still-image GPU figures are partial.** That run read the GPU engine counters on 6 of 8
samples and the GPU memory counters on 5 of 8, so those two cells are an average of what could be
read rather than of the whole window. Its CPU and memory figures are complete. The row records this
itself in `GpuCounters`; it is marked here rather than quietly published as if clean.

**The 4K60 playing row is the headline number and it is now measured:** 0.318 % of a 24-core
machine, 7.6 % of one core, one process, with the work sitting on the video-decode engine at 5.8 %
where it belongs. A still image costs 0.008 % — the "zero ongoing cost after first paint" claim,
measured.

**The two remaining rows cannot be produced by a script.** Both need a window over the desktop, and
`PauseDecision` reads `GetForegroundWindow` — Windows only grants foreground activation to a process
that already owns the foreground, so a benchmark driven from a background task, a scheduled job or an
automation harness opens its blocker window *behind* the desktop and pauses nothing.
`Run-Bench.ps1` now detects that and refuses those rows by name instead of producing them wrongly.
Run it from an interactive console you are sitting in front of and they fill in.

The 1080p60 row is simply not measured yet; it needs a 1080p clip.

## Against the competition

Nothing here has been measured. These rows exist so it is obvious what is missing.

Two independent Reddit threads asked for exactly this table, and **process count and RAM** were
what both asked about — which is the one axis where a single-process design should separate
itself from a launcher plus a browser subprocess plus a player.

| App | Processes | Private MB | Working set MB | GPU dedicated MB | CPU (machine) | Verified |
|---|---|---|---|---|---|---|
| FeatherWall | | | | | | |
| Lively Wallpaper | | | | | | |
| Wallpaper Engine | | | | | | |
| DesktopHut | | | | | | |

To fill a row, install the app, set **the same media file** FeatherWall was measured with, and run:

```powershell
.\scripts\bench\Measure-App.ps1 -ProcessName <their process> -Seconds 30 -Label "video 4K60 playing"
```

The script walks the whole process tree from that name, so a tool that spawns a player or a
browser subprocess is measured including them. That is the entire point of the comparison and it
is why quoting a vendor's own published figure would not do.

**Same file, same machine, same script, or it is not a comparison.** Wallpaper Engine's worst
numbers come from 4K and from codecs FeatherWall would inherit the cost of too; picking media that
suits one side is how these tables usually get written.

## Reading the numbers

**The two CPU columns are the same measurement.** One divides by 24 cores, the other by one.
Quoting only the first is how a busy process is made to look free, so both are always shown.

**GPU percentages do not add up.** Windows reports GPU work per engine — 3D, video decode, video
processing, copy — and those are separate engines running concurrently. The table shows the
busiest single one, which is roughly what Task Manager displays. Summing them produces a number
that means nothing.

**Dedicated and shared GPU memory are both real, and neither is in the working set.** The honest
total for a wallpaper is working set plus both.

> **Correction, 2026-08-20.** The README previously said dedicated VRAM "reads 0 MB on every row
> because integrated graphics have no memory of their own". That is wrong. This machine's Intel
> Arc integrated GPU reports 2048 MB of adapter RAM, and Windows attributes **231.6 MB of
> dedicated usage** to FeatherWall on it. The old explanation made the footprint look smaller
> than it is. The harness found this on its first real run.

**Paused does not give the memory back.** Buffers stay allocated so resuming is instant, which is
why the paused row is not much cheaper in memory than a playing one. It is dramatically cheaper in
CPU and GPU, which is the state the wallpaper is in whenever you are actually gaming.
