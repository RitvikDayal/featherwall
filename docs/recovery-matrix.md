# Recovery and desktop-integrity matrix

FeatherWall lives inside a window tree it does not own. Explorer restarts, the shell destroys
and recreates its `WorkerW`, the GPU driver updates, monitors come and go. The README claims
FeatherWall re-attaches itself when that happens. This file is where that claim is either
earned or shown to be untrue.

**An unrun row is not a passing row.** Every result column below starts empty and stays empty
until a human runs the step on real hardware and writes what happened. Do not fill a row from
reasoning about the code.

## How to record a result

Put your Windows build, your display setup and the date in the notes. `featherwall --diag`
prints the topology, the monitor list and each monitor's DPI, which is most of what a result
needs. Logs are at `%LOCALAPPDATA%\FeatherWall\featherwall.log`.

### Do not verify with a GDI screen capture

Measured on build 26200, 2026-08-20. A GDI capture — `BitBlt`, `Graphics.CopyFromScreen`, most
scripted PowerShell screenshots — **cannot see the wallpaper FeatherWall is drawing.** It reads
the redirection surface, and bypassing redirection is the entire point of the DirectComposition
path described in [`architecture.md`](architecture.md). What such a capture returns instead is
the OS static wallpaper, which FeatherWall itself sets to a captured frame for the
virtual-desktop-switch fallback. That fallback can be a stale frame from a previously-configured
wallpaper, so the capture looks plausible and is wrong, which is the dangerous kind of wrong.

Use Print Screen, the Snipping Tool, Game Bar, or anything else built on the DWM capture path
(`Windows.Graphics.Capture`). Or photograph the screen. A green result recorded from a GDI
capture is not evidence.

## Recovery legs

| # | Scenario | How to reproduce | Expected | Result | Who / when |
|---|---|---|---|---|---|
| R1 | explorer.exe restart | `taskkill /f /im explorer.exe` (it relaunches itself), or restart it from Task Manager | Wallpaper re-attaches within a few seconds without user action; desktop icons return and stay clickable | | |
| R2 | Sleep and resume | `rundll32.exe powrprof.dll,SetSuspendState 0,1,0`, then wake | Wallpaper still rendering; video still playing or correctly paused; no black rectangle | | |
| R3 | Lock and unlock | `Win+L`, then sign back in | Playback paused while locked, resumes on unlock; layer handles revalidated | | |
| R4 | Monitor power-cycle | Switch the display off at the monitor, wait 10 s, switch it on | Wallpaper returns; no stale swapchain; no duplicate visuals | | |
| R5 | Display topology change | Plug or unplug an external display; `Win+P` between Extend and Duplicate | Per-monitor windows rebuilt at the new geometry; no leaked swapchain; clock lands on its configured monitor | | |
| R6 | RDP in and out | Connect over Remote Desktop, then disconnect and log back in at the console | Paused during the remote session; recovers at the console without a black desktop | | |
| R7 | GPU device removal (TDR) | Force a driver reset, or update or roll back the display driver while running | `DeviceLost` fires, the composition tree is rebuilt automatically, and the log shows the recovery. **This is the leg that regressed silently before 2026-08-20** — the event existed and nothing subscribed to it. Recovery is bounded to 3 consecutive attempts; a device lost *again* during the rebuild is carried forward rather than dropped, so an adapter that fails repeatedly gives up with a log line instead of looping | | |
| R8 | Classic WorkerW topology | Run on Windows 10 1809 or Windows 11 ≤ 23H2, where `--diag` reports `Topology : Classic` rather than `RaisedDesktop` | Wallpaper renders behind the icons on the classic path. **Never verified on hardware** — issue #4 | | |

## Desktop integrity

The wallpaper must not cost the user their desktop. Check these while a video wallpaper runs.

| # | Check | Expected | Result | Who / when |
|---|---|---|---|---|
| D1 | Icons clickable | Single-click selects, double-click opens | | |
| D2 | Icons renameable | F2 renames in place | | |
| D3 | Drag-select | A rubber-band selection draws over the wallpaper and selects icons | | |
| D4 | Icon refresh | A file created on the Desktop appears without a manual F5 | | |
| D5 | Hidden icons | Right-click → View → uncheck "Show desktop icons"; the wallpaper keeps rendering | | |
| D6 | Right-click menu | The desktop context menu opens over the wallpaper | | |
| D7 | Restore on exit | `featherwall --exit` puts the user's original wallpaper back | | |

## Power

| # | Check | How | Expected | Result | Who / when |
|---|---|---|---|---|---|
| P1 | Does not block sleep | Leave FeatherWall running and idle, then `powercfg /requests` | No `DISPLAY` or `SYSTEM` request held by `featherwall.exe`. Lively #1109 is the cautionary case: an audio wallpaper there blocks sleep | | |
| P2 | Reaches sleep on its timer | Idle the machine past its sleep timeout with a video wallpaper set | The machine sleeps normally | | |

## Known state as of 2026-08-20

Nothing in this file has been run. The development machine is a single 2560x1600 display at
150% on Windows 11 build 26200, raised-desktop topology, so R5, R6 and R8 cannot be produced
here at all, and R2, R3, R4 and R7 have not yet been done.

**R5 and R8 are the two open `help wanted` issues** — [#3](https://github.com/RitvikDayal/featherwall/issues/3)
and [#4](https://github.com/RitvikDayal/featherwall/issues/4). R1 is
[#5](https://github.com/RitvikDayal/featherwall/issues/5). If you have the hardware, filling in
one row of this table is the single most useful contribution available to this project.
