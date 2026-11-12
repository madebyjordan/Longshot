# Made-up releases on this PC, so the launcher's buttons can be pressed for real and nothing real happens (design work).
# Dot-sourced by scripts/launcher/live.ps1; on its own it makes the world and prints what it set:
#
#   . scripts/launcher/dummy.ps1; $world = New-DummyWorld -Folder build\launcher\live -Port 8793
#
# The world (all under -Folder, made afresh each time; tests/builder_launcher/rig/rig.py lays it out):
#   - releases v2.2.0, v2.2.0-rc.1, a nightly (v2.2.0-3-g1a2b3c4) and v2.0.14, served by a web server on 127.0.0.1 (python -m http.server), each with its setup
#     list and parts, as GitHub would hand them out; v2.1.0 is the builder already installed and in use;
#   - a game folder holding v2.1.0, whose game is a stand-in that says "Play works" (scripts/launcher/dummygame.c);
#   - a game file (game.nup, empty) already chosen, for Build;
#   - the builders' python.exe is a stand-in for make_game.py (tests/builder_launcher/rig/fakepy.c): a build or an update
#     goes through its steps in a few seconds and writes the folder's version, without reading anything.
# Pressing Update installs v2.2.0's builder from the local server with the real setup (builder/setup/setup.c, --engine),
# then "builds" the folder with it; Build makes a new game folder; Play shows the stand-in. HOOKSHOT_DUMMY_RELEASES (Builders.cs)
# keeps every download on the local server: a first builder from GitHub and AMD's FSR runtime are refused.
# -Fail: every build stops half way with a made-up error (to see the stopped states). -StepMs: the wait between the
# stand-in's lines (default 400: a build takes about 10 seconds).

function Find-Mingw {
    $found = Get-ChildItem "$env:LOCALAPPDATA\HookshotBuilder\versions\*\HookshotBuilder\tools\llvm-mingw\bin" -Directory -ErrorAction SilentlyContinue |
        Select-Object -First 1 -ExpandProperty FullName
    if (-not $found -and $env:LLVM_MINGW) { $found = "$env:LLVM_MINGW\bin" }
    if (-not $found -or -not (Test-Path "$found\x86_64-w64-mingw32-clang.exe")) {
        throw 'llvm-mingw was not found (an installed Hookshot Builder has it): set LLVM_MINGW to its folder'
    }
    $found
}

function New-DummyWorld([string]$Folder, [int]$Port = 8793, [switch]$Fail, [int]$StepMs = 400) {
    $repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
    $tools = Join-Path $Folder 'tools'
    $rig = Join-Path $Folder 'rig'
    New-Item -ItemType Directory -Force $tools | Out-Null
    # (the three programs, compiled again only when their source is newer)
    $mingw = $null
    foreach ($p in @(
            @('HookshotBuilder-Setup.exe', "$repo\builder\setup\setup.c", 'setup'),
            @('fakepy.exe', "$repo\tests\builder_launcher\rig\fakepy.c", 'console'),
            @('hookshot.exe', "$repo\scripts\launcher\dummygame.c", 'window'))) {
        $out = Join-Path $tools $p[0]
        if ((Test-Path $out) -and (Get-Item $out).LastWriteTimeUtc -gt (Get-Item $p[1]).LastWriteTimeUtc) { continue }
        if (-not $mingw) { $mingw = Find-Mingw }
        $clang = "$mingw\x86_64-w64-mingw32-clang.exe"
        if ($p[2] -eq 'setup') {
            & "$mingw\x86_64-w64-mingw32-windres.exe" "$repo\builder\setup\setup.rc" -I "$repo\builder\setup" -O coff -o "$tools\setup_rc.o"
            & $clang -O2 -mwindows -municode $p[1] "$tools\setup_rc.o" -lwinhttp -lbcrypt -lcomctl32 -lshell32 -lole32 -o $out
        } elseif ($p[2] -eq 'window') { & $clang -O1 -mwindows -municode $p[1] -o $out }
        else { & $clang -O1 $p[1] -o $out }
        if ($LASTEXITCODE -ne 0 -or -not (Test-Path $out)) { throw "$($p[0]) did not compile" }
    }
    # (the world, afresh: rig.py wants fakepy.exe beside it)
    if (Test-Path $rig) { Remove-Item -Recurse -Force $rig }
    New-Item -ItemType Directory -Force $rig | Out-Null
    Copy-Item "$tools\fakepy.exe" "$rig\fakepy.exe"
    & python -I "$repo\tests\builder_launcher\rig\rig.py" $rig $Port | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'rig.py failed' }
    $base = Join-Path $rig 'base'
    $game = Join-Path $rig 'game'
    # (the setup the launcher installs releases with: in the builder in use, as a release has it)
    Copy-Item "$tools\HookshotBuilder-Setup.exe" "$base\versions\v2.1.0\HookshotBuilder\HookshotBuilder-Setup.exe"
    Copy-Item "$tools\hookshot.exe" "$game\bin\hookshot.exe" -Force
    # (where Build puts a new game: its parent made, as the stand-in makes only the last folder)
    New-Item -ItemType Directory -Force (Join-Path $rig 'Games') | Out-Null
    $nup = Join-Path $rig 'game.nup'
    [IO.File]::WriteAllBytes($nup, [byte[]]@())
    $settings = @{ Out = $game; Nup = $nup; PrepareLater = $false; Track = 'release' } | ConvertTo-Json
    [IO.File]::WriteAllText((Join-Path $base 'builder-settings.json'), $settings)
    $server = Start-Process python -ArgumentList '-I', '-m', 'http.server', "$Port", '--bind', '127.0.0.1', '--directory', "`"$rig\www`"" `
        -WindowStyle Hidden -PassThru
    [pscustomobject]@{
        Game = $game
        NewGame = Join-Path $rig 'Games\Hookshot'
        Server = $server
        Env = [ordered]@{
            HOOKSHOT_SETUP_BASE = $base
            HOOKSHOT_LAUNCHER_HOME = Join-Path $rig 'home'
            HOOKSHOT_DUMMY_RELEASES = "http://127.0.0.1:$Port/"
            # (any setup started without the launcher's own list still asks the local server, never GitHub)
            HOOKSHOT_SETUP_LIST = "http://127.0.0.1:$Port/list-v2.2.0.txt"
            HOOKSHOT_LATEST_TAG = 'v2.2.0,v2.2.0-rc.1,v2.2.0-3-g1a2b3c4,v2.1.0,v2.0.14'
            HOOKSHOT_NO_UPDATE_CHECK = '1'
            HOOKSHOT_KEEP_BUILD_FILES = '1'
            FAKE_STEP_MS = "$StepMs"
            FAKE_FAIL = $(if ($Fail) { '1' } else { '' })
            FAKE_GAME_EXE = "$tools\hookshot.exe"
        }
    }
}
