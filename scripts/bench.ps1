# A benchmark run: scripts/test-run.ps1 with no window, on the same save and settings every time and the game's own warm
# caches (read-only), then scripts/benchlog.py's table of the run: the frame rate in play, the GPU's and the replay's time a
# frame, the busiest threads, the present intervals. -Against puts an earlier run beside it, with the change: an A/B of two
# renderers, two builds or two switches. Nothing in the game folder is written.
#
#   powershell -File scripts/bench.ps1 [-Name x] [-Game <game folder>] [-Renderer <hookshot_vk.dll>] [-Exe <hookshot.exe>]
#       [-Env "A=1","B=2"] [-Against <run folder or log>] [-Seconds 150] [-Timing] [-SampleAll] [-ShotEvery 0] [-Window]
#       [-Pinned <folder>] [-Pin] [-Cache game|cold|<folder>] [-Buttons "..."] [-Skip 4]
#
# -Game      the game folder (default: LONGSHOT_GAME, else game-dev beside game\ in the Longshot folder above this checkout,
#            else game\).
# -Pinned    the saves and settings every run starts from: a folder with user\ and hookshot_options.ini (default
#            <Longshot>\bench\pinned). The first run makes it from the game folder's own as they are then; -Pin makes it
#            again from the game folder's now. Without it a run would measure wherever the last play session stopped, at
#            whatever the Insert menu was left on. They are your saves: the folder is not in the repository.
# -Buttons   what gets from the title into the save: A every 4 s from 32 s to 56 s (the title appears at ~25 s on warm caches,
#            the save loads by ~50 s), then nothing: the game stands where the save was made, the same view every run.
# -Skip      in-play samples (120 presents each) left out at the start, while the area's textures and pipelines come in (4).
# -Timing    RX_VK_PASS_TIMING=1: the GPU's time by pass and program. It makes the GPU time itself longer, so compare a
#            -Timing run only with another one.
# -SampleAll test-run.ps1's: every thread's stack every 2 ms, for scripts/crit.py and vkprof.py. It slows the game: not for
#            a run whose frame rate is the result.
# -Cache     test-run.ps1's (default game: the game's caches, read-only). A renderer whose shader translator differs from
#            the game's cannot use them: give it a folder of its own, kept between runs.
# -Window    a window instead of none (it never takes the keyboard).
# The busiest threads are always logged (HOS_PROFILE=5: a few thread-time reads every 5 s).
param([string]$Name = "bench", [string]$Game = $env:LONGSHOT_GAME, [string]$Renderer = "", [string]$Exe = "", [string[]]$Env = @(),
      [string]$Against = "", [int]$Seconds = 150, [switch]$Timing, [switch]$SampleAll, [int]$ShotEvery = 0, [switch]$Window,
      [string]$Pinned = "", [switch]$Pin, [string]$Cache = "game", [string]$Buttons = "32:A,36:A,40:A,44:A,48:A,52:A,56:A",
      [int]$Skip = 4, [int]$MaxGB = 12)
$ErrorActionPreference = "Stop"

$longshot = $PSScriptRoot   # (this script is in <Longshot>\source\scripts or <Longshot>\worktrees\<name>\scripts)
while ($longshot -and -not (Test-Path (Join-Path $longshot "game\.hookshot_exefs"))) { $longshot = Split-Path -Parent $longshot }
if (-not $Game) {
    if (-not $longshot) { throw "No game folder: pass -Game <folder> or set LONGSHOT_GAME." }
    $Game = if (Test-Path "$longshot\game-dev\image.bin") { "$longshot\game-dev" } else { "$longshot\game" }
}
$Game = (Resolve-Path $Game).Path.TrimEnd('\')
if (-not $Pinned) { $Pinned = Join-Path (Split-Path -Parent $Game) "bench\pinned" }
if ($Against -and -not (Test-Path $Against)) { throw "-Against ${Against}: no such run folder or log." }

$python = (Get-Command python -ErrorAction SilentlyContinue).Source
if (-not $python) {   # (the installed builder's, as scripts/build-vk.ps1 finds its tools)
    $python = Get-ChildItem "$env:LOCALAPPDATA\LongshotBuilder\versions\*\HookshotBuilder\tools\python\python.exe" -ErrorAction SilentlyContinue |
              Sort-Object LastWriteTime | Select-Object -Last 1 -ExpandProperty FullName
}
if (-not $python) { throw "No python for scripts/benchlog.py: on the PATH, or an installed Longshot Builder's." }

# ---- the pinned saves and settings ----
if ($Pin -or -not (Test-Path "$Pinned\hookshot_options.ini")) {
    foreach ($need in "user", "hookshot_options.ini") {
        if (-not (Test-Path "$Game\$need")) { throw "$Game has no $need to pin: play it once (and save), or pass -Pinned <folder>." }
    }
    New-Item -ItemType Directory -Force $Pinned | Out-Null
    if (Test-Path "$Pinned\user") { Remove-Item "$Pinned\user" -Recurse -Force }
    Copy-Item "$Game\user" "$Pinned\user" -Recurse
    Copy-Item "$Game\hookshot_options.ini" "$Pinned\hookshot_options.ini" -Force
    "pinned: $Pinned (the saves and settings of $Game as they are now)"
}

# ---- the run ----
$switches = @("HOS_PROFILE=5")
if ($Timing) { $switches += "RX_VK_PASS_TIMING=1" }
$run = @{ Game = $Game; Name = $Name; Cache = $Cache; Seconds = $Seconds; Buttons = $Buttons; Env = $switches + $Env
          User = "$Pinned\user"; Options = "$Pinned\hookshot_options.ini"; MaxGB = $MaxGB; Mute = $true }
if ($Renderer) { $run.Renderer = $Renderer }
if ($Exe) { $run.Exe = $Exe }
if (-not $Window) { $run.Headless = $true }
if ($ShotEvery -gt 0) { $run.ShotEvery = $ShotEvery }
if ($SampleAll) { $run.SampleAll = $true }
& "$PSScriptRoot\test-run.ps1" @run | Tee-Object -Variable said
$out = ($said | ForEach-Object { if ("$_" -match "^run '.*' -> (.+)$") { $matches[1] } } | Select-Object -First 1)
if (-not $out -or -not (Test-Path "$out\log.txt")) { throw "The run left no log." }

# ---- its numbers ----
$benchlog = Join-Path $PSScriptRoot "benchlog.py"
""
[string[]]$logs = @($out)                        # (an array even of one: a string splatted goes over a letter at a time)
if ($Against) { $logs = @($Against, $out) }      # (the earlier run is A, this one B)
$table = & $python -I $benchlog @logs --skip $Skip
if ($LASTEXITCODE) { throw "scripts/benchlog.py failed ($LASTEXITCODE)" }
$table
$table | Set-Content "$out\bench.txt" -Encoding utf8
""
"bench: $out\bench.txt (compare a later run with -Against `"$out`")"
