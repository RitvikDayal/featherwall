# FeatherWall v0.2.0

Widgets that read the system, and performance numbers you can reproduce with a command instead of
taking on trust.

## Widgets

**Info widget.** A second, independently anchored block fed by the operating system. **Off by
default** — one checkbox on the Info page turns it on. A v0.1.0 config loads unchanged and the
desktop renders exactly as it did until you switch it on.

**Battery halo.** A ring with the charge inside it: the arc is the level, the colour steps with it
— red low, orange middling, green when healthy — and a bolt sits beside the number while charging,
replaced by a tick when full. Three palette presets, five colour pickers, adjustable thresholds and
size. It sits beside the text, or detaches entirely and takes its own anchor anywhere on screen.

**Now-playing record.** What you are listening to, drawn as a vinyl record with the **real album
artwork** on its label — read from the same Windows media session everything else here uses, so
there is still no network call. It turns at 33⅓ rpm while something plays, with the track's
progress on the rim. A track with no artwork gets a flat disc in the accent colour rather than a
broken box; pausing dims it rather than emptying it. It reads any app with media controls, a
browser tab included.

**Clock.** Independent date styling — its own font, size as a percentage of the time, colour and
opacity, or inherit each from the time. Per-edge margins for both.

## FeatherWall now has a timer, and it did not before

The record turns, and that is the app's first frame clock. v0.1.0 and every widget before this one
repainted **only** when Windows pushed an event.

It is deliberately hard to leave running. It exists only while something is actually playing **and**
the desktop is uncovered **and** the display is on. Any of those going false stops it dead — not
throttled, stopped. Turning off *Spin while playing* creates no timer at all and leaves the record,
the artwork and the progress ring as a still image.

Everything else still repaints on events only: a battery percentage moving, a track changing.

## Rendering and reliability

- **Per-monitor DPI.** Widgets are sized against the primary display's scaling, so they keep the
  same physical size on a 100 % external monitor as on a 150 % laptop panel.
- **Device-loss recovery.** A driver update or TDR rebuilds the whole layer instead of leaving a
  dead surface, with a bounded guard against a rebuild loop.
- **Pushed power and display signals** replace polled idle detection. The wallpaper stops rendering
  into a screen that is off because Windows says so, not because a timer noticed.
- **CodeQL** runs on the C# code on every push.

## Measurement, and two corrections

`scripts/bench/Run-Bench.ps1` drives FeatherWall through each wallpaper state and measures it. It
**refuses to report a row** whose pause state changed inside the sampling window — measuring a
silently paused wallpaper is the easiest way to publish a flattering number. That check is not
theoretical: the harness's own first run reported a "video playing" figure while the log showed the
wallpaper pausing and resuming three times inside the window.

Building it corrected two published claims, and both corrections go against us:

- The README and the website said dedicated VRAM reads 0 MB "because integrated graphics have no
  memory of their own". **Wrong.** This machine's integrated Arc reports 2048 MB of adapter RAM and
  Windows attributes **232 MB of dedicated usage** to FeatherWall on it. The old explanation made
  the footprint look smaller than it is.
- The paused-state CPU figure was published as **0.004 %**. It measures **0.034 %**.

The website carried both of those until this release and now carries the corrections.

### What the widgets cost, measured

Two runs back to back in one session, because this machine's CPU figure moves two- to threefold
between runs and comparing across sessions would mean nothing.

| Still image | Without widgets | With widgets |
|---|---|---|
| CPU, whole machine | 0.026 % | **0.135 %** |
| CPU, one core | 0.6 % | **3.3 %** |
| Working set | 110.7 MB | 127.8 MB |

**It moved about fivefold, and that is the record's frame clock.** Five times is well outside the
run-to-run spread, so it is a real signal. In absolute terms it is small — 0.135 % of a 24-core
machine is roughly a thirtieth of one core — but the still image is the state where FeatherWall
used to do *nothing at all* after first paint, so it is worth saying out loud rather than burying
in a ratio. `disc.rotate: false` creates no timer and gives the number back.

On a 4K60 video wallpaper the same widgets cost proportionally far less (0.721 % → 0.988 %) because
the decoder already dominates, and the GPU figure does not move.

The record was confirmed turning during the sampling window from FeatherWall's own log, so this
measures the timer running rather than a static widget.

### What is still not measured

The 1080p60 row, the settings-panel row, and every competitor comparison row. The website's 1080p60
tab shows dashes for the same reason. An empty cell means nobody has run it — not zero.

## Upgrading

A v0.1.0 config loads unchanged. Everything new is off or absent until you turn it on, with one
documented exception: if you enable the info widget, the battery halo comes with it rather than
needing a second switch.

## Known limitations

- Multi-monitor widget placement is **unverified** — issues #3 and #4 need hardware this project
  has not been able to test on. Help wanted.
- No row of `docs/recovery-matrix.md` has been executed.
- On the Battery settings page, the anchor grid stays visually undimmed when it does not apply. It
  is inert, but it does not look inert.
- Releases are **unsigned**, so SmartScreen will warn. Verify the SHA-256 against the published
  `.sha256`, or build from source.
