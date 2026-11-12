# The launcher in a real window that follows your edits (design work):
#
#   powershell -File scripts/launcher/live.ps1                       # main screen, a game built, an update out
#   powershell -File scripts/launcher/live.ps1 -State building=0.6   # a made-up state (see below)
#   powershell -File scripts/launcher/live.ps1 -NoGame               # no game yet: opens on Build
#
# A .axaml file saved shows in the window at once (HotAvalonia, Debug builds only; Alt+F5 reloads the whole window). A .cs
# file saved is applied by `dotnet watch` where .NET can, else the window restarts by itself in a few seconds.
#
# -State is HOOKSHOT_UI_PREVIEW (launcher/Hookshot/MainWindow.axaml.cs Preview): menu, tab=build|releases|settings, older,
# pre, and a made-up job, nothing run: building[=0..1], stopped, updating[=0..1], updatestopped. Comma-separated.
# Everything is in scratch folders under build/launcher/live: no builder of this PC, no game, no GitHub (-Releases makes the
# releases up). Pressing Build or Update there tries for real (downloads a builder into the scratch folder): leave them be.
param([string]$State = '', [switch]$NoGame, [string]$Releases = 'v2.2.0,v2.1.0,v2.0.14')
$ErrorActionPreference = 'Stop'
$Root = Resolve-Path (Join-Path $PSScriptRoot '..\..')
Set-Location $Root
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    $env:DOTNET_ROOT = Join-Path $env:LOCALAPPDATA 'Microsoft\dotnet'
    $env:PATH = "$env:DOTNET_ROOT;$env:PATH"
}
$Live = Join-Path $Root 'build\launcher\live'
$game = Join-Path $Live 'Games\Hookshot'
if (-not (Test-Path (Join-Path $game 'bin\hookshot.exe'))) {
    New-Item -ItemType Directory -Force (Join-Path $game 'bin'), (Join-Path $game 'romfs'), (Join-Path $game '.hookshot_exefs') | Out-Null
    [IO.File]::WriteAllText((Join-Path $game 'bin\hookshot.exe'), 'MZ')
    [IO.File]::WriteAllText((Join-Path $game 'hookshot_version.txt'), "version=v2.1.0`n")
}
$env:HOOKSHOT_UI_PREVIEW = $State
$env:HOOKSHOT_LATEST_TAG = $Releases
$env:HOOKSHOT_SETUP_BASE = Join-Path $Live 'builders'
$env:HOOKSHOT_LAUNCHER_HOME = Join-Path $Live 'home'
$env:HOOKSHOT_NO_UPDATE_CHECK = '1'
$env:DOTNET_WATCH_RESTART_ON_RUDE_EDIT = '1'
$env:DOTNET_WATCH_SUPPRESS_LAUNCH_BROWSER = '1'
$launch = if ($NoGame) { @('--launch', (Join-Path $Live 'no game')) } else { @('--launch', $game) }
& dotnet watch --project launcher/Hookshot/Hookshot.csproj --non-interactive run -p:NuGetAudit=false -- @launch
