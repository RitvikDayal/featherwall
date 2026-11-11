# FeatherWall v0.3.1

Two new widget styles, Monolith and Tiles, and a round of fixes to Settings and to how the widgets
redraw.

## New styles

- **Monolith**: the weekday is the widget, in thin capitals spaced wide, with the date under it and
  the time in your accent. Music and battery sit on one quiet line at the other end of the screen.
  Starts top left.
- **Tiles**: rounded dark cards, an analog clock with a scalloped face, the date, the turning record
  with a progress bar, and a battery ring. Starts bottom right.

Pick them from the tray (right-click the feather, then **Widget style**) or from **Settings →
Style**. Both follow your accent, size, position and monitor settings.

## Fixes

- **Opening FeatherWall while it runs**, from the Start menu or with `featherwall.exe --settings`,
  brings the running copy's Settings to the front instead of an "already running" box.
- **Settings stays in step with the tray.** A style or accent picked from the tray updates an open
  Style page.
- **Text size on mixed-DPI setups.** On a 100% monitor beside a 150% one, the Settings tabs, the
  style captions and the position grid no longer come out 1.5 times too big.
- **Large sizes stay on screen.** At large sizes the grouped styles give up their margin before they
  cross the screen edge, and Monolith shrinks long weekday names such as QUARTA-FEIRA to fit.
- **Less work each minute.** In the grouped styles, only the panels whose pixels changed get a new
  shadow and go back to the screen.
- **Memory.** Settings frees its wallpaper backdrop and rebuilt controls when they are replaced or
  closed, and Poster no longer leaks a drawing object on every repaint.

## Upgrading

Replace the folder (or `winget upgrade RitvikDayal.FeatherWall`). Nothing changes until you pick a
new style. Going back to v0.3.0 with `monolith` or `tiles` in config.json shows Classic.
