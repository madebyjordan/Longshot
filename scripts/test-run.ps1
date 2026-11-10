# A test run on the game folder you play, without copying it: a run folder of hard links (no bytes written for the exe,
# image.bin or the shader list), the renderer under test copied in (6 MB), the game's romfs used where it lies (--romfs), a
# copy of the saves (1 MB), and the caches chosen by -Cache. The game folder itself is never written: not its bin, caches,
# settings, saves, env file or logs. The run's log, summary and screenshots go to <runs>\<name>; the run folder is deleted
# afterwards unless -Keep.
#
#   powershell -File scripts/test-run.ps1 -Game <game folder> [-Renderer <hookshot_vk.dll>] [-Exe <hookshot.exe>] [-Name x]
#       [-Cache cold|game|<folder>] [-Env "A=1","B=2"] [-NoGameEnv] [-Buttons "0:LUP/2000,2:A"] [-FrameClock]
#       [-Seconds 120] [-Headless] [-Mute] [-ShotEvery 300] [-Prepare] [-Keep] [-Runs <folder>] [-MaxGB 12] [-Force] [-DryRun]
#
# -Game          the game folder (default: LONGSHOT_GAME). The run folder goes beside it (<game>\..\runs) unless -Runs: it
#                must be on the same drive, for the hard links.
# -Renderer/-Exe the hookshot_vk.dll / hookshot.exe to test (a build's, or one kept in renderers\); the game's without them.
# -Cache cold    (default) caches start empty in the run folder: a first start. AMD's own driver cache stays warm; for a
#                fully cold first start use bench\bench_prepare.ps1.
#        game    the game's caches, read-only (RX_VK_CACHE_DIR + RX_VK_CACHE_READONLY): a warm start that cannot change or
#                start them again, whatever the renderer's stamp. A renderer from before these switches ignores them and
#                starts cold, in the run folder; the script says so. A renderer whose shader translator changed (its
#                stamp: vk_shader.cpp, Eden, sirit) cannot use the game's caches at all, and what it prepares is not kept:
#                give it -Cache <folder> instead.
#        <folder> a cache folder of its own, read and written (made if missing): a warm cache for one line of work that is
#                not the game's. Kept after the run.
# -Env           extra NAME=VALUE switches, added to the game's hookshot_env.txt (copied; -NoGameEnv leaves it out).
# -Buttons       scripted input (HOS_INPUT_SCRIPT); -FrameClock times it by frames (HOS_INPUT_FRAME_CLOCK=1).
# -Seconds       stop the game after this long (0, the default: wait until it is closed).
# -Headless      no window (HOS_HEADLESS); otherwise the window opens without taking focus. Muted when headless or
#                scripted, or with -Mute.
# -ShotEvery     a screenshot every n presents, into <runs>\<name>\shots (they reach gigabytes: delete them once seen).
# -Prepare       the builder's shader prepare only (--prepare-shaders), no game: times the first start's work.
# -DryRun        make the run folder, list it and what would start, then delete it: no game.
#
# Guards: one game at a time (no run while hookshot.exe runs; -Force overrides), and the game is stopped when its working
# set passes -MaxGB.
param([string]$Game = $env:LONGSHOT_GAME, [string]$Renderer = "", [string]$Exe = "", [string]$Name = "run",
      [string]$Cache = "cold", [string[]]$Env = @(), [switch]$NoGameEnv, [string]$Buttons = "", [switch]$FrameClock,
      [int]$Seconds = 0, [switch]$Headless, [switch]$Mute, [int]$ShotEvery = 0, [switch]$Prepare, [switch]$Keep,
      [string]$Runs = "", [int]$MaxGB = 12, [switch]$Force, [switch]$DryRun)
$ErrorActionPreference = "Stop"

if (-not $Game) { throw "No game folder: pass -Game <folder> or set LONGSHOT_GAME." }
$Game = (Resolve-Path $Game).Path.TrimEnd('\')
foreach ($need in "bin\hookshot.exe", "image.bin", "romfs") {
    if (-not (Test-Path (Join-Path $Game $need))) { throw "$Game is not a game folder (no $need)." }
}
if (-not $Force -and -not $DryRun -and (Get-Process hookshot -ErrorAction SilentlyContinue)) {
    throw "A game is running (hookshot.exe): one at a time. Close it, or pass -Force."
}
if (-not $Runs) { $Runs = Join-Path (Split-Path -Parent $Game) "runs" }
$stamp = Get-Date -Format "yyyyMMdd-HHmmss"
$out = Join-Path $Runs "$stamp-$Name"            # (kept: log, summary, shots)
$run = Join-Path $out "game"                     # (deleted after: the run folder)
New-Item -ItemType Directory -Force "$run\bin" | Out-Null
if ((Split-Path -Qualifier $run) -ne (Split-Path -Qualifier $Game)) { throw "-Runs must be on the game's drive (hard links)." }

function Link($from, $to) { New-Item -ItemType HardLink -Path $to -Target $from | Out-Null }

# ---- the run folder: hard links to what the game only reads, copies of what it may write ----
foreach ($f in Get-ChildItem "$Game\bin" -File) { Link $f.FullName "$run\bin\$($f.Name)" }
if ($Renderer) {
    Remove-Item "$run\bin\hookshot_vk.dll" -Force -ErrorAction SilentlyContinue
    Copy-Item (Resolve-Path $Renderer).Path "$run\bin\hookshot_vk.dll"   # (a copy: a later build cannot change it mid-run)
}
if ($Exe) {
    Remove-Item "$run\bin\hookshot.exe" -Force
    $exeFrom = (Resolve-Path $Exe).Path
    if ((Split-Path -Qualifier $exeFrom) -eq (Split-Path -Qualifier $run)) { Link $exeFrom "$run\bin\hookshot.exe" }
    else { Copy-Item $exeFrom "$run\bin\hookshot.exe" }
}
foreach ($n in "image.bin", "hookshot_shaders.bin", "hookshot_pipeline_states_shipped.bin") {
    if (Test-Path "$Game\$n") { Link "$Game\$n" "$run\$n" }
}
foreach ($n in "language.txt", "hookshot_options.ini", "hookshot_version.txt") {
    if (Test-Path "$Game\$n") { Copy-Item "$Game\$n" "$run\$n" }
}
if (Test-Path "$Game\hookshot_prepare_skipped") { Copy-Item "$Game\hookshot_prepare_skipped" $run }
if (Test-Path "$Game\user") { Copy-Item "$Game\user" "$run\user" -Recurse } else { New-Item -ItemType Directory "$run\user" | Out-Null }

$lines = @("HOOKSHOT_NO_UPDATE_CHECK=1")
if (-not $NoGameEnv -and (Test-Path "$Game\hookshot_env.txt")) { $lines += Get-Content "$Game\hookshot_env.txt" }
$lines += $Env
Set-Content "$run\hookshot_env.txt" $lines -Encoding ascii

# ---- the caches ----
$dll = "$run\bin\hookshot_vk.dll"
$knows = [System.Text.Encoding]::ASCII.GetString([System.IO.File]::ReadAllBytes($dll)).Contains("RX_VK_CACHE_READONLY")
$cacheNote = "cold (the run folder)"
foreach ($n in "RX_VK_CACHE_DIR", "RX_VK_CACHE_READONLY") { Remove-Item "Env:$n" -ErrorAction SilentlyContinue }
if ($Cache -eq "game") {
    if ($knows) {
        $env:RX_VK_CACHE_DIR = $Game; $env:RX_VK_CACHE_READONLY = "1"
        $cacheNote = "the game's, read-only ($Game)"
    } else {
        $cacheNote = "cold: this renderer predates RX_VK_CACHE_DIR, so it cannot read the game's caches without writing them"
    }
} elseif ($Cache -ne "cold") {
    New-Item -ItemType Directory -Force $Cache | Out-Null
    $Cache = (Resolve-Path $Cache).Path
    if ($knows) { $env:RX_VK_CACHE_DIR = $Cache; $cacheNote = "$Cache (read and written)" }
    else { $cacheNote = "cold: this renderer predates RX_VK_CACHE_DIR" }
}

# ---- the run ----
foreach ($n in "HOS_HEADLESS", "HOS_WINDOW_NOACTIVATE", "HOS_AUDIO", "HOS_INPUT_SCRIPT", "HOS_INPUT_FRAME_CLOCK",
               "RX_VK_SHOTS", "RX_VK_SHOT_EVERY", "HOS_ENV_FILE") { Remove-Item "Env:$n" -ErrorAction SilentlyContinue }
if ($Headless) { $env:HOS_HEADLESS = "1" } else { $env:HOS_WINDOW_NOACTIVATE = "1" }
if ($Headless -or $Buttons -or $Mute) { $env:HOS_AUDIO = "0" }
if ($Buttons) { $env:HOS_INPUT_SCRIPT = $Buttons }
if ($FrameClock) { $env:HOS_INPUT_FRAME_CLOCK = "1" }
if ($ShotEvery -gt 0) {
    New-Item -ItemType Directory -Force "$out\shots" | Out-Null
    $env:RX_VK_SHOTS = "$out\shots"; $env:RX_VK_SHOT_EVERY = "$ShotEvery"
}
"run '$Name' -> $out"
"  renderer: $(if ($Renderer) { $Renderer } else { "the game's" })$(if ($Exe) { ", exe: $Exe" })"
"  caches:   $cacheNote"

# (romfs where it lies: --romfs. The log is stderr's file, so the game neither writes nor rotates hookshot_log.txt)
$gameArgs = @("--romfs", "`"$Game\romfs`"", "--user", "`"$run\user`"")
if ($Prepare) { $gameArgs = @("--prepare-shaders") }
$t0 = Get-Date
$exited = $false; $peak = 0; $p = $null
if ($DryRun) {
    "  dry run: would start $run\bin\hookshot.exe $($gameArgs -join ' ')"
    Push-Location $run
    Get-ChildItem -Recurse -File | ForEach-Object { "    {0,14:N0}  {1}" -f $_.Length, (Resolve-Path -Relative $_.FullName) }
    Pop-Location
} else {
$p = Start-Process "$run\bin\hookshot.exe" -ArgumentList $gameArgs -WorkingDirectory $run -PassThru -NoNewWindow `
    -RedirectStandardOutput "$out\stdout.txt" -RedirectStandardError "$out\log.txt"
$null = $p.Handle   # (held, so ExitCode is there after it exits)
while ($true) {
    if ($p.WaitForExit(2000)) { $exited = $true; break }
    try { $p.Refresh(); $mb = [int]($p.WorkingSet64 / 1MB) } catch { $mb = 0 }
    if ($mb -gt $peak) { $peak = $mb }
    if ($mb -gt $MaxGB * 1024) { "memory guard: $mb MB, stopped"; break }
    if ($Seconds -gt 0 -and ((Get-Date) - $t0).TotalSeconds -ge $Seconds) { break }
}
if (-not $exited) {
    # (closed as a player closes it first: a stop mid-prepare otherwise leaves the cache's "did not finish" marker, and the next
    # run from that cache leaves the driver's part out. Killed only when it has no window or does not close)
    $closed = $false
    try { if ($p.CloseMainWindow()) { $closed = $p.WaitForExit(20000) } } catch {}
    if (-not $closed) { Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue; $p.WaitForExit(10000) | Out-Null }
}
}
$secs = [int]((Get-Date) - $t0).TotalSeconds

# ---- the summary ----
$how = if ($DryRun) { "dry run" } elseif ($exited) { "exited with $($p.ExitCode)" } else { "stopped" }
$sum = @("run '$Name': $secs s, $how, peak working set $peak MB",
         "renderer: $(if ($Renderer) { $Renderer } else { "the game's" })", "caches: $cacheNote")
if (Test-Path "$out\log.txt") {
    $sum += Select-String -Path "$out\log.txt" -Pattern 'vk: program cache |vk: pipeline cache |vk: prepared |vk: predicted |vk: precompile: |vk: state list: |fps|crash|fault|exception|assert' |
        Select-Object -First 40 | ForEach-Object { $_.Line }
}
$sum | Set-Content "$out\summary.txt" -Encoding utf8
$sum

# ---- the run folder goes (hard links: the game's files stay; nothing in it is a link to a folder) ----
if ($Keep) { "kept: $run" }
else {
    $full = (Resolve-Path $run).Path
    if (-not $full.StartsWith((Resolve-Path $Runs).Path, [StringComparison]::OrdinalIgnoreCase) -or $full -eq $Game) {
        throw "not deleting ${full}: it is not a run folder"
    }
    Remove-Item $full -Recurse -Force
}
