# winget packaging

Manifests for submitting FeatherWall to the
[Windows Package Manager community repository](https://github.com/microsoft/winget-pkgs), so that
installing is `winget install RitvikDayal.FeatherWall` instead of a manual download.

## Files

Everything under `manifests/`, kept apart from this file on purpose: `winget validate` parses
every file in the directory it is given, so a stray Markdown file there fails the whole run with
a confusing YAML scanner error.

| File | Purpose |
|---|---|
| `manifests/RitvikDayal.FeatherWall.yaml` | Version manifest, points at the default locale |
| `manifests/RitvikDayal.FeatherWall.installer.yaml` | Installer type, URL, SHA-256, runtime dependency |
| `manifests/RitvikDayal.FeatherWall.locale.en-US.yaml` | Name, publisher, licence, description, tags |

The release is a zip containing a single `featherwall.exe`, so it is packaged as
`InstallerType: zip` with `NestedInstallerType: portable`. winget puts `featherwall` on the PATH
through its portable link directory, and uninstall removes it cleanly.

`Microsoft.DotNet.DesktopRuntime.10` is declared as a package dependency because the build is
framework-dependent, which is what keeps the download at 7 MB. winget will offer to install the
runtime if it is missing.

That identifier is confirmed present in the winget source (`Microsoft .NET Windows Desktop
Runtime 10.0`, 10.0.11). `winget validate` still prints a "dependencies that were not validated"
notice for it, which it emits for any dependency it does not resolve locally; that notice is not
an error and the manifest validates.

## Submitting

The community repository validates by downloading the installer, so **the GitHub release has to be
published first**. A draft release returns 404 to the validation pipeline and the pull request
will fail.

Once the release is public:

```powershell
winget validate --manifest packaging\winget\manifests
winget install --manifest packaging\winget\manifests   # optional local install test
```

Then submit with [wingetcreate](https://github.com/microsoft/winget-create), which forks
`microsoft/winget-pkgs` and opens the pull request:

```powershell
winget install Microsoft.WingetCreate
wingetcreate submit --token <github-pat> packaging\winget
```

Expect the automated validation to take a few minutes and a human review to take longer. A first
submission from a new publisher usually attracts a moderation check, which is normal.

## Updating for a new release

`wingetcreate update` recomputes the hash from the new URL rather than doing it by hand:

```powershell
wingetcreate update RitvikDayal.FeatherWall `
  --version <new-version> `
  --urls https://github.com/RitvikDayal/featherwall/releases/download/v<new-version>/featherwall-v<new-version>-win-x64.zip `
  --submit --token <github-pat>
```

Keep the copies in this directory in step with whatever gets merged upstream, so the repository
always shows what was actually submitted.
