<#
.SYNOPSIS
    Measures what a running wallpaper application costs: processes, memory, CPU, GPU.

.DESCRIPTION
    Written so FeatherWall's own numbers can be reproduced by anyone, and so a competitor can
    be measured by the same code on the same machine. Quoting one vendor's marketing against
    another's is not a comparison.

    Deliberate choices, each of which changes the answer:

    * The whole PROCESS TREE is measured, not one process. This is the entire point of the
      comparison — a tool that runs a launcher plus a browser subprocess plus a player is
      spending that memory whether or not its main window is the one you named.

    * CPU comes from TotalProcessorTime deltas over wall-clock, not from a performance
      counter. It is exact rather than sampled and it makes the denominator explicit instead of
      hidden. It is accumulated PER PID across the whole window, not differenced between the
      first and last process tree: a child that burns CPU and exits before the end is in the
      start tree and not the end one, so the endpoint subtraction lost its time entirely and
      under-reported the app. This file used to claim the measurement survived a tree that
      changes shape while doing exactly that.

    * Both CPU denominators are reported. "0.8% of a 24-core machine" and "19% of one core"
      are the same measurement, and quoting only the first is how a busy process is made to
      look free.

    * GPU is read per-engine from '\GPU Engine(*)' filtered to our pids. Windows reports GPU
      work per engine and those percentages DO NOT sum to a total, so the busiest single
      engine is reported along with its name, which is roughly what Task Manager shows.
      If the counters cannot be read — they are localised, so these English paths do not
      resolve on every Windows — the GPU fields are NULL and GpuCounters says so. Reporting
      an unreadable counter as 0 would publish a perfect GPU row for a video engine.

    * Dedicated and shared GPU memory are reported separately, and BOTH are real. A modern
      integrated GPU does carve out dedicated memory — measured 2026-08-20, Intel Arc reports
      2048 MB of adapter RAM and Windows attributed 231 MB of *dedicated* usage to FeatherWall
      on it. The README used to claim dedicated always reads 0 on integrated graphics; that was
      wrong, and finding it is why this script exists. Neither number is inside the working set,
      so the honest total for a wallpaper is working set plus both.

.PARAMETER ProcessName
    Process name without .exe. All matching processes and their descendants are measured.

.PARAMETER Seconds
    Sampling window. Longer is better; 30 is a reasonable floor for a video wallpaper.

.PARAMETER Label
    Free text recorded with the result, e.g. "video 1080p60 playing".

.PARAMETER ExpectState
    Playing, Paused, or Any. For FeatherWall only: reads the log and REFUSES to report a row
    whose pause state was not the expected one for the whole window.

    This is the most important thing in this file. FeatherWall stops decoding when a window
    covers the desktop, so a benchmark run on a machine someone is using will silently record a
    paused wallpaper as a cheap playing one — which is the single most flattering lie this
    project could tell about itself. The first run of this harness did exactly that: it reported
    "video playing" at 3.3% video decode while the log showed the wallpaper pausing and resuming
    three times inside the sampling window.

.PARAMETER LogPath
    FeatherWall's log. Defaults to the standard location.

.EXAMPLE
    .\Measure-App.ps1 -ProcessName featherwall -Seconds 30 -Label "video 4K60 playing"

.EXAMPLE
    .\Measure-App.ps1 -ProcessName Lively -Seconds 30 -Label "video 1080p60 playing"
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $ProcessName,
    [ValidateRange(1, 86400)][int] $Seconds = 30,
    [string] $Label = "",
    [ValidateSet('Playing', 'Paused', 'Any')][string] $ExpectState = 'Any',
    [string] $LogPath = "$env:LOCALAPPDATA\FeatherWall\featherwall.log"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Get-ProcessTree {
    param([string] $Name)

    $roots = @(Get-Process -Name $Name -ErrorAction SilentlyContinue)
    if ($roots.Count -eq 0) { return ,@() }

    # One CIM query, then walk in memory. Querying per-process is slow enough to perturb a
    # short sampling window on a machine with many processes.
    # Both ids are cast to [int]. Win32_Process reports UInt32 and Diagnostics.Process.Id is
    # Int32, and a Hashtable does not consider a boxed UInt32 equal to a boxed Int32 — so a
    # lookup keyed on a root's .Id missed every entry and the walk found no children at all.
    # FeatherWall is one process, so its rows were unaffected and nothing looked wrong; a
    # multi-process competitor would have been measured as its root alone, which is the whole
    # comparison this script exists for, wrong in the direction that flatters FeatherWall.
    $all = Get-CimInstance Win32_Process -Property ProcessId, ParentProcessId, Name
    $byParent = @{}
    foreach ($p in $all) {
        $parent = [int]$p.ParentProcessId
        if (-not $byParent.ContainsKey($parent)) { $byParent[$parent] = [System.Collections.ArrayList]::new() }
        [void]$byParent[$parent].Add([int]$p.ProcessId)
    }

    $seen = [System.Collections.Generic.HashSet[int]]::new()
    $queue = [System.Collections.Queue]::new()
    foreach ($r in $roots) { [void]$seen.Add([int]$r.Id); $queue.Enqueue([int]$r.Id) }

    while ($queue.Count -gt 0) {
        # Not $pid — that is a read-only automatic variable holding this shell's own id.
        $current = [int]$queue.Dequeue()
        if (-not $byParent.ContainsKey($current)) { continue }
        foreach ($child in $byParent[$current]) {
            if ($seen.Add($child)) { $queue.Enqueue($child) }
        }
    }

    $result = [System.Collections.ArrayList]::new()
    foreach ($id in $seen) {
        $proc = Get-Process -Id $id -ErrorAction SilentlyContinue
        if ($proc) { [void]$result.Add($proc) }
    }
    # Leading comma stops PowerShell unrolling a one-element collection into a bare object,
    # which then has no .Count and fails under StrictMode. A single-process app is the normal
    # case for FeatherWall, so this path is the one that matters most.
    return ,@($result.ToArray())
}

function Get-GpuSample {
    param([int[]] $Pids)

    # Instance names look like: pid_1234_luid_0x..._phys_0_eng_3_engtype_VideoDecode
    #
    # Keyed on (luid, phys, eng) — the identity of a physical engine — and NOT on engtype. Two
    # different VideoDecode engines are two pieces of hardware; adding them produces a number that
    # cannot be compared with Task Manager and can exceed 100%. Summing across pids within one
    # engine is correct, because that is this process tree's share of that one engine.
    $engines = @{}
    $dedicated = 0.0
    $shared = 0.0
    $enginesOk = $false
    $memoryOk = $false

    try {
        $util = (Get-Counter '\GPU Engine(*)\Utilization Percentage' -ErrorAction Stop).CounterSamples
        foreach ($s in $util) {
            $m = [regex]::Match($s.InstanceName, '^pid_(\d+)_luid_(.+?)_phys_(\d+)_eng_(\d+)_engtype_(.+)$')
            if (-not $m.Success) { continue }
            if ($Pids -notcontains [int]$m.Groups[1].Value) { continue }
            $key = "$($m.Groups[2].Value)|$($m.Groups[3].Value)|$($m.Groups[4].Value)"
            if (-not $engines.ContainsKey($key)) {
                $engines[$key] = [pscustomobject]@{ Name = $m.Groups[5].Value; Value = 0.0 }
            }
            $engines[$key].Value += $s.CookedValue
        }
        $enginesOk = $true
    } catch {
        Write-Verbose "GPU Engine counters unavailable: $_"
    }

    try {
        foreach ($name in @('Dedicated Usage', 'Shared Usage')) {
            $samples = (Get-Counter "\GPU Process Memory(*)\$name" -ErrorAction Stop).CounterSamples
            foreach ($s in $samples) {
                $m = [regex]::Match($s.InstanceName, '^pid_(\d+)_')
                if (-not $m.Success) { continue }
                if ($Pids -notcontains [int]$m.Groups[1].Value) { continue }
                if ($name -eq 'Dedicated Usage') { $dedicated += $s.CookedValue } else { $shared += $s.CookedValue }
            }
        }
        $memoryOk = $true
    } catch {
        Write-Verbose "GPU Process Memory counters unavailable: $_"
    }

    # Availability travels with the sample. A counter that could not be read is not zero usage,
    # and on a localised Windows these counter paths do not resolve at all — which would have
    # published a flawless-looking 0 MB / 0 %% GPU row for a video wallpaper engine.
    return [pscustomobject]@{
        Engines = $engines; EnginesOk = $enginesOk
        DedicatedBytes = $dedicated; SharedBytes = $shared; MemoryOk = $memoryOk
    }
}

function Get-PauseEvents {
    <#
      Every Paused/Resumed line FeatherWall logged, newest last. Reading the app's own log is
      the only non-circular way to know what state was measured — inferring it from the GPU
      numbers would be using the answer to check the answer.
    #>
    param([string] $Path)

    if (-not (Test-Path $Path)) { return ,@() }
    $events = [System.Collections.ArrayList]::new()
    foreach ($line in Get-Content $Path -Tail 600) {
        # The milliseconds are captured, not discarded. Log.cs writes 'HH:mm:ss.fff', and
        # truncating to the second puts an event at 14:00:00.900 BEFORE a window that opened at
        # 14:00:00.500 — so a state change inside the first half-second is classified as having
        # happened before the run, and the row it should have discarded gets reported.
        # The device is captured, because pause state is PER MONITOR: Engine.cs logs
        # "Paused \\.\DISPLAY2: Fullscreen" and one monitor can be paused while another plays.
        # Collapsing them to a single app-wide state accepts a mixed run as a clean one.
        #
        # Anchoring on a device path also rejects "Resumed from sleep - refreshing clock", which
        # the previous pattern parsed as a pause event on a monitor named "from".
        $m = [regex]::Match($line, '^(\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3}) \[INF\] (Paused|Resumed) (\\\\[.?]\\[^\s:]+)')
        if (-not $m.Success) { continue }
        [void]$events.Add([pscustomobject]@{
            # InvariantCulture at both ends. '-' and ':' are culture-sensitive placeholders in a
            # custom format string, so a machine with different date or time separators writes and
            # reads a different shape; Log.cs is pinned to invariant for the same reason.
            At     = [datetime]::ParseExact($m.Groups[1].Value, 'yyyy-MM-dd HH:mm:ss.fff', [Globalization.CultureInfo]::InvariantCulture)
            State  = if ($m.Groups[2].Value -eq 'Paused') { 'Paused' } else { 'Playing' }
            Device = $m.Groups[3].Value
        })
    }
    return ,@($events.ToArray())
}

# ---- measure -----------------------------------------------------------------------------

$cores = [Environment]::ProcessorCount
$tree = Get-ProcessTree -Name $ProcessName
if ($tree.Count -eq 0) { throw "No process named '$ProcessName' is running." }

$startPids = @($tree | ForEach-Object { $_.Id })

# pid -> CPU seconds already burned when we first saw it, and the last total we observed for it.
# A pid first seen mid-window gets a zero baseline, because everything it has ever spent happened
# inside the window. Retaining the last total after a pid disappears is the whole point.
$cpuBaseline = @{}
$cpuLast = @{}
# Pids that existed before the window opened. A pid absent from $cpuBaseline is otherwise
# indistinguishable from a brand-new one, and giving a pre-existing process a zero baseline
# charges its ENTIRE lifetime of CPU to this window.
$startPids = [System.Collections.Generic.HashSet[int]]::new()
$readErrors = 0

# BEFORE the baseline reads, as $wallEnd is taken after the closing ones. Baselines are collected
# sequentially, so with $wallStart set afterwards the numerator covered the collection itself while
# the denominator did not — inflating both percentages by more the bigger the tree, which is
# backwards for a script whose reason to exist is measuring a competitor's larger tree fairly.
# Bracketing the window outside both snapshots leaves a bias that under-reports by at most the
# snapshot cost, which is the direction an honest benchmark should err in.
$wallStart = Get-Date
foreach ($p in $tree) {
    [void]$startPids.Add([int]$p.Id)
    try {
        $t = $p.TotalProcessorTime.TotalSeconds
        $cpuBaseline[$p.Id] = $t
        $cpuLast[$p.Id] = $t
    } catch { $readErrors++ }
}

# GPU and memory are instantaneous, so they are averaged across the window rather than read once.
$gpuSamples = [System.Collections.ArrayList]::new()
$memSamples = [System.Collections.ArrayList]::new()

# Loop on elapsed time, not on a tick count. Get-Counter blocks for a second or more per call
# and there are two of them per tick, so a fixed count of 2-second sleeps overshot the requested
# window by 3x — the CPU maths stayed correct because it divides by real elapsed time, but
# "-Seconds 10" that measures for 31 seconds is a lie in the parameter name.
$deadline = $wallStart.AddSeconds($Seconds)
while ((Get-Date) -lt $deadline) {
    $now = Get-ProcessTree -Name $ProcessName
    if ($now.Count -eq 0) { throw "'$ProcessName' exited during the sampling window." }

    $ws = 0; $pws = 0; $threads = 0; $handles = 0
    foreach ($p in $now) {
        try {
            $ws += $p.WorkingSet64
            $pws += $p.PrivateMemorySize64
            $threads += $p.Threads.Count
            $handles += $p.HandleCount
            $t = $p.TotalProcessorTime.TotalSeconds
            if (-not $cpuBaseline.ContainsKey($p.Id)) {
                # A pid whose baseline read failed at the start is NOT a new process. Anchoring it
                # here attributes zero CPU to the part of the window before it became readable,
                # which is a small under-count; a zero baseline would have charged its whole
                # lifetime to this window instead, which is unbounded.
                $cpuBaseline[$p.Id] = if ($startPids.Contains([int]$p.Id)) { $t } else { 0.0 }
            }
            $cpuLast[$p.Id] = $t
        } catch { $readErrors++ }
    }
    [void]$memSamples.Add([pscustomobject]@{
        Processes = $now.Count; WorkingSet = $ws; Private = $pws; Threads = $threads; Handles = $handles
    })

    [void]$gpuSamples.Add((Get-GpuSample -Pids @($now | ForEach-Object { $_.Id })))

    # Only sleep if the counter reads were quicker than the sampling cadence.
    $remaining = ($deadline - (Get-Date)).TotalSeconds
    if ($remaining -gt 1) { Start-Sleep -Milliseconds 500 }
}

$endTree = Get-ProcessTree -Name $ProcessName
foreach ($p in $endTree) {
    try {
        $t = $p.TotalProcessorTime.TotalSeconds
        if (-not $cpuBaseline.ContainsKey($p.Id)) {
            $cpuBaseline[$p.Id] = if ($startPids.Contains([int]$p.Id)) { $t } else { 0.0 }
        }
        $cpuLast[$p.Id] = $t
    } catch { $readErrors++ }
}
# AFTER the final CPU snapshot, not before it. Get-ProcessTree runs a CIM query that takes real
# time on a machine with many processes, and CPU burned during it landed in $cpuLast while being
# excluded from $elapsed — inflating both CPU percentages by whatever the enumeration cost.
$wallEnd = Get-Date

$elapsed = ($wallEnd - $wallStart).TotalSeconds
$cpuSeconds = 0.0
foreach ($id in $cpuLast.Keys) {
    $cpuSeconds += [math]::Max($cpuLast[$id] - $cpuBaseline[$id], 0)
}

# Refuse to report a row whose pause state moved under it. A benchmark that quietly averages a
# paused wallpaper into a "playing" row is worse than no benchmark, because it is wrong in the
# flattering direction and nothing in the output shows it.
$stateNote = 'not checked'
if ($ExpectState -ne 'Any') {
    # The state comes from FeatherWall's log, so it can only describe FeatherWall. Measuring a
    # competitor with -ExpectState would have read THIS app's log and discarded or accepted the
    # competitor's row on the strength of FeatherWall's monitors — a verdict about the wrong
    # program, delivered with the same confidence as a real one.
    if ($ProcessName -ine 'featherwall') {
        throw "-ExpectState is only meaningful for ProcessName 'featherwall'; it is read from FeatherWall's own log. Measure '$ProcessName' with -ExpectState Any."
    }

    $events = Get-PauseEvents -Path $LogPath

    # Two separate things have to be true, and checking only the first is a hole: a wallpaper
    # that was paused for the WHOLE window has no transitions in it, so a transitions-only check
    # would happily report a paused measurement as a playing one.
    $inside = @($events | Where-Object { $_.At -ge $wallStart -and $_.At -le $wallEnd })

    if ($inside.Count -gt 0) {
        $detail = ($inside | ForEach-Object { "  $($_.At.ToString('HH:mm:ss.fff'))  $($_.Device) -> $($_.State)" }) -join "`n"
        throw @"
DISCARDED: the wallpaper changed pause state during the '$Label' window, so this number is an
average of two different states and means nothing.

$detail

Expected every monitor to stay $ExpectState for the whole $([math]::Round($elapsed))s.
"@
    }

    # Per monitor, because the state is per monitor. The app-wide "latest event wins" version
    # accepted a run where DISPLAY1 was paused and DISPLAY2 was playing, which is two different
    # measurements averaged into one row.
    $devices = @($events | ForEach-Object { $_.Device } | Sort-Object -Unique)
    if ($devices.Count -eq 0) {
        throw @"
DISCARDED: no pause-state events for any monitor were found in $LogPath, so the state during the
'$Label' window is unknown and cannot be verified. An unverified row is not a row.
"@
    }

    $wrong = [System.Collections.ArrayList]::new()
    foreach ($device in $devices) {
        $before = @($events | Where-Object { $_.Device -eq $device -and $_.At -lt $wallStart })
        $entering = if ($before.Count -gt 0) { $before[-1].State } else { 'unknown' }
        if ($entering -ne $ExpectState) { [void]$wrong.Add("  $device was '$entering'") }
    }

    if ($wrong.Count -gt 0) {
        throw @"
DISCARDED: not every monitor was '$ExpectState' for the whole '$Label' window.

$($wrong -join "`n")

FeatherWall stops decoding when a window covers the desktop, so measuring a 'playing' row on a
machine that is in use records a paused wallpaper as a cheap playing one. That is the most
flattering lie this project could tell about itself, so it is refused rather than reported.

Close anything covering the desktop — a maximised window, a game, a fullscreen video — and run
it again on an otherwise idle machine.
"@
    }

    $stateNote = "$ExpectState, verified for the full window on $($devices.Count) monitor(s): $($devices -join ', ')"
}

# Average each engine across the window, then report the busiest one. Summing engines would
# produce a number that means nothing.
$engineTotals = @{}
foreach ($s in $gpuSamples) {
    foreach ($k in $s.Engines.Keys) {
        if (-not $engineTotals.ContainsKey($k)) {
            $engineTotals[$k] = [pscustomobject]@{ Name = $s.Engines[$k].Name; Sum = 0.0 }
        }
        $engineTotals[$k].Sum += $s.Engines[$k].Value
    }
}
# Only samples whose counters actually read are averaged, and if none did the metric is null
# rather than zero. "Unavailable" and "measured zero" are different answers and this harness
# exists to stop the second one being printed when the first is true.
$engineOkSamples = @($gpuSamples | Where-Object { $_.EnginesOk })
$memoryOkSamples = @($gpuSamples | Where-Object { $_.MemoryOk })

$busiest = $null; $busiestValue = $null
if ($engineOkSamples.Count -gt 0) {
    $busiestValue = 0.0
    foreach ($k in $engineTotals.Keys) {
        $avg = $engineTotals[$k].Sum / $engineOkSamples.Count
        if ($avg -gt $busiestValue) { $busiestValue = $avg; $busiest = $engineTotals[$k].Name }
    }
}

$counterNotes = [System.Collections.ArrayList]::new()
if ($engineOkSamples.Count -eq 0) { [void]$counterNotes.Add('GPU Engine counters unavailable') }
elseif ($engineOkSamples.Count -lt $gpuSamples.Count) { [void]$counterNotes.Add("GPU Engine counters read on $($engineOkSamples.Count)/$($gpuSamples.Count) samples") }
if ($memoryOkSamples.Count -eq 0) { [void]$counterNotes.Add('GPU Process Memory counters unavailable') }
elseif ($memoryOkSamples.Count -lt $gpuSamples.Count) { [void]$counterNotes.Add("GPU Process Memory counters read on $($memoryOkSamples.Count)/$($gpuSamples.Count) samples") }
$counterNote = if ($counterNotes.Count -eq 0) { 'all read' } else { $counterNotes -join '; ' }

# @() before .Count: a pipeline that yields exactly one value is unrolled to a bare scalar, which
# has no .Count and dies under StrictMode. One sample happens whenever the window is short or the
# tree is big enough that a single pass fills it — found measuring a 57-process tree, which is
# exactly the competitor shape this script is for.
function Avg($values) { $v = @($values); if ($v.Count -eq 0) { 0 } else { ($v | Measure-Object -Average).Average } }

[pscustomobject]@{
    App               = $ProcessName
    Label             = $Label
    SampledSeconds    = [math]::Round($elapsed, 1)
    Processes         = [int](Avg ($memSamples | ForEach-Object { $_.Processes }))
    Threads           = [int](Avg ($memSamples | ForEach-Object { $_.Threads }))
    Handles           = [int](Avg ($memSamples | ForEach-Object { $_.Handles }))
    WorkingSetMB      = [math]::Round((Avg ($memSamples | ForEach-Object { $_.WorkingSet })) / 1MB, 1)
    PrivateMB         = [math]::Round((Avg ($memSamples | ForEach-Object { $_.Private })) / 1MB, 1)
    GpuDedicatedMB    = if ($memoryOkSamples.Count -eq 0) { $null } else { [math]::Round((Avg ($memoryOkSamples | ForEach-Object { $_.DedicatedBytes })) / 1MB, 1) }
    GpuSharedMB       = if ($memoryOkSamples.Count -eq 0) { $null } else { [math]::Round((Avg ($memoryOkSamples | ForEach-Object { $_.SharedBytes })) / 1MB, 1) }
    CpuPercentMachine = [math]::Round(100 * $cpuSeconds / ($elapsed * $cores), 3)
    CpuPercentOneCore = [math]::Round(100 * $cpuSeconds / $elapsed, 1)
    BusiestGpuEngine  = $busiest
    BusiestGpuPercent = if ($null -eq $busiestValue) { $null } else { [math]::Round($busiestValue, 1) }
    GpuCounters       = $counterNote
    # Non-zero means some process property could not be read at some point, so this row is
    # missing part of a contribution. Reported rather than swallowed: the numbers still look
    # complete, and only this field says whether they are.
    ProcessReadErrors = $readErrors
    Cores             = $cores
    PauseState        = $stateNote
    MeasuredAtUtc     = $wallStart.ToUniversalTime().ToString('s') + 'Z'
}
