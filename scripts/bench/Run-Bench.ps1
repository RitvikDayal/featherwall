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
    [string] $Exe = "$PSScriptRoot\..\..\src\FeatherWall\bin\Release\net10.0-windows10.0.19041.0\featherwall.exe"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$measure = Join-Path $PSScriptRoot 'Measure-App.ps1'
$configPath = Join-Path $env:APPDATA 'FeatherWall\config.json'
$backup = "$configPath.bench-backup"
$shell = New-Object -ComObject Shell.Application

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
    $config | ConvertTo-Json -Depth 6 | Set-Content -Path $configPath -Encoding UTF8
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

    $shell.MinimizeAll()
    Start-Sleep -Seconds 2
    Write-Host "Windows minimised. Do not touch the machine until this finishes." -ForegroundColor Yellow

    # --- still image -----------------------------------------------------------------------
    Write-Host "[1/4] still image" -ForegroundColor Cyan
    Stop-FeatherWall; Set-Wallpaper -Path $Image; Start-AndSettle
    [void]$results.Add((& $measure -ProcessName featherwall -Seconds $Seconds -Label 'Still image' -ExpectState Playing))

    # --- video playing ---------------------------------------------------------------------
    Write-Host "[2/4] video playing (desktop visible)" -ForegroundColor Cyan
    Stop-FeatherWall; Set-Wallpaper -Path $Video; Start-AndSettle
    [void]$results.Add((& $measure -ProcessName featherwall -Seconds $Seconds -Label 'Video playing' -ExpectState Playing))

    # --- video paused ----------------------------------------------------------------------
    # A maximised window is what actually triggers the pause, so one is opened rather than the
    # pause being simulated. Measuring "paused" by trusting a config flag would measure nothing.
    Write-Host "[3/4] video auto-paused (maximised window over the desktop)" -ForegroundColor Cyan
    $blocker = Start-Process notepad -PassThru
    Start-Sleep -Seconds 3
    $sig = '[DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int c);'
    $u = Add-Type -MemberDefinition $sig -Name BenchWin -Namespace W -PassThru
    [void]$u::ShowWindow($blocker.MainWindowHandle, 3) # SW_MAXIMIZE
    Start-Sleep -Seconds 8
    [void]$results.Add((& $measure -ProcessName featherwall -Seconds $Seconds -Label 'Video auto-paused' -ExpectState Paused))
    Stop-Process -Id $blocker.Id -Force -EA SilentlyContinue
    Start-Sleep -Seconds 2

    # --- settings panel open ---------------------------------------------------------------
    # The settings panel is the one place a UI toolkit is used, and it is loaded on demand. This
    # row is what that claim costs when the claim is being exercised.
    Write-Host "[4/4] settings panel open" -ForegroundColor Cyan
    Start-Process $Exe -ArgumentList '--settings'
    Start-Sleep -Seconds 8
    [void]$results.Add((& $measure -ProcessName featherwall -Seconds $Seconds -Label 'Settings panel open' -ExpectState Paused))
}
finally {
    if ($blocker) { Stop-Process -Id $blocker.Id -Force -EA SilentlyContinue }
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
Write-Host "| State | Processes | Private MB | Working set MB | GPU dedicated MB | GPU shared MB | CPU (machine) | CPU (one core) | Busiest GPU engine |"
Write-Host "|---|---|---|---|---|---|---|---|---|"
foreach ($r in $results) {
    "| {0} | {1} | {2} | {3} | {4} | {5} | {6} % | {7} % | {8} % ({9}) |" -f `
        $r.Label, $r.Processes, $r.PrivateMB, $r.WorkingSetMB, $r.GpuDedicatedMB, $r.GpuSharedMB,
        $r.CpuPercentMachine, $r.CpuPercentOneCore, $r.BusiestGpuPercent, $r.BusiestGpuEngine
}
Write-Host ""
Write-Host "Raw results: $json" -ForegroundColor DarkGray
