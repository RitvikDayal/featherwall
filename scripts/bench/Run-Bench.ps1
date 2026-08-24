<#
.SYNOPSIS
    Drives FeatherWall through each wallpaper state and measures every one with Measure-App.ps1.

.DESCRIPTION
    Produces the table in docs/benchmark.md. Run it before a release and paste the output;
    a regression should be caught by a command, not by someone on Reddit.

    THE DESKTOP MUST BE VISIBLE for the playing rows. FeatherWall stops decoding when a window
    covers the desktop — that is the product working correctly, and it is also how a benchmark
    accidentally records a paused wallpaper as a cheap one. This script minimises all windows
    for the duration and restores them at the end. Do not use the machine while it runs.

    It backs up your config.json first and restores it at the end, including on Ctrl-C. If you had
    no config.json, it removes the one it wrote rather than leaving the benchmark's settings behind.

.PARAMETER Video
    A video file to measure. Use the same file at different resolutions to isolate resolution.

.PARAMETER Image
    A still image to measure.

.PARAMETER Seconds
    Sampling window per state.

.PARAMETER Widgets
    Also enable the info widget (now playing + battery). Run once without and once with to see
    what the widgets cost. Without this the benchmark config has no "info" block at all, which
    is the shipped default, so a plain run measures the widget as off rather than as absent.

.PARAMETER Exe
    featherwall.exe to measure. Defaults to the Release build.

.EXAMPLE
    .\Run-Bench.ps1 -Video D:\clips\1080p60.mp4 -Image .\docs\media\wall-hubble.jpg
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $Video,
    [Parameter(Mandatory)][string] $Image,
    [int] $Seconds = 30,
    [switch] $Widgets,
    [string] $Exe = "$PSScriptRoot\..\..\src\FeatherWall\bin\Release\net10.0-windows10.0.19041.0\featherwall.exe"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$measure = Join-Path $PSScriptRoot 'Measure-App.ps1'
$configPath = Join-Path $env:APPDATA 'FeatherWall\config.json'
$backup = "$configPath.bench-backup"
$shell = New-Object -ComObject Shell.Application
Add-Type -AssemblyName System.Windows.Forms

foreach ($p in @($Video, $Image, $Exe, $measure)) {
    if (-not (Test-Path $p)) { throw "Not found: $p" }
}

function Stop-FeatherWall {
    & $Exe --exit 2>$null | Out-Null
    $deadline = (Get-Date).AddSeconds(15)
    while ((Get-Date) -lt $deadline -and (Get-Process featherwall -EA SilentlyContinue)) { Start-Sleep -Milliseconds 500 }
    Get-Process featherwall -EA SilentlyContinue | Stop-Process -Force -EA SilentlyContinue
    Start-Sleep -Seconds 2
}

function Set-Wallpaper {
    param([string] $Path, [bool] $PauseOnFullscreen = $true)
    $config = @{
        fit         = 'Fill'
        volume      = 0.3
        muteVideo   = $true
        wallpapers  = @(@{ monitor = '*'; path = $Path })
        pause       = @{ onFullscreen = $PauseOnFullscreen; onBatterySaver = $true; onRemoteSession = $true }
        clock       = @{ enabled = $true; anchor = 'Center'; fontSize = 190; fontFamily = 'Segoe UI Light'
                         showSeconds = $false; showDate = $true; separator = $true; shadow = $true
                         color = '#F0FFFFFF'; marginX = 48; marginY = 48; twentyFourHour = $true; monitor = '*' }
    }
    if ($Widgets) {
        $config.info = @{ enabled = $true; monitor = '*'; anchor = 'BottomLeft'; marginX = 48; marginY = 48
                          fontSize = 34; fontFamily = 'Segoe UI'; color = '#F0FFFFFF'; shadow = $true
                          maxCharacters = 48; sources = @('nowPlaying', 'battery') }
    }
    $config | ConvertTo-Json -Depth 6 | Set-Content -Path $configPath -Encoding UTF8
}

Add-Type -Namespace Bench -Name Fg -MemberDefinition @'
[DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
[DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
[DllImport("user32.dll")] public static extern bool IsZoomed(IntPtr h);
[DllImport("user32.dll")] public static extern bool IsIconic(IntPtr h);
[DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
[DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowTextW(IntPtr h, System.Text.StringBuilder s, int n);
[DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
[StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
'@

function Find-CoveringWindow {
    <#
      Names the window still covering the desktop, or $null if the desktop is clear.

      Sampled repeatedly rather than once, because the window that breaks a run is usually one
      that keeps TAKING the foreground back rather than one that sits there. A full-screen video
      in a browser is the case that prompted this: it survives MinimizeAll, re-asserts itself a
      second later, and a single snapshot between two of its grabs reports a clear desktop.

      Without this the run proceeds, FeatherWall correctly pauses because something is in the
      way, and five minutes later every playing row is refused with no hint as to what did it.
    #>
    param([int] $Samples = 6, [int] $IntervalMs = 700)

    $screen = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
    for ($i = 0; $i -lt $Samples; $i++) {
        if ($i -gt 0) { Start-Sleep -Milliseconds $IntervalMs }
        $h = [Bench.Fg]::GetForegroundWindow()
        if ($h -eq [IntPtr]::Zero -or [Bench.Fg]::IsIconic($h)) { continue }

        $r = New-Object Bench.Fg+RECT
        if (-not [Bench.Fg]::GetWindowRect($h, [ref]$r)) { continue }
        if (($r.R - $r.L) -lt $screen.Width * 0.9 -or ($r.B - $r.T) -lt $screen.Height * 0.9) { continue }

        $title = New-Object System.Text.StringBuilder 256
        [void][Bench.Fg]::GetWindowTextW($h, $title, 256)
        [uint32]$procId = 0
        [void][Bench.Fg]::GetWindowThreadProcessId($h, [ref]$procId)
        $name = (Get-Process -Id $procId -EA SilentlyContinue).ProcessName
        return "$name — $($title.ToString())"
    }
    return $null
}

$script:blockerPids = @()

function Open-Blocker {
    <#
      Puts a maximised window over the desktop and returns once one is actually there.

      The window is one this script CREATES, rather than an app's borrowed from the system, because
      on Windows 11 you cannot reliably find an app's window from the process you started:

      * Notepad, which this used to launch, is packaged — the process you start hands off to a
        DIFFERENT one and its own MainWindowHandle stays 0 forever. Measured on 26200: the launched
        pid still reported 0 after 15 seconds while the real window belonged to another pid.
        ShowWindow(0, SW_MAXIMIZE) is a no-op, the desktop was never covered, and the "auto-paused"
        row was measured against a playing wallpaper. The harness refused it, which is the only
        reason this was noticed at all.
      * Taking "any Notepad window" instead is worse: with one already open it grabs the USER'S
        window, measures against it, foregrounds it, and leaves it in front — Close-Blocker only
        kills what this run started.
      * Filtering to our own pid then finds nothing, because a second Notepad launch opens a window
        in the process that already exists rather than a new one.
      * A console window is no better; conhost or Windows Terminal owns it, not us.

      A WinForms form owned by a process we spawned has none of those problems. Its handle is real,
      it is ours by construction, and cleanup is exact.
    #>
    $formScript = @'
Add-Type -AssemblyName System.Windows.Forms
$f = New-Object System.Windows.Forms.Form
$f.Text = "FeatherWall benchmark blocker"
$f.WindowState = "Maximized"
$f.TopMost = $true
[void]$f.ShowDialog()
'@
    $encoded = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($formScript))
    $proc = Start-Process powershell -PassThru `
        -ArgumentList '-NoProfile', '-STA', '-WindowStyle', 'Hidden', '-EncodedCommand', $encoded
    $script:blockerPids = @($proc.Id)

    $sig = @'
[DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int c);
[DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
[DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
'@
    $u = Add-Type -MemberDefinition $sig -Name BenchWin -Namespace W -PassThru

    $deadline = (Get-Date).AddSeconds(20)
    while ((Get-Date) -lt $deadline) {
        if ($proc.HasExited) { throw "The blocker process exited before showing a window." }
        $proc.Refresh()
        $window = if ($proc.MainWindowHandle -ne 0) { $proc } else { $null }
        if ($window) {
            [void]$u::ShowWindow($window.MainWindowHandle, 3) # SW_MAXIMIZE
            [void]$u::SetForegroundWindow($window.MainWindowHandle)
            Start-Sleep -Milliseconds 500

            # Maximised is not enough. PauseDecision reads GetForegroundWindow, and Windows
            # refuses foreground activation to a process that does not own the foreground — so
            # from a scheduled task, a CI agent or an automation harness the blocker window opens
            # maximised BEHIND the desktop and pauses nothing. Checked rather than assumed,
            # because the failure otherwise surfaces as Measure-App refusing the row with a
            # message about closing windows, which points the user at their own desktop.
            if ([IntPtr]$u::GetForegroundWindow() -ne [IntPtr]$window.MainWindowHandle) {
                throw @"
The blocker window opened but could not be brought to the foreground, so nothing is covering the
desktop and the paused rows cannot be produced from this session.

Windows only grants foreground activation to a process that already owns the foreground. Run this
script from an interactive console you are sitting in front of, not from a background task, a
remote session or an automation harness.
"@
            }
            return $window
        }
        Start-Sleep -Milliseconds 250
    }
    throw "The blocker window did not appear within 20s, so nothing is covering the desktop and the paused row cannot be produced honestly."
}

function Close-Blocker {
    foreach ($id in $script:blockerPids) { Stop-Process -Id $id -Force -EA SilentlyContinue }
    # Only the pids this script created. A Notepad the user already had open is not ours to kill.
    $script:blockerPids = @()
}

function Get-LastPauseEvent {
    <#
      The most recent Paused/Resumed line, or $null. Taken BEFORE a setup action so
      Wait-ForPauseState can insist on a NEWER one.
    #>
    $log = "$env:LOCALAPPDATA\FeatherWall\featherwall.log"
    (Get-Content $log -Tail 40 -EA SilentlyContinue |
        Select-String -Pattern '\[INF\] (Paused|Resumed) \\\\' |
        Select-Object -Last 1).Line
}

function Wait-ForPauseState {
    <#
      Blocks until FeatherWall's log reports the expected state IN AN EVENT THE SETUP ACTION
      CAUSED, and throws naming the real cause if it never does. The old code slept 8 seconds and
      hoped; when the blocker silently failed, what surfaced was Measure-App refusing the row with
      a message about closing windows — true, but pointing at the user's desktop rather than at the
      broken step.

      -Since is what makes it a check rather than a coincidence. Accepting any matching line means
      that when the wallpaper is ALREADY paused, this returns instantly whether or not the settings
      panel opened at all — and the row then measures an unrelated paused state with full
      confidence. A state the setup did not produce is not evidence the setup worked.
    #>
    param([ValidateSet('Paused', 'Playing')][string] $Expected, [string] $Because, [string] $Since)

    $deadline = (Get-Date).AddSeconds(25)
    while ((Get-Date) -lt $deadline) {
        $last = Get-LastPauseEvent
        if ($last -and $last -ne $Since) {
            $state = if ($last -match '\[INF\] Paused ') { 'Paused' } else { 'Playing' }
            if ($state -eq $Expected) { Start-Sleep -Seconds 2; return }
        }
        Start-Sleep -Milliseconds 500
    }
    throw "FeatherWall never reported a new '$Expected' event after $Because. The step did not do what it claims, so the row it would produce would be measuring something else."
}

function Measure-State {
    <#
      Runs one state and returns either its measurement or a record of why it was refused.

      A refusal used to abort the whole run from inside the try block, so the output section was
      never reached and every row already measured was thrown away with it. That is the wrong
      response to the one event this harness is built to produce: a refused row is a result, and
      it should be reported next to the rows that succeeded rather than deleting them.
    #>
    param([string] $Label, [ValidateSet('Playing', 'Paused')][string] $Expect, [scriptblock] $Setup)

    try {
        & $Setup
        $tagged = if ($Widgets) { "$Label + widgets" } else { $Label }
        return (& $measure -ProcessName featherwall -Seconds $Seconds -Label $tagged -ExpectState $Expect)
    }
    catch {
        # Trim each line: a Windows exception carries \r that -split "`n" leaves behind, and those
        # control characters ended up inside the committed results JSON.
        $reason = ($_.Exception.Message -split "`n" | ForEach-Object { $_.Trim() } |
                   Where-Object { $_ } | Select-Object -First 2) -join ' '
        Write-Host "  REFUSED: $reason" -ForegroundColor Yellow
        $tagged = if ($Widgets) { "$Label + widgets" } else { $Label }
        return [pscustomobject]@{ Label = $tagged; Refused = $reason; ExpectedState = $Expect }
    }
}

function Start-AndSettle {
    Start-Process $Exe
    # First frame, first static-fallback capture and the initial pause evaluation all happen in
    # the first few seconds. Measuring through them records startup, not steady state.
    Start-Sleep -Seconds 12
}

$results = [System.Collections.ArrayList]::new()

# Whether a config EXISTED decides what restoring even means, and the old code could not tell the
# two failures apart: a silenced Copy-Item error and "there was nothing to copy" both left no
# backup file, and the finally block then left the benchmark's own config in place as if it were
# the user's. A stale backup from an earlier interrupted run would restore the wrong config again.
$hadConfig = Test-Path $configPath
if (Test-Path $backup) { Remove-Item $backup -Force }

# Declared before the try so the finally block can always reach it — Measure-App.ps1 throws BY
# DESIGN when it refuses a row, which is precisely the path that used to leave a maximised
# Notepad sitting on the user's desktop.
$blocker = $null

try {
    if ($hadConfig) {
        # No -EA SilentlyContinue: failing to preserve a real config is not a warning.
        Copy-Item $configPath $backup -Force
        Write-Host "Config backed up to $backup" -ForegroundColor DarkGray
    } else {
        Write-Host "No existing config; the benchmark's own will be removed afterwards." -ForegroundColor DarkGray
    }

    # MinimizeAll on its own is not enough: it does not touch a full-screen window, and a
    # terminal left in full screen therefore stays over the desktop for the whole run. Every
    # playing row is then refused for a reason that reads like a FeatherWall fault rather than a
    # window nobody minimised. Measured on 26200 — a full-screen Windows Terminal survived
    # MinimizeAll and refused three of four rows. The foreground window is forced down first.
    $fg = [Bench.Fg]::GetForegroundWindow()
    if ($fg -ne [IntPtr]::Zero) { [void][Bench.Fg]::ShowWindow($fg, 11) }  # SW_FORCEMINIMIZE
    $shell.MinimizeAll()
    Start-Sleep -Seconds 2

    $covering = Find-CoveringWindow
    if ($covering) {
        throw "'$covering' is still covering the desktop after minimising everything — a full-screen " +
              "window ignores MinimizeAll and takes the foreground back. The playing rows cannot be " +
              "measured like this. Close it or leave full screen, then run again."
    }
    Write-Host "Windows minimised. Do not touch the machine until this finishes." -ForegroundColor Yellow

    # --- still image -----------------------------------------------------------------------
    Write-Host "[1/4] still image" -ForegroundColor Cyan
    [void]$results.Add((Measure-State -Label 'Still image' -Expect Playing -Setup {
        Stop-FeatherWall; Set-Wallpaper -Path $Image; Start-AndSettle
    }))

    # --- video playing ---------------------------------------------------------------------
    Write-Host "[2/4] video playing (desktop visible)" -ForegroundColor Cyan
    [void]$results.Add((Measure-State -Label 'Video playing' -Expect Playing -Setup {
        Stop-FeatherWall; Set-Wallpaper -Path $Video; Start-AndSettle
    }))

    # --- video paused ----------------------------------------------------------------------
    # A maximised window is what actually triggers the pause, so one is opened rather than the
    # pause being simulated. Measuring "paused" by trusting a config flag would measure nothing.
    Write-Host "[3/4] video auto-paused (maximised window over the desktop)" -ForegroundColor Cyan
    [void]$results.Add((Measure-State -Label 'Video auto-paused' -Expect Paused -Setup {
        $since = Get-LastPauseEvent
        [void](Open-Blocker)
        Wait-ForPauseState -Expected 'Paused' -Because 'the maximised blocker window' -Since $since
    }))
    Close-Blocker
    Start-Sleep -Seconds 2

    # --- settings panel open ---------------------------------------------------------------
    # The settings panel is the one place a UI toolkit is used, and it is loaded on demand. This
    # row is what that claim costs when the claim is being exercised.
    Write-Host "[4/4] settings panel open" -ForegroundColor Cyan
    [void]$results.Add((Measure-State -Label 'Settings panel open' -Expect Paused -Setup {
        $since = Get-LastPauseEvent
        Start-Process $Exe -ArgumentList '--settings'
        Wait-ForPauseState -Expected 'Paused' -Because 'the settings panel opening over the desktop' -Since $since
    }))
}
finally {
    Close-Blocker
    $shell.UndoMinimizeALL()
    Stop-FeatherWall

    if ($hadConfig) {
        if (Test-Path $backup) {
            Move-Item $backup $configPath -Force
            Write-Host "Config restored." -ForegroundColor DarkGray
        } else {
            Write-Warning "Backup $backup is gone - config.json is the benchmark's, not yours."
        }
    }
    elseif (Test-Path $configPath) {
        Remove-Item $configPath -Force
        Write-Host "Removed the benchmark's config." -ForegroundColor DarkGray
    }

    Start-Process $Exe
}

# ---- output ---------------------------------------------------------------------------------

$stamp = (Get-Date).ToUniversalTime().ToString('yyyyMMdd-HHmmss')
$outDir = Join-Path $PSScriptRoot 'results'
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
$json = Join-Path $outDir "featherwall-$stamp.json"
$results | ConvertTo-Json -Depth 4 | Set-Content -Path $json -Encoding UTF8

Write-Host ""
# Ten columns including Verified, matching the schema in docs/benchmark.md — this output is meant
# to be pasted straight in, and a row with nine cells silently shifts every value left of it.
Write-Host "| State | Processes | Private MB | Working set MB | GPU dedicated MB | GPU shared MB | CPU (machine) | CPU (one core) | Busiest GPU engine | Verified |"
Write-Host "|---|---|---|---|---|---|---|---|---|---|"
foreach ($r in $results) {
    if ($r.PSObject.Properties.Name -contains 'Refused') {
        # Blank cells, not zeroes. docs/benchmark.md already reads an empty cell as "nobody
        # measured it", and a refused row is exactly that plus a reason.
        "| {0} | | | | | | | | | REFUSED |" -f $r.Label
    } else {
        # No engine name when nothing was busy — "0 % ()" is a stray empty bracket in a table
        # that gets pasted straight into docs/benchmark.md.
        $engine = if ($r.BusiestGpuEngine) { " ($($r.BusiestGpuEngine))" } else { "" }
        "| {0} | {1} | {2} | {3} | {4} | {5} | {6} % | {7} % | {8} %{9} | {10} |" -f `
            $r.Label, $r.Processes, $r.PrivateMB, $r.WorkingSetMB, $r.GpuDedicatedMB, $r.GpuSharedMB,
            $r.CpuPercentMachine, $r.CpuPercentOneCore, $r.BusiestGpuPercent, $engine, $r.PauseState
    }
}

$refused = @($results | Where-Object { $_.PSObject.Properties.Name -contains 'Refused' })
if ($refused.Count -gt 0) {
    Write-Host ""
    Write-Host "$($refused.Count) of $($results.Count) rows refused:" -ForegroundColor Yellow
    foreach ($r in $refused) { Write-Host "  $($r.Label): $($r.Refused)" -ForegroundColor DarkYellow }
}

Write-Host ""
Write-Host "Raw results: $json" -ForegroundColor DarkGray
if ($refused.Count -gt 0) { exit 1 }
