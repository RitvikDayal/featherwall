# FeatherWall v0.2.0

Second release. The headline is **widgets that read the system** — a battery halo and a now-playing
line — but the larger change is that the performance claims are now produced by a script anyone can
run instead of by someone reading Task Manager.

## Widgets

**Info widget.** A second, independently anchored stack of lines fed by the operating system: what
is playing, and the battery. **Off by default** — one checkbox on the Info page turns it on.

There is no timer behind it. Windows pushes a notification when the battery percentage moves and
raises a WinRT event when the media session changes; those events are the only thing that repaints
it. Between them it costs nothing. Now playing reads the same session Windows itself uses, so it
covers anything with media controls, a browser tab included. Nothing playing means no line rather
than an empty one, and a machine with no battery never shows a battery line.

**Battery halo.** A ring beside the battery line whose arc is the charge level and whose colour
steps with it — red low, orange middling, gold high, pale gold when charged, with a bolt in the
centre while charging and a tick when full. Three palette presets, five colour pickers, adjustable
thresholds and size. It can sit left, right, above or below the text, or detach entirely and take
its own anchor anywhere on screen.

It does not animate, deliberately. The arc already is the level, and a frame clock would be the
first thing to make idle cost non-zero.

## Rendering and reliability

- **Per-monitor DPI.** Widgets are sized against the primary display's scaling, so they keep the
  same physical size on a 100 % external monitor as on a 150 % laptop panel.
- **Device-loss recovery.** A GPU driver update or TDR rebuilds the whole layer instead of leaving
  a dead surface, with a bounded guard against a rebuild loop.
- **Pushed power and display signals** replace polled idle detection. The wallpaper stops rendering
  into a screen that is off because Windows says so, not because a timer noticed.
- **Independent date styling** — its own font, size, colour and opacity, or inherit each from the
  time. Per-edge margins for both.
- **CodeQL** now runs on the C# code on every push.

## Measurement

`scripts/bench/Run-Bench.ps1` drives FeatherWall through each wallpaper state and measures it.

The point of it is that it **refuses to report a number it cannot stand behind**: it reads
FeatherWall's own log and discards any row where the pause state was not the expected one for the
whole sampling window. That check is not theoretical — the first run of the harness reported a
"video playing" figure while the log showed the wallpaper pausing and resuming three times inside
the window.

Building it corrected the published table. The previous hand-measured numbers claimed dedicated
VRAM read 0 MB "because integrated graphics have no memory of their own". That was wrong: Windows
attributes 231.6 MB of dedicated usage to FeatherWall on this machine's integrated GPU, and the old
explanation made the footprint look smaller than it is.

### What is still not measured

**The still-image row with the widget enabled has never been run.** The widgets were designed to a
stated cost budget — no timer, one small bitmap per change — and the way to check that is to run
the still-image row with and without them and compare. Attempts were lost to a full-screen browser
holding the desktop, which correctly paused the wallpaper and correctly caused every playing row to
be refused.

`docs/benchmark.md` carries that row as a blank cell with the two commands to fill it. An empty
cell there means nobody has measured it. It does not mean zero.

Also still blank: the 1080p60 row, the settings-panel row, and every competitor comparison row.

## Upgrading

A v0.1.0 config loads unchanged. The info widget is off, so the desktop renders exactly as it did
before until you switch it on.

## Known limitations

- Multi-monitor widget placement is **unverified** — issues #3 and #4 need hardware this project
  has not been able to test on. Help wanted.
- No row of `docs/recovery-matrix.md` has been executed.
- The anchor grid on the Battery page stays visually undimmed when it does not apply. It is inert,
  but it does not look inert.
