# The launcher in a real window that follows your edits (design work):
#
#   powershell -File scripts/launcher/live.ps1                       # main screen, a game built, an update out
#   powershell -File scripts/launcher/live.ps1 -State building=0.6   # a made-up state (see below)
#   powershell -File scripts/launcher/live.ps1 -NoGame               # no game yet: opens on Build
#   powershell -File scripts/launcher/live.ps1 -Fail                 # every build stops half way (the stopped states)
#
# A .axaml file saved shows in the window at once (HotAvalonia, Debug builds only; Alt+F5 reloads the whole window). A .cs
# file saved is applied by `dotnet watch` where .NET can, else the window restarts by itself.
#
# Every button can be pressed: the window runs against made-up releases on this PC (scripts/launcher/dummy.ps1), made afresh
# at each start under build/launcher/live. Update installs v2.2.0's stand-in builder from a local server and "builds" the
# game folder in about 10 seconds; Build makes a new game folder from an empty game file; Play shows a stand-in that says
# "Play works". Nothing of this PC's own (builders, games, settings) is read or written, and nothing goes to GitHub.
#
# -State is HOOKSHOT_UI_PREVIEW (launcher/Hookshot/MainWindow.axaml.cs Preview): menu, tab=build|releases|settings, older,
# pre, and a made-up job shown without running: building[=0..1], stopped, updating[=0..1], updatestopped. Comma-separated.
param([string]$State = '', [switch]$NoGame, [switch]$Fail, [int]$StepMs = 400, [int]$Port = 8793)
$ErrorActionPreference = 'Stop'
$Root = Resolve-Path (Join-Path $PSScriptRoot '..\..')
Set-Location $Root
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    $env:DOTNET_ROOT = Join-Path $env:LOCALAPPDATA 'Microsoft\dotnet'
    $env:PATH = "$env:DOTNET_ROOT;$env:PATH"
}
. (Join-Path $PSScriptRoot 'dummy.ps1')
$world = New-DummyWorld -Folder (Join-Path $Root 'build\launcher\live') -Port $Port -Fail:$Fail -StepMs $StepMs
try {
    foreach ($k in $world.Env.Keys) { [Environment]::SetEnvironmentVariable($k, $world.Env[$k]) }
    $env:HOOKSHOT_UI_PREVIEW = $State
    $env:DOTNET_WATCH_RESTART_ON_RUDE_EDIT = '1'
    $env:DOTNET_WATCH_SUPPRESS_LAUNCH_BROWSER = '1'
    $launch = if ($NoGame) { $world.NewGame } else { $world.Game }
    "made-up releases: v2.2.0 (latest), v2.2.0-rc.1, v2.1.0 (installed), v2.0.14. Game folder: $launch"
    & dotnet watch --project launcher/Hookshot/Hookshot.csproj --non-interactive run -p:NuGetAudit=false -- --launch $launch
} finally {
    Stop-Process -Id $world.Server.Id -Force -ErrorAction SilentlyContinue
}
