# Every screen of the launcher on one page, drawn again each time a file of it is saved (design work).
#
#   powershell -File scripts/launcher/sheet.ps1          # build/launcher/sheet/index.html opens, and redraws on every save
#   powershell -File scripts/launcher/sheet.ps1 -Once    # draw once and exit
#
# Each state is the real window drawn off-screen (HOOKSHOT_UI_SHOT, launcher/Hookshot/Shot.cs) from a Debug build of its own
# (build/launcher/sheet/artifacts: it never mixes with `dotnet watch` or a publish), in scratch folders: no builder of this PC,
# no game, no GitHub (HOOKSHOT_LATEST_TAG makes the releases up) and nothing run. A build, an update and their stopped states
# are made up (HOOKSHOT_UI_PREVIEW building=, stopped, updating=, updatestopped). The page reloads its pictures by itself;
# a picture of the Paper design named like a state (build/launcher/sheet/paper/<state>.png) shows beside it. The pictures are
# drawn one at a time at below normal priority, so a benchmark running in another thread is not disturbed.
param([switch]$Once)
$ErrorActionPreference = 'Stop'
$Root = Resolve-Path (Join-Path $PSScriptRoot '..\..')
Set-Location $Root
# (a dotnet with an SDK: the one on PATH may be a runtime alone; the per-user SDK, %LOCALAPPDATA%\Microsoft\dotnet, then)
$userDotnet = Join-Path $env:LOCALAPPDATA 'Microsoft\dotnet'
if ((-not (Get-Command dotnet -ErrorAction SilentlyContinue) -or -not (& dotnet --list-sdks 2>$null)) -and (Test-Path "$userDotnet\dotnet.exe")) {
    $env:DOTNET_ROOT = $userDotnet
    $env:PATH = "$userDotnet;$env:PATH"
}
$Sheet = Join-Path $Root 'build\launcher\sheet'
$Shots = Join-Path $Sheet 'shots'
$Scratch = Join-Path $Sheet 'scratch'
New-Item -ItemType Directory -Force $Shots, (Join-Path $Sheet 'paper') | Out-Null

# The states: name, what it shows, HOOKSHOT_UI_PREVIEW, the game folder ('game': a built one at v2.1.0; '': none), the releases
$States = @(
    @('main',            'Play, up to date',                  '',                      'game', 'v2.1.0,v2.0.14'),
    @('main-update',     'Play, a Hookshot release out',      '',                      'game', 'v2.2.0,v2.1.0,v2.0.14'),
    @('launcher-update', "Play, the launcher's own update",   'launcherupdate',        'game', 'v2.1.0,v2.0.14'),
    @('updating',        'Play, updating',                    'updating=0.6',          'game', 'v2.2.0,v2.1.0,v2.0.14'),
    @('update-stopped',  'Play, update stopped',              'updatestopped',         'game', 'v2.2.0,v2.1.0,v2.0.14'),
    @('first',           'No game yet (opens on Build)',      '',                      '',     'v2.1.0,v2.0.14'),
    @('building',        'Build: building',                   'building=0.45',         '',     'v2.1.0,v2.0.14'),
    @('stopped',         'Build: stopped',                    'stopped',               '',     'v2.1.0,v2.0.14'),
    @('releases',        'Releases (the version under Play)', 'tab=releases',          'game', 'v2.2.0,v2.1.0,v2.0.14,v2.0.13,v2.0.12,v2.0.11,v2.0.10'),
    @('settings',        'Settings',                          'tab=settings',          'game', 'v2.1.0,v2.0.14')
)

function New-Scratch {
    if (Test-Path $Scratch) { Remove-Item -Recurse -Force $Scratch }
    $game = Join-Path $Scratch 'Games\Hookshot'
    New-Item -ItemType Directory -Force (Join-Path $game 'bin'), (Join-Path $game 'romfs'), (Join-Path $game '.hookshot_exefs'),
        (Join-Path $Scratch 'builders'), (Join-Path $Scratch 'home') | Out-Null
    [IO.File]::WriteAllText((Join-Path $game 'bin\hookshot.exe'), 'MZ')
    [IO.File]::WriteAllText((Join-Path $game 'hookshot_version.txt'), "version=v2.1.0`n")
    return $game
}

function Write-Page {
    $cards = foreach ($s in $States) {
        $paper = if (Test-Path (Join-Path $Sheet "paper\$($s[0]).png")) { "<figure><img data-src='paper/$($s[0]).png'><figcaption>Paper</figcaption></figure>" } else { '' }
        "<section><h2>$($s[1])</h2><div class='pair'><figure><img data-src='shots/$($s[0]).png'><figcaption>Launcher</figcaption></figure>$paper</div></section>"
    }
    $html = @"
<!doctype html><html><head><meta charset="utf-8"><title>Launcher sheet</title>
<style>
:root{color-scheme:dark}body{margin:0;padding:20px 24px;background:#0b0b0b;color:#ddd;font:14px system-ui,sans-serif}
header{display:flex;gap:16px;align-items:baseline;margin-bottom:12px}h1{font-size:18px;margin:0}#when{color:#999}
#grid{display:grid;grid-template-columns:repeat(auto-fill,minmax(480px,1fr));gap:20px}
h2{font-size:13px;font-weight:600;margin:0 0 6px;color:#bbb}.pair{display:flex;gap:8px}
figure{margin:0;flex:1}figcaption{font-size:11px;color:#777;margin-top:3px}img{width:100%;display:block;border:1px solid #222;background:#121212}
img.new{outline:2px solid #D2823F}
</style></head><body><header><h1>Launcher sheet</h1><span id="when"></span></header><div id="grid">
$($cards -join "`n")
</div><script src="stamp.js"></script><script>
// (file:// pages cannot fetch: stamp.js is loaded again every second, and the pictures reload when its stamp changes)
let seen = null;
function load(){ document.querySelectorAll('img[data-src]').forEach(i => { const n = new Image(); n.onload = () => { i.src = n.src; i.classList.add('new'); setTimeout(()=>i.classList.remove('new'),1200) }; n.src = i.dataset.src + '?' + Date.now(); }) }
function poll(){ const s = document.createElement('script'); s.src = 'stamp.js?' + Date.now(); s.onload = () => { if (window.STAMP !== seen) { seen = window.STAMP; document.getElementById('when').textContent = window.WHEN || ''; load() } s.remove() }; document.body.appendChild(s) }
setInterval(poll, 1000); poll();
</script></body></html>
"@
    [IO.File]::WriteAllText((Join-Path $Sheet 'index.html'), $html)
}

function Draw {
    $watch = [Diagnostics.Stopwatch]::StartNew()
    # (one project at a time, below normal priority: benchmarks may be running in other threads)
    $proc = [Diagnostics.Process]::GetCurrentProcess(); try { $proc.PriorityClass = 'BelowNormal' } catch {}
    $built = & dotnet build launcher/Hookshot/Hookshot.csproj -c Debug -p:NuGetAudit=false -nologo -v:q -m:1 `
        --artifacts-path (Join-Path $Sheet 'artifacts') 2>&1
    if ($LASTEXITCODE -ne 0) {
        $why = ($built | Select-String 'error' | Select-Object -First 3) -join "`n"
        [IO.File]::WriteAllText((Join-Path $Sheet 'stamp.js'), "window.STAMP='failed $(Get-Date -Format o)';window.WHEN=" + (ConvertTo-Json "Build failed: $why"))
        Write-Host "build failed:`n$why" -ForegroundColor Red
        return
    }
    $exe = Get-ChildItem (Join-Path $Sheet 'artifacts\bin') -Recurse -Filter HookshotLauncher.exe | Select-Object -First 1
    $game = New-Scratch
    $procs = foreach ($s in $States) {
        $si = New-Object Diagnostics.ProcessStartInfo $exe.FullName
        if ($s[3]) { $si.Arguments = "--launch `"$game`"" }
        $si.UseShellExecute = $false
        $si.CreateNoWindow = $true
        $png = Join-Path $Shots "$($s[0]).new.png"
        $si.EnvironmentVariables['HOOKSHOT_UI_SHOT'] = $png
        $si.EnvironmentVariables['HOOKSHOT_UI_PREVIEW'] = $s[2]
        $si.EnvironmentVariables['HOOKSHOT_LATEST_TAG'] = $s[4]
        $si.EnvironmentVariables['HOOKSHOT_SETUP_BASE'] = Join-Path $Scratch "builders\$($s[0])"
        $si.EnvironmentVariables['HOOKSHOT_LAUNCHER_HOME'] = Join-Path $Scratch "home\$($s[0])"
        $si.EnvironmentVariables['HOOKSHOT_NO_UPDATE_CHECK'] = '1'
        $si.EnvironmentVariables['HotAvalonia'] = 'disable'
        $p = [Diagnostics.Process]::Start($si)
        try { $p.PriorityClass = 'BelowNormal' } catch {}
        $null = $p.WaitForExit(60000)
        [pscustomobject]@{ Name = $s[0]; Png = $png; P = $p }
    }
    $bad = @()
    foreach ($p in $procs) {
        if (-not $p.P.HasExited) { $p.P.Kill(); $bad += $p.Name; continue }
        if ($p.P.ExitCode -ne 0 -or -not (Test-Path $p.Png)) { $bad += $p.Name; continue }
        Move-Item -Force $p.Png (Join-Path $Shots "$($p.Name).png")
    }
    $when = "drawn $(Get-Date -Format 'HH:mm:ss') in $([int]$watch.Elapsed.TotalSeconds) s" + $(if ($bad) { " (not drawn: $($bad -join ', '))" } else { '' })
    [IO.File]::WriteAllText((Join-Path $Sheet 'stamp.js'), "window.STAMP='$(Get-Date -Format o)';window.WHEN=" + (ConvertTo-Json $when))
    Write-Host $when
}

Write-Page
Draw
if ($Once) { return }
Start-Process (Join-Path $Sheet 'index.html')

# Saved files: the launcher's own, and the WPF launcher's rules it compiles in. A burst of saves draws once
$fsw = New-Object IO.FileSystemWatcher (Join-Path $Root 'launcher\Hookshot')
$fsw.IncludeSubdirectories = $true
$fsw2 = New-Object IO.FileSystemWatcher (Join-Path $Root 'builder\HookshotBuilder')
foreach ($w in $fsw, $fsw2) { $w.NotifyFilter = 'LastWrite, FileName'; $w.EnableRaisingEvents = $true }
Write-Host "watching launcher/Hookshot: save a file to draw again (Ctrl+C to stop)"
while ($true) {
    $changed = $null
    foreach ($w in $fsw, $fsw2) {
        $r = $w.WaitForChanged('Changed, Created, Renamed', 500)
        if (-not $r.TimedOut -and $r.Name -match '\.(axaml|cs|csproj|props|png|ttf)$' -and $r.Name -notmatch '(^|\\)(bin|obj)\\') { $changed = $r.Name }
    }
    if ($changed) {
        Start-Sleep -Milliseconds 400   # (the rest of a save that writes several files)
        Write-Host "saved: $changed"
        Draw
    }
}
