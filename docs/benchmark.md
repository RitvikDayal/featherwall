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

**"No timer anywhere" was never quite true, and this file used to say it was.** The clock widget
has re-armed a 1 Hz timer since v0.1.0. What that claim actually meant, and still means, is that the
*battery and now-playing sources* repaint only on pushed events. The record is the new exception and
the only continuous one: 15 fps while music plays and the desktop is visible, nothing otherwise. Two attempts were lost to a full-screen browser that ignores `MinimizeAll` and
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

## Record spin: timer vs compositor — measured 2026-11-10

v0.3.0 tried turning the now-playing record with a DirectComposition rotation animation instead of
the 15 fps GDI+ redraw. A compositor animation moves work out of FeatherWall and into `dwm.exe`, so
DWM has to be measured too. DWM also composes everything else on screen, so its numbers mean
something only next to a baseline taken under the same conditions.

**Method.**
- **Three arms, interleaved over three rounds in one session**, all on the same still image on
  `\\.\DISPLAY1` (2560 × 1600 at 240 Hz), Classic theme, info widget on:
  - **off:** the record shown but not turning (`disc.rotate = false`), the baseline;
  - **compositor:** the rotation animation;
  - **timer:** the 15 fps redraw.
- **Steady media.** A silent media session played throughout, so the record turned for every
  spinning window. The harness refused any row where it did not.
- **Each process reported separately:** CPU, and its own busiest GPU engine. All 9 runs were
  accepted.

Medians of the three rounds:

| Arm | FeatherWall CPU | FeatherWall GPU | DWM CPU | DWM GPU (3D) |
|---|---|---|---|---|
| off (baseline) | 0.015 % | 0 % | 0.242 % | 0 % |
| compositor | 0.017 % | 0 % | 0.840 % | **10.8 %** |
| timer | 0.106 % | 0.6 % (copy) | 0.363 % | 0.7 % |

Cost of turning the record, over the baseline:

| Arm | CPU (FeatherWall + DWM) | GPU (FeatherWall + DWM) |
|---|---|---|
| compositor | +0.60 % | +10.8 % |
| timer | +0.21 % | +1.3 % |

**Verdict: the timer is cheaper on both measures, so the record keeps it.**
- **Why the compositor costs more:** it leaves FeatherWall idle, but DWM then recomposes the turning
  record at the display's 240 Hz rather than 15 times a second.
- **Repeatability:** DWM's GPU cost under the compositor was 10.7–10.8 % in every round. Its CPU
  varied, at 1.08 %, 0.84 % and 0.12 %.
- **Not measured:** the second display (60 Hz) and video. Over video, DWM composes every frame
  anyway, so the difference there should shrink. A slower panel would cut the compositor's cost, but
  nothing measured here suggests it would beat the timer.

**An earlier attempt the same day is superseded.** It reported "combined busiest GPU" across
FeatherWall and DWM, and it ran the two arms half an hour apart with different media playing. That
made the compositor look cheaper on CPU, and its video rows took FeatherWall's video-decode engine
as the "busiest" while hiding DWM's 3D engine. Its files are kept as a record:
`scripts/bench/results/spin-gate-DISPLAY1-*.json`.

Result file for the numbers above: `scripts/bench/results/spin-ab-DISPLAY1.json`, one row per run,
with the full measurement attached.

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

## The README's full notes, as of v0.3.0

The README now carries a short summary. These are the notes it used to carry, moved here word for word.

These are measurements, not estimates. Each row is 25 one-second samples on a running instance,
with the pause state checked before and after the window so a silently paused wallpaper could
never be recorded as a cheap one. The GPU column that used to sit here carried no stated method
and has been fully re-measured; what follows replaces it.

> **Test rig:** Windows 11 Pro 26200 · Intel Core Ultra 9 275HX, 24 logical cores · 63 GB RAM ·
> Intel Arc integrated graphics driving the display, NVIDIA RTX 5090 Laptop idle · 2560x1600 at
> 240 Hz, 150 % scaling · .NET 10 Release build, single monitor.
>
> **Test media, as measured:** the still-image row used `docs/media/hero.jpg`, 1800x1125. The
> 4K60 row used a 3840x2160 H.264 clip at 60 fps and 12 Mbps. Both video rows below were meant to
> be the same clip at two resolutions; only the 4K one has been run through the harness.
>
> The earlier hand-measured table used a 6200x6200 Hubble Ultra Deep Field JPEG and a 1920x1080
> downscale of the video. Those are **not** the files behind the numbers above, which is one more
> reason the two tables should not be compared cell by cell.

| State | CPU (whole machine) | CPU (one core) | Memory (working set) | Graphics memory | Busiest GPU engine |
|---|---|---|---|---|---|
| Still image | 0.008–0.016 % | 0.2–0.4 % | 108–112 MB | 58–62 MB † | 0 % |
| Video, auto-paused | 0.034 % | 0.8 % | 204 MB | 300 MB | 0 % (copy) |
| Video, 1080p60 playing | — | — | — | — | — |
| Video, 4K60 playing | 0.32–0.47 % | 7.6–11.2 % | 189–216 MB | 279–308 MB | 5.8–5.9 % (video decode) |

Every **filled** row is produced by [`scripts/bench/Run-Bench.ps1`](../scripts/bench/), which reads
FeatherWall's own log and **refuses to report a row** whose pause state was not the expected one for
the whole sampling window. The 1080p60 row is an intentionally empty placeholder, not a measurement.
The raw records are in [`scripts/bench/results/`](../scripts/bench/results/); `docs/benchmark.md`
carries the same numbers with their provenance.

**† The still-image graphics figures are partial** — that run read the GPU counters on 5 of 8
samples, which the record states. Its CPU and memory figures are complete.

**The 1080p60 row is empty because nobody has measured it with the harness.** The 1080p downscale
described above was made for the hand-measured table and is not on this machine, so filling the row
means regenerating that clip and running the harness once. An empty cell here means exactly that,
and never "probably fine".

The first CPU column is the honest one for a 24-core machine. The second is the same measurement
divided by one core instead of twenty-four, because a sub-1 % figure on a machine this wide
deserves the honest denominator next to it.

**Ranges, not points, because CPU moves between runs.** Two full harness runs of the 4K60 row on
the same machine with the same clip measured **0.318 %** and **0.466 %**, and a third measurement
taken straight from `TotalProcessorTime` with no harness involved gave **0.236 %**. Decode cost
tracks scene content and whatever else the machine is doing, so a single run quoted to three
decimals would be false precision — which is the same mistake as the hand-read peak this table
replaced, just a smaller one. The GPU engine figure is stable across runs; CPU is not.

The rows with one measurement show a point value. The auto-paused row is a single run from
2026-08-20, so treat its CPU the same way until it has been repeated.

**Working set grows with uptime.** These rows are measured about twelve seconds after launch, which
is what a script can reproduce. A copy of FeatherWall left running for several hours on this machine
measured **233 MB**. Neither is wrong; they are different questions, and the reproducible one is
published.

**This runs on the integrated GPU, not the discrete one.** Every GPU number above belongs to the
**Intel Arc integrated graphics**. `nvidia-smi` lists no FeatherWall process on the RTX 5090, which
sat at 0 % utilisation and 287 MiB of its 24463 MiB throughout. That is not an accident: the
compositor lives on whichever adapter drives the display, so the wallpaper stays there and never
competes with a game on the discrete card. On a desktop whose only GPU is discrete, these numbers
would land on that card instead.

**Read the GPU column carefully.** Windows reports GPU work per engine, not as one number, and
those percentages do not add up to a total. The table shows the busiest single engine. This adapter
exposes **two** video-decode engines, and adding them together would be meaningless — reporting the
pair as one summed figure would be wrong in a way that looks plausible, which the harness used to do
and was corrected.

A separate 12-sample verification run, taken to check the table's GPU figure against the raw
`\GPU Engine(*)` counters, put the two decode engines at 5.31 % and 5.49 % with 3D at 1.24 %. Those
are a different run from the 5.8 % in the table above, which is why they do not match to the decimal:
each number is the busiest engine averaged across that run's own samples. The agreement that matters
is that both put the work on video decode at around 5–6 %, not on a video-processing engine at 42 %.

**Graphics memory is not the working set.** It is a separate allocation, so add the columns for
the honest total: a 4K60 wallpaper costs roughly **470 MB** of system memory, not 189 MB. An earlier
version of this table listed only the working set, which undersold the real footprint by about
half.

> **Correction, 2026-08-20.** This paragraph used to say dedicated VRAM "reads 0 MB on every row
> because integrated graphics have no memory of their own". That is wrong. The Intel Arc
> integrated GPU on this machine reports 2048 MB of adapter RAM, and Windows attributes **231.6 MB
> of dedicated usage** to FeatherWall on it. The scripted harness in
> [`scripts/bench/`](../scripts/bench/) found this on its first real run, which is roughly why it
> exists.
>
> **Re-run, 2026-08-22. The table above is now the scripted one, and the hand-measured figures it
> replaced were substantially wrong.** They read:
>
> | State | CPU (machine) | CPU (one core) | Working set | Graphics | Busiest engine |
> |---|---|---|---|---|---|
> | Still image | 0.06 % | 1.4 % | 156 MB | 44 MB | 0.1 % (3D) |
> | Video, auto-paused | 0.004 % | 0.1 % | 309 MB | 269 MB | 0 % |
> | Video, 1080p60 playing | 0.79 % | 19 % | 212 MB | 122 MB | 29.4 % (video processing) |
> | Video, 4K60 playing | 0.84 % | 20 % | 248 MB | 234 MB | 41.8 % (video processing) |
>
> The 4K60 CPU figure was **2.6× too high** and the GPU figure named an engine that is not busy at
> all. Checked against the raw `\GPU Engine(*)` counters, filtered to FeatherWall's pid, with the
> same clip on the same machine: the busiest engines are two video-decode engines at 5.31 % and
> 5.49 %, with 3D at 1.24 %. **No video-processing engine appears in FeatherWall's counters.** CPU
> was checked the same way, straight from `TotalProcessorTime` over a 30 s window with no harness
> involved: **0.236 %** of the machine, against 0.84 % published. The old numbers were read by hand
> off Task Manager, and a hand-read peak is not an average.
>
> The paused row's CPU moved the other way — 0.034 % rather than 0.004 % — so this is not a
> correction that only ever flatters the project.
>
> Working set is the one figure the old table got closest to: 248 MB against 189 MB measured twelve
> seconds after launch, because it was read from a long-running copy. That difference is uptime, not
> error, and is stated above the table now.

**Frame rate is a dial as well as resolution.** The 1080p and 24 fps comparisons that used to sit
here were built on the hand-measured numbers above, so they have been removed rather than rescaled
— the shape of the claim may well survive a re-measurement, but it has not had one, and a
plausible number is not a measured one. Re-run the harness with a 1080p clip and a 24 fps clip to
put it back on evidence.

**Pausing costs nothing to run, but it does not give the memory back.** Put a fullscreen app in
front, lock the session, connect over RDP or switch on battery saver, and playback stops rather
than throttling: 0.034 % CPU and no video engine activity at all. The buffers stay allocated so
resuming is instant, which makes paused the highest-memory row in the table. That is the honest
shape of it, and it is the state the wallpaper is in whenever you are actually gaming.

It also stays where it starts. Memory and threads were flat across 30 consecutive full re-applies,
which is the path a laptop hits every time it docks or changes monitors: 250 MB to 278 MB, 56
threads to 63. That used to go very differently, and the [honest notes](../README.md#honest-notes) in the README
explain what changed.

<sub>On battery specifically: system draw sat at 31 to 33 W across all three states on this laptop, which is within noise for isolating a single process. FeatherWall's share is not separable from that, so there is no watts figure here. The pause behaviour above is the honest version of that claim.</sub>
