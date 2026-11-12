# FeatherWall v0.3.2

FeatherWall now comes with an installer, `FeatherWall-Setup.exe`, alongside the zip.

## The installer

- **No unzipping and no admin.** Setup.exe installs FeatherWall for your user, adds it to the Start menu and starts it.
- **.NET included when missing.** If the .NET 10 desktop runtime isn't installed, Setup.exe installs it first. That step is Microsoft's own runtime installer and may ask for administrator permission.
- **A real uninstall.** FeatherWall appears in **Settings → Apps**. Uninstalling it puts your own wallpaper back and removes its Start menu entry and its "Run at startup" entry. Your settings (`%APPDATA%\FeatherWall\config.json`) are kept, so a reinstall picks up where you left off.

The installer is unsigned, like the zip, so SmartScreen warns the first time: click **More info**, then **Run anyway**. The zip and winget are unchanged.

## Moving from the zip to the installer

1. Right-click the feather in the tray, then **Exit FeatherWall**.
2. Run `FeatherWall-Setup.exe`. The Start menu entry now opens the installed copy.
3. If you use **Run at startup**, untick it and tick it again in the installed copy, so Windows starts the new copy rather than the old folder.
4. Delete the old folder.
