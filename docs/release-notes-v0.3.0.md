# FeatherWall v0.3.0

Five widget styles, chosen from the tray or a new Style page, and an app you can find again in the
Start menu.

## Widget styles

The clock, the music record and the battery now read as one set, in one of five styles:

- **Classic** — the look FeatherWall has always had. **Upgrading keeps it**: a v0.2.0 config loads
  unchanged and the desktop looks exactly as before until you pick something else.
- **Editorial** — one type family throughout: the date in small accent capitals above a light
  clock, a short accent rule beneath, a real title hierarchy on the record, and the battery as a
  quiet ring.
- **Glass dock** — everything in one capsule on the top or bottom edge: the turning record, the
  track with a thin progress bar, the time and day, the battery. Missing parts collapse.
- **Orbit** — everything becomes a ring: a dial whose accent arc fills as the hour passes, with the
  record and the battery as smaller rings beneath it.
- **Poster** — a large stacked clock with outlined hours and accent minutes, the date running down
  beside it; the record shrinks to a token beside one line, the battery to a slim bar.

**Choosing one.** Right-click the tray icon → **Widget style**, or open Settings → **Style**: a live
preview of each style, drawn with your own settings, applies on click. The Style page also holds:

- **Accent** — taken once from your wallpaper (`auto`), or a colour you pick.
- **Size** — 50–300 % for every style but Classic, which keeps its own sizes.
- **Position** and **Monitor** — Glass dock, Orbit and Poster place the whole set at one spot.

Settings a style ignores are labelled on their pages ("Classic only", "set on Style page") rather
than hidden, so nothing you set is lost.

**One-time notice.** Upgraders on Classic get a single tray notification that the styles exist;
clicking it opens the Style page. It never shows again, and a fresh install — which starts on
Orbit with the music and battery widget on — never sees it.

## Legibility

Every new style draws one soft shadow under its widgets, so thin text stays readable over a bright
sky. It is baked into the widget's own picture when it repaints; the wallpaper behind is never
blurred or read back.

Track titles and artists in any script now draw correctly: Hindi, Thai, Chinese, Japanese and
Korean use a Windows font that has the letters, and channel names written in Unicode's decorative
"bold" letters (𝐋𝐎𝐅𝐈 𝐎𝐍 𝐅𝐈𝐑𝐄) show as plain text instead of empty boxes.

## The record still turns on its timer, and here is why

This release tried turning the record on the compositor instead of redrawing it 15 times a second.
Both ways were measured against a still record, in the same session, with DWM's own cost counted
separately. The compositor left FeatherWall idle, but Windows then recomposed the turning record 240
times a second. Over the still record it added 0.60 % CPU and 10.8 % GPU, against 0.21 % and 1.3 %
for the timer, so the timer stays. The method and every run are in `docs/benchmark.md`.

What did change: in Glass dock, Orbit and Poster, only the small record itself is redrawn while it
turns, not the whole widget.

## Start menu

FeatherWall adds itself to the Start menu on first start, so it can be found again after you exit
it. Delete the entry and it stays deleted; set `startMenuShortcutAdded` to `false` in config.json to
get it back.

## Upgrading

Replace the folder (or `winget upgrade RitvikDayal.FeatherWall`). Your config.json carries over; a
new `widgets` section is added the first time something is saved.
