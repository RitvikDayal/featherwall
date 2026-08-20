# Benchmark scripts

Two PowerShell scripts. `Measure-App.ps1` measures one running application; `Run-Bench.ps1`
drives FeatherWall through its states and calls the first one for each.

Published results live in [`../../docs/benchmark.md`](../../docs/benchmark.md).

## Running it

```powershell
.\Run-Bench.ps1 -Video "D:\clips\4k60.mp4" -Image "..\..\docs\media\wall-hubble.jpg"
```

It backs up `config.json`, minimises every window, measures four states, then restores your
config and restarts FeatherWall. **Do not use the machine while it runs**, and close anything that
covers the desktop — including games.

To measure a competitor, install it, point it at the same media file, and:

```powershell
.\Measure-App.ps1 -ProcessName Lively -Seconds 30 -Label "video 4K60 playing"
```

## Why a row can be refused

`-ExpectState Playing|Paused` makes the script read FeatherWall's log and throw unless the pause
state was the expected one for the whole window — no transition inside it, and the right state
going in.

Without that check the harness is worse than useless, because its failure mode is flattering:
FeatherWall stops decoding when a window covers the desktop, so a benchmark run on a machine
someone is using records a paused wallpaper as a cheap playing one and nothing in the output shows
it. The first run of this harness did exactly that — *"video playing, 3.3% video decode"* while
the log showed three pause/resume transitions inside the sampling window.

Both halves of the check are load-bearing. Checking only for transitions passes a wallpaper that
was paused for the entire window, which is the more common failure.

## What is measured, and why that way

**The whole process tree**, walked from every process matching the name. A tool that runs a
launcher plus a browser subprocess plus a player is spending that memory whether or not the
process you named is the big one. This is the axis two separate Reddit threads asked about.

**CPU from `TotalProcessorTime` deltas over wall-clock**, not a sampled counter. Exact, survives a
tree that changes shape mid-run, and it forces the denominator to be explicit. Both denominators
are reported — the same measurement over 24 cores and over one.

**GPU per engine**, from `\GPU Engine(*)` filtered to our pids. Windows reports 3D, video decode,
video processing and copy separately and they run concurrently, so the busiest single engine is
reported with its name. Summing them yields a meaningless number.

**Dedicated and shared GPU memory separately.** Both are real and neither is inside the working
set. A modern integrated GPU does have dedicated memory: measured here, Intel Arc reports 2048 MB
of adapter RAM and 231.6 MB of dedicated usage attributed to FeatherWall.

## Known limits

- **Non-English Windows.** Performance counter paths are localised, so `\GPU Engine(*)` and
  `\GPU Process Memory(*)` will not resolve. The script degrades to zero GPU figures rather than
  failing, which means a GPU column of 0 on a localised machine is *missing*, not measured.
- **The pause-state check is FeatherWall-only.** It reads FeatherWall's log, so a competitor row
  has no equivalent protection. Measure competitors on the same idle machine in the same sitting.
- **Cold-start-to-first-frame is not implemented.** It needs a timestamp from process start to the
  first DirectComposition commit that presents content, which means instrumenting the app rather
  than observing it from outside.
