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
| **Still image + widgets** | **1** | **93.0** | **127.8** | **31.5** | **2.2** | **0.135 %** | **3.3 %** | **0.1 % (copy)** | playing, full window |
| **Video 4K60 + widgets** | **1** | **208.5** | **209.9** | **220.6** | **63.5** | **0.988 %** | **23.7 %** | **6.6 % (videodecode)** | playing, full window |
| **Video 4K60 paused + widgets** | **1** | **210.0** | **212.6** | **220.6** | **63.5** | **0.052 %** | **1.2 %** | **0 % (3d)** | paused, full window |

*Playing rows measured 2026-08-22 on build 26200, 24 cores, Intel Arc, single 2560×1600 display at
150 %. Source `scripts/bench/results/featherwall-playing-verified.json`. Paused row measured
2026-08-20, 32.4 s window, source `scripts/bench/results/featherwall-paused-verified.json`.*

**† The still-image GPU figures are partial.** That run read the GPU engine counters on 6 of 8
samples and the GPU memory counters on 5 of 8, so those two cells are an average of what could be
read rather than of the whole window. Its CPU and memory figures are complete. The row records this
itself in `GpuCounters`; it is marked here rather than quietly published as if clean.

**The 4K60 playing row is the headline number and it is now measured:** one process, with the work
sitting on the video-decode engine at 5.8 % where it belongs. A still image costs well under a
tenth of a percent — the "zero ongoing cost after first paint" claim, measured.

**CPU moves between runs, so treat the table cell as one sample.** The row above is
`featherwall-playing-verified.json` at 0.318 % of the machine. A second full run
(`featherwall-playing-verified-run2.json`) measured **0.466 %**, and a reading taken straight from
`TotalProcessorTime` outside the harness gave **0.236 %** — a spread of roughly 2× on the same
machine with the same clip. Decode cost tracks scene content and background load. The GPU engine
figure barely moved across those runs (5.8 % and 5.9 %); the CPU figure is the volatile one, and
quoting any single run of it to three decimals would be false precision.

**Only the settings-panel row is still blank, and it is blank for a specific reason.** Rows that need
a window over the desktop cannot be produced from a *background* session: `PauseDecision` reads
`GetForegroundWindow`, and Windows only grants foreground activation to a process that already owns
the foreground, so a benchmark driven from a scheduled task, a CI agent or an automation harness
opens its blocker window *behind* the desktop and pauses nothing. `Run-Bench.ps1` now detects that
and refuses the row by name instead of producing it wrongly.

The auto-paused row above was produced exactly that way — from an interactive session, with a real
window covering the desktop — so an interactive run is known to be able to produce *a* paused row.

**The settings-panel row is unverified.** It has never succeeded in any session. Every attempt so
far has run from a background session, where the blocker cannot take the foreground, so the row was
refused rather than measured. An interactive run is *expected* to produce it, by the same mechanism
that produced the auto-paused row — but expected is not verified, no run has yet produced it, and
this file should not read as though one has.

## What the widgets cost — measured 2026-08-29

Two runs, back to back in one session, on the same machine and the same media. Comparing across
sessions would be meaningless: this machine's CPU figure moves two- to threefold run to run.

| Still image | Without widgets | With widgets | Change |
|---|---|---|---|
| CPU, whole machine | 0.026 % | **0.135 %** | **x5.2** |
| CPU, one core | 0.6 % | **3.3 %** | **x5.5** |
| Working set | 110.7 MB | 127.8 MB | +17 MB |

| 4K60 playing | Without widgets | With widgets | Change |
|---|---|---|---|
| CPU, whole machine | 0.721 % | **0.988 %** | x1.37 |
| CPU, one core | 17.3 % | **23.7 %** | x1.37 |
| Busiest GPU engine | 6.4 % decode | 6.6 % decode | unchanged |

**The still-image row moved, and a lot in relative terms.** Five times is far outside the two- to
threefold spread this machine shows between runs, so it is a real signal rather than noise.

That is the record's frame clock, and it is the trade the design accepted knowingly. The still
image is the state where FeatherWall previously did *nothing at all* after first paint, so any
number above zero is an infinite proportional increase and a small absolute one: **0.135 % of a
24-core machine is about a thirtieth of one core.** Set `disc.rotate` to false and no timer is
created — the record, its artwork and the progress ring stay as a still image.

On a video wallpaper the same widgets cost proportionally far less, because the decoder already
dominates, and the GPU figure does not move at all.

**Sources:** `scripts/bench/results/featherwall-baseline-verified.json` and
`featherwall-widgets-verified.json`. The record was confirmed turning during the sampling window
from FeatherWall's own log, so this measures the timer running rather than a static widget.

**"No timer anywhere" is no longer true of FeatherWall as a whole, and this file used to say it
was.** The record turns at 15 fps while music plays and the desktop is visible, and stops
completely otherwise. Two attempts were lost to a full-screen browser that ignores `MinimizeAll` and
takes the foreground back between samples, so FeatherWall correctly paused and the harness
correctly refused every playing row. The harness now names that window and stops before running
rather than discovering it four rows later.

To reproduce, from a session with nothing in full screen:

```powershell
.\scripts\bench\Run-Bench.ps1 -Video <the 4K clip> -Image .\docs\media\hero.jpg -Seconds 30
.\scripts\bench\Run-Bench.ps1 -Video <the 4K clip> -Image .\docs\media\hero.jpg -Seconds 30 -Widgets
```

Compare the two still-image rows, and only ever from a single session. **CPU spans roughly two- to
threefold run to run on this machine**, so a difference inside that spread is not evidence of
anything.

What *is* verified is which parts have a timer and which do not. Every repaint is logged. Over an
hour of running, the battery and info widgets repainted only at startup, when a media session
changed, and when the battery percentage moved — at irregular intervals of 20 to 41 seconds while
charging, never on a period.

The record is the exception, and it logs its own frame clock starting and stopping (`record
turning` / `record still`), so the gating can be read off the log rather than taken on trust. What
has **not** been measured is what those frames cost.

The 1080p60 row has simply never been run through the harness. The 1920x1080 downscale the
hand-measured table used is not on this machine, so filling it means regenerating that clip from
the same source and running the harness once — not a limitation, just work nobody has done.

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
