# The Longshot Builder built by scripts/dev-update.ps1 (the launcher) put in the place of the installed one:
#
#   powershell -File scripts/install-launcher.ps1
#
# The files it replaces are kept (%LOCALAPPDATA%\LongshotBuilder\launcher-before, the last time's only). The build tools
# beside the builder are not touched. A builder that is running cannot be replaced: -WaitFor <process id> waits for that
# one to close first (the launcher passes its own, as it closes after an update), and -Relaunch <game folder> opens the
# new one as that game's launcher afterwards.
#
# Options: -From <folder> (default: this checkout's builder\HookshotBuilder\bin\ready), -To <folder> (default: the
# installed builder in use).
param([string]$From = "", [string]$To = "", [int]$WaitFor = 0, [string]$Relaunch = "")
$ErrorActionPreference = "Stop"
if (-not $From) { $From = Join-Path $PSScriptRoot "..\builder\HookshotBuilder\bin\ready" }
if (-not $To) {
    $current = "$env:LOCALAPPDATA\LongshotBuilder\versions\current.txt"
    if (-not (Test-Path $current)) { throw "No Longshot Builder is installed here (%LOCALAPPDATA%\LongshotBuilder\versions)." }
    $To = "$env:LOCALAPPDATA\LongshotBuilder\versions\$((Get-Content $current -TotalCount 1).Trim())\HookshotBuilder"
}
$names = "LongshotBuilder.dll", "LongshotBuilder.exe", "LongshotBuilder.deps.json", "LongshotBuilder.runtimeconfig.json", "LongshotBuilder-Setup.exe"
foreach ($n in "LongshotBuilder.dll", "LongshotBuilder.exe", "LongshotBuilder-Setup.exe") {
    if (-not (Test-Path (Join-Path $From $n))) { throw "$From has no ${n}: build it first (scripts/dev-update.ps1)." }
}
if (-not (Test-Path (Join-Path $To "LongshotBuilder.exe"))) { throw "$To holds no Longshot Builder." }

if ($WaitFor) {   # (the launcher that started this, closing)
    $p = Get-Process -Id $WaitFor -ErrorAction SilentlyContinue
    if ($p -and -not $p.WaitForExit(60000)) { throw "The Longshot Builder that is open did not close: the new one is still in $From." }
}
$open = Get-Process LongshotBuilder -ErrorAction SilentlyContinue | Where-Object { $_.Path -and (Split-Path $_.Path) -eq (Resolve-Path $To).Path }
if ($open) { throw "A Longshot Builder is open from $To. Close it, and run this again." }

$before = "$env:LOCALAPPDATA\LongshotBuilder\launcher-before"
if (Test-Path $before) { Remove-Item $before -Recurse -Force }
New-Item -ItemType Directory -Force $before | Out-Null
foreach ($n in $names) { if (Test-Path (Join-Path $To $n)) { Copy-Item (Join-Path $To $n) $before } }
try {
    foreach ($n in $names) { if (Test-Path (Join-Path $From $n)) { Copy-Item (Join-Path $From $n) (Join-Path $To $n) -Force } }
} catch {
    # (half of it copied is a builder that does not start: the one before, back whole)
    foreach ($n in $names) { if (Test-Path (Join-Path $before $n)) { Copy-Item (Join-Path $before $n) (Join-Path $To $n) -Force } }
    throw "The new launcher could not be put in place ($($_.Exception.Message)): the one before is back."
}
$version = (Get-Item (Join-Path $To "LongshotBuilder.dll")).VersionInfo.ProductVersion
"the launcher in $To is now $version (the one before: $before)"
if ($Relaunch) { Start-Process (Join-Path $To "LongshotBuilder.exe") -ArgumentList "--launch", "`"$Relaunch`"" }
