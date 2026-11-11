# One game folder per line of Longshot, side by side: the same game files (romfs hard-linked, no second 24 GB), each with its
# own caches, saves and settings, so switching between them costs nothing (one folder for both would start the 6.7 GB shader
# cache again at every switch: each line's translator differs).
#
#   powershell -File scripts/build-game.ps1 -Channel release   Longshot\game: the published release, built by the installed
#                                                               Longshot Builder's own tools, as its Update does ("v1.0.0")
#   powershell -File scripts/build-game.ps1 -Channel dev       Longshot\game-dev: Longshot\worktrees\dev as it is checked
#                                                               out, with AMD FSR 4 ("pre-1.1.0 6353e9b")
# Options: -Longshot <folder> (the folder holding game\; found from this script's place when left out), -Jobs <n>,
# -Source <checkout> (dev: another checkout than Longshot\worktrees\dev), -Lines (the build tools' own lines, @@stage and
# the rest, as they come: for a program that shows them, scripts/dev-update.ps1 and through it the launcher).
#
# A dev build's folder says what it was built from (hookshot_build.txt: its name, the commit, how many files of the
# checkout were changed and not committed, the checkout), so the launcher can say whether it is the newest and bring it
# there: scripts/dev-update.ps1.
#
# Which build is running: the window's title ("Longshot - pre-1.1.0 6353e9b"), the start-up screen, the Insert menu's heading and
# the log's first line. A release says its tag ("v1.0.0"); anything else its branch and commit.
#
# dev, the first time: game-dev is made from game\'s own game files (hard links), its saves and settings copied from game\
# once (they are its own after that), and AMD's FSR 4 DLLs linked in from Longshot\fsr4 when they are there (AMD's, never in
# the repository: the readme's AMD FSR 4 section). A desktop shortcut "Longshot (dev)" starts it. The objects are kept in
# %LOCALAPPDATA%\LongshotDev, as scripts/build-vk.ps1's: the builder's own (LongshotBuilder) are not touched.
param([Parameter(Mandatory)][ValidateSet("release", "dev")][string]$Channel, [string]$Longshot = "", [int]$Jobs = 0,
      [string]$Source = "", [switch]$Lines)
$ErrorActionPreference = "Stop"

if (-not $Longshot) {   # (this script is in <Longshot>\source\scripts or <Longshot>\worktrees\<name>\scripts)
    $d = $PSScriptRoot
    while ($d -and -not (Test-Path (Join-Path $d "game\.hookshot_exefs"))) { $d = Split-Path -Parent $d }
    if (-not $d) { throw "No Longshot folder with game\ above ${PSScriptRoot}: pass -Longshot <folder>." }
    $Longshot = $d
}
$game = Join-Path $Longshot "game"
$logs = Join-Path $Longshot "logs"
New-Item -ItemType Directory -Force $logs | Out-Null

$v = Get-ChildItem "$env:LOCALAPPDATA\LongshotBuilder\versions" -Directory -ErrorAction SilentlyContinue |
     Where-Object { Test-Path "$($_.FullName)\HookshotBuilder\tools\nx2\make_game.py" } | Sort-Object LastWriteTime | Select-Object -Last 1
if (-not $v) { throw "No Longshot Builder installed (%LOCALAPPDATA%\LongshotBuilder\versions): install it first." }
$builder = Join-Path $v.FullName "HookshotBuilder"
$tools = Join-Path $builder "tools"
$python = Join-Path $tools "python\python.exe"

$out = if ($Channel -eq "release") { $game } else { Join-Path $Longshot "game-dev" }
$running = Get-CimInstance Win32_Process -Filter "Name='hookshot.exe'" | Where-Object { $_.ExecutablePath -like "$out\*" }
if ($running) { throw "The game in $out is running: close it first." }
foreach ($n in "HOOKSHOT_MINGW", "RX_CPM", "OOT_WORK", "HOOKSHOT_VERSION", "RX_VK_ONLY", "RX_RELEASE") { Remove-Item "Env:$n" -ErrorAction SilentlyContinue }
$jobArgs = if ($Jobs -gt 0) { @("--jobs", "$Jobs") } else { @() }
$log = Join-Path $logs "build-$Channel.log"

if ($Channel -eq "release") {
    # (the builder's tools, its compiler and its work folder, as its Update runs them: its compiled objects stay good for it)
    $env:HOOKSHOT_BUILDER_EXE = Join-Path $builder "LongshotBuilder.exe"
    $src = $tools
    $makeArgs = @("--update", $game)
    "release: $game, with the Longshot Builder $($v.Name)'s tools"
} else {
    $src = if ($Source) { $Source } else { Join-Path $Longshot "worktrees\dev" }
    if (-not (Test-Path "$src\nx2\make_game.py")) { throw "No dev checkout at $src (git worktree add ..\worktrees\dev dev, from source\)." }
    $src = (Resolve-Path $src).Path
    $branch = (git -C $src rev-parse --abbrev-ref HEAD).Trim()
    $commit = (git -C $src rev-parse HEAD).Trim()
    $changed = @(git -C $src status --porcelain --untracked-files=no).Count
    if ($branch -ne "dev") { Write-Warning "$src is on '$branch', not dev: the build says so in its name." }
    if ($changed) { Write-Warning "$src has $changed file(s) changed and not committed: the build is named '-dirty', and the launcher says so." }
    $env:HOOKSHOT_MINGW = Join-Path $tools "llvm-mingw"   # (a checkout has no llvm-mingw of its own)
    $env:RX_CPM = Join-Path $tools "cpm"
    # (the translated code and compiled objects are kept, ~4 GB: make_game deletes them after a build unless asked, and every
    # dev update then took 30 minutes. The release keeps the builder's own way)
    $env:HOOKSHOT_KEEP_BUILD_FILES = "1"
    $work = Join-Path $env:LOCALAPPDATA "LongshotDev"
    if (Test-Path "$out\image.bin") {
        $makeArgs = @("--update", $out, "--work", $work)
    } else {
        $lang = (Get-Content "$game\language.txt" -ErrorAction SilentlyContinue | Where-Object { $_ -and -not $_.StartsWith("#") } | Select-Object -First 1)
        $makeArgs = @("--romfs", "$game\romfs", "--exefs", "$game\.hookshot_exefs", "--out", $out, "--work", $work, "--fsr")
        if ($lang) { $makeArgs += @("--language", $lang.Trim()) }
    }
    "dev: $out, from $src ($branch $((git -C $src rev-parse --short=7 HEAD).Trim()))"
}

$t0 = Get-Date
Push-Location $src
$ErrorActionPreference = "Continue"   # (Windows PowerShell stops on a native program's stderr line under "Stop")
try {
    & $python -I -u -X utf8 nx2/make_game.py @makeArgs @jobArgs --log $log 2>&1 | ForEach-Object {
        $line = "$_"
        if ($Lines) { $line }
        elseif ($line -match '^@@(stage|error|done) (.*)$') { "  $($matches[1]): $($matches[2])" }
        elseif ($line -match '^version: ') { "  $line" }
    }
    $code = $LASTEXITCODE
} finally { Pop-Location; $ErrorActionPreference = "Stop" }
if ($code) { throw "The build stopped ($code): see $log" }

if ($Channel -eq "dev") {
    $bin = if (Test-Path "$out\bin\hookshot.exe") { "$out\bin" } else { $out }
    foreach ($n in "user", "hookshot_options.ini") {   # (once: the dev folder's own after that)
        if (-not (Test-Path "$out\$n") -and (Test-Path "$game\$n")) { Copy-Item "$game\$n" "$out\$n" -Recurse }
    }
    # (SDL2/SDL3 for controllers and wgpu_native for the fallback renderer are loaded at run time, and come with the builder's
    # tools, not a checkout: without SDL3.dll no controller works)
    foreach ($from in "$tools\third_party\sdl2\SDL2.dll", "$tools\third_party\sdl2\SDL3.dll", "$tools\wgpu-native\lib\wgpu_native.dll") {
        $to = Join-Path $bin (Split-Path -Leaf $from)
        if ((Test-Path $from) -and -not (Test-Path $to)) { Copy-Item $from $to }
    }
    foreach ($n in "amd_fidelityfx_loader_vk.dll", "amd_fidelityfx_upscaler_vk.dll") {
        $from = Join-Path $Longshot "fsr4\$n"
        if ((Test-Path $from) -and -not (Test-Path "$bin\$n")) { New-Item -ItemType HardLink -Path "$bin\$n" -Target $from | Out-Null }
    }
    $lnk = Join-Path ([Environment]::GetFolderPath("Desktop")) "Longshot (dev).lnk"
    $s = (New-Object -ComObject WScript.Shell).CreateShortcut($lnk)
    $s.TargetPath = if (Test-Path "$out\Longshot.exe") { "$out\Longshot.exe" } else { "$bin\hookshot.exe" }
    $s.WorkingDirectory = $out
    $s.Description = "Longshot, the dev branch's build"
    $s.Save()
    "  shortcut: $lnk"
}
$name = (Select-String -Path $log -Pattern '^version: (.*)$' | Select-Object -Last 1).Matches.Groups[1].Value
if ($Channel -eq "dev") {   # (the launcher lists it beside the releases: builder/HookshotBuilder/Launcher.cs)
    $short = if ($name -match '^(.*?) \(source') { $matches[1] } else { $name }
    # (what it was built from, for the launcher: builder/HookshotBuilder/DevBuild.cs)
    # (UTF-8 with no mark before the first line: Windows PowerShell's own UTF-8 writes one, and a folder may have any name)
    [IO.File]::WriteAllLines((Join-Path $out "hookshot_build.txt"),
        [string[]]@("name=$short", "channel=dev", "commit=$commit", "branch=$branch", "changed=$changed", "source=$src",
                    "built=$((Get-Date).ToUniversalTime().ToString('s'))Z"))
}
"$Channel built in {0:N0} min: $out, '$name'" -f ((Get-Date) - $t0).TotalMinutes
