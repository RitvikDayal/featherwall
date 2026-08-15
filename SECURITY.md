# Security policy

## Reporting a vulnerability

Please do not open a public issue for a security problem.

Use GitHub's [private vulnerability reporting](https://github.com/RitvikDayal/featherwall/security/advisories/new)
on this repository, or email ritvikr98@gmail.com with `FeatherWall security` in the subject.

This is a small project maintained by one person in their spare time, so treat these as best
effort rather than a guarantee: expect a first reply within about a week. If you have not heard
back in two weeks, feel free to chase it.

## Supported versions

Only the latest release and the current `main` branch receive fixes. There are no long-term
support branches.

## What is in scope

FeatherWall runs as a normal desktop application under your own account, and it deliberately
never runs elevated. The interesting attack surface is small but real:

- **Gallery downloads.** FeatherWall fetches media over HTTPS from URLs listed in
  `src/FeatherWall/Gallery/gallery.json`. Eight of the eleven entries are pinned to a SHA-1 that
  is verified after download. Three NASA-hosted videos are currently unpinned, which is tracked
  as a known gap. Anything that lets a manifest entry write outside the cache directory, or that
  bypasses checksum verification where a checksum exists, is in scope.
- **Config parsing.** `%APPDATA%\FeatherWall\config.json` is read at startup. Crashes are bugs;
  anything that turns a config file into code execution is a vulnerability.
- **Media decoding.** Decoding runs through the operating system's Media Foundation pipeline.
  Bugs in the codecs themselves belong to Microsoft, but if FeatherWall hands the pipeline
  something it should not, that is ours.
- **Window handling.** FeatherWall reparents a window into the shell's wallpaper layer. Anything
  that lets another process abuse that to draw over or read from the desktop is in scope.

## What is out of scope

- SmartScreen warnings on unsigned builds. Releases are unsigned today and the README says so.
  Code signing is on the roadmap.
- Requiring the user to already have local code execution or administrator rights.
- Denial of service that amounts to "a corrupt video file makes the wallpaper stop."

## Disclosure

Report privately, give it a reasonable window to be fixed, and credit is yours in the release
notes unless you would rather stay anonymous.
