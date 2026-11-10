# The renderer alone (hookshot_vk.dll), from this checkout, for scripts/test-run.ps1: nx2/build.py with RX_VK_ONLY=1 and
# RX_RELEASE=1 (as the builder's), the builder's own compiler and packages, in a work folder of its own.
#   powershell -File scripts/build-vk.ps1 [-Work <folder>] [-Jobs 12] [-Tools <builder tools folder>]
# Its own work folder (default %LOCALAPPDATA%\LongshotDev): a build from a checkout in the builder's (LongshotBuilder\sa)
# changed the compiler's path as the builder writes it, and the builder's next build then threw away every compiled object
# (30 minutes). No lift is needed. The first build compiles the renderer and Eden's recompiler (~600 files); after that
# only what changed. The DLL is <work>\sa\build\hookshot_vk.dll.
param([string]$Work = "$env:LOCALAPPDATA\LongshotDev", [int]$Jobs = [Environment]::ProcessorCount, [string]$Tools = "")
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
if (-not $Tools) {   # (the newest installed builder's)
    $v = Get-ChildItem "$env:LOCALAPPDATA\LongshotBuilder\versions" -Directory -ErrorAction SilentlyContinue |
         Where-Object { Test-Path "$($_.FullName)\HookshotBuilder\tools\llvm-mingw" } | Sort-Object LastWriteTime | Select-Object -Last 1
    if (-not $v) { throw "No Longshot Builder found in $env:LOCALAPPDATA\LongshotBuilder\versions: pass -Tools <its tools folder>." }
    $Tools = "$($v.FullName)\HookshotBuilder\tools"
}
foreach ($need in "llvm-mingw", "cpm", "python\python.exe") {
    if (-not (Test-Path "$Tools\$need")) { throw "$Tools has no $need." }
}
New-Item -ItemType Directory -Force $Work | Out-Null
$env:OOT_WORK = $Work
$env:HOOKSHOT_MINGW = "$Tools\llvm-mingw"
$env:RX_CPM = "$Tools\cpm"
$env:RX_VK_ONLY = "1"
$env:RX_RELEASE = "1"
Push-Location $root
try {
    & "$Tools\python\python.exe" -I -u nx2/build.py --standalone --jobs $Jobs
    if ($LASTEXITCODE) { throw "build failed ($LASTEXITCODE)" }
} finally { Pop-Location }
$dll = "$Work\sa\build\hookshot_vk.dll"
if (-not (Test-Path $dll)) { throw "no $dll after the build" }
"renderer: $dll"
