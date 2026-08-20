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
| Still image | | | | | | | | | |
| Video 1080p60 playing | | | | | | | | | |
| Video 4K60 playing | | | | | | | | | |
| **Video 4K60, auto-paused** | **1** | **219.5** | **204.2** | **231.6** | **68.1** | **0.034 %** | **0.8 %** | **0 %** | paused, full window |
| Settings panel open | | | | | | | | | |

*Paused row measured 2026-08-20, 32.4 s window, source `scripts/bench/results/featherwall-paused-verified.json`.*

**The playing rows are empty because they could not be honestly measured.** A game was running on
the machine and holding the foreground, so FeatherWall was correctly paused the whole time and the
harness refused every playing row. Filling them in needs one command on an otherwise idle machine
with nothing covering the desktop.

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
