# The Hookshot launcher for Windows (launcher/Hookshot), as players download it: published for win-x64 as
# launcher/Hookshot/Publish.props says (NativeAOT), checked by drawing its window with no screen, and zipped:
#
#   scripts/launcher/publish.ps1             -> build/launcher/Hookshot-<version>-Windows-x64-portable.zip
#   scripts/launcher/publish.ps1 -Jit        the JIT build instead (trimmed, ReadyToRun, one file): no C++ tools needed
#   scripts/launcher/publish.ps1 -NoSmoke    without the check
#
# Beside Hookshot.exe goes HookshotBuilder-Setup.exe (builder/setup/setup.c, built here as .github/workflows/windows-builder.yml
# builds it): the launcher installs each release's builder with its engine mode, which the setups of the releases before it
# do not have. It is built with llvm-mingw: LLVM_MINGW's, else the one an installed Hookshot Builder has in its tools.
#
# NativeAOT needs Visual Studio's C++ tools ("Desktop development with C++"), as the GitHub runners have. The version is
# HOOKSHOT_VERSION's, or git describe's as the release workflows name their files (v2.1.0, v2.1.0-rc.1-3-g1a2b3c4).
# The check (HOOKSHOT_UI_SHOT, launcher/Hookshot/Shot.cs) runs the zipped files themselves: the window drawn by Skia with
# Avalonia.Headless to build/launcher/smoke-win-x64.png, 960x640, with GitHub's answer made up (HOOKSHOT_LATEST_TAG) and
# the builders' folder a temporary one (HOOKSHOT_SETUP_BASE): no network, no window, nothing of this PC's installs touched.
# docs/launcher.md has more.
[CmdletBinding()]
param([switch]$Jit, [switch]$NoSmoke, [string]$Version = $env:HOOKSHOT_VERSION)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 3
$Root = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
Set-Location $Root
$B = Join-Path $Root 'build/launcher'
$Rid = 'win-x64'

# (the dotnet the developer installed for this user only, when there is no other: docs/launcher.md)
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    $mine = Join-Path $env:LOCALAPPDATA 'Microsoft\dotnet'
    if (-not (Test-Path (Join-Path $mine 'dotnet.exe'))) { throw 'no dotnet: install the .NET 8 SDK' }
    $env:DOTNET_ROOT = $mine
    $env:PATH = "$mine;$env:PATH"
}
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'
# (NativeAOT finds link.exe through vswhere, which Visual Studio's installer keeps here but does not put in PATH)
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer'
if (Test-Path (Join-Path $vswhere 'vswhere.exe')) { $env:PATH = "$env:PATH;$vswhere" }

if (-not $Version) {
    # (--always: a commit with no tag before it gives its hash alone, which is not a version)
    $Version = "$(git describe --tags --long --always --match 'v[0-9]*')"
    if ($Version -notmatch '^v\d') { $Version = "v0.0.0-0-g$(git rev-parse --short HEAD)" }
}
# (-p:Version takes no v: 2.1.0-rc.1-3-g1a2b3c4, as the Builder's own publish in .github/workflows/windows-builder.yml)
$NumVersion = $Version -replace '^v', ''
"version: $Version$(if ($Jit) { ' (JIT build)' } else { ' (NativeAOT)' })"

# (its own bin and obj under build/launcher/: a publish never mixes with a `dotnet build` of launcher/Hookshot)
$Publish = Join-Path $B "$Rid\publish"
if (Test-Path $Publish) { Remove-Item -Recurse -Force $Publish }
$a = @('publish', 'launcher/Hookshot/Hookshot.csproj', '--configuration', 'Release', '--runtime', $Rid,
       '--artifacts-path', (Join-Path $B "$Rid\artifacts"), '--output', $Publish, "-p:Version=$NumVersion")
if ($Jit) { $a += '-p:HookshotJit=true' }
& dotnet @a
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed ($LASTEXITCODE)" }

# What the zip holds: the published files but the symbols, in a Hookshot folder
$Stage = Join-Path $B "$Rid\zip"
if (Test-Path $Stage) { Remove-Item -Recurse -Force $Stage }
$App = Join-Path $Stage 'Hookshot'
New-Item -ItemType Directory -Force $App | Out-Null
Get-ChildItem $Publish -Recurse -File | Where-Object { $_.Extension -notin '.pdb', '.dbg' } | ForEach-Object {
    $rel = $_.FullName.Substring($Publish.Length + 1)
    $to = Join-Path $App $rel
    New-Item -ItemType Directory -Force (Split-Path $to) | Out-Null
    Copy-Item $_.FullName $to
}
# The setup, as the release's own (--dependent-load-flag 0x800: what it imports comes from System32 only)
$Mingw = $env:LLVM_MINGW
if (-not $Mingw) {
    $Mingw = Get-ChildItem (Join-Path $env:LOCALAPPDATA 'HookshotBuilder\versions\*\HookshotBuilder\tools\llvm-mingw') -Directory -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending | Select-Object -First 1 -ExpandProperty FullName
}
if (-not $Mingw -or -not (Test-Path (Join-Path $Mingw 'bin\x86_64-w64-mingw32-clang.exe'))) {
    throw 'no llvm-mingw for the setup: set LLVM_MINGW (python -I scripts/release_parts.py llvm-mingw <folder> fetches it)'
}
$SetupWork = Join-Path $B "$Rid\setup"
New-Item -ItemType Directory -Force $SetupWork | Out-Null
Push-Location builder/setup
try {
    & (Join-Path $Mingw 'bin\x86_64-w64-mingw32-windres.exe') setup.rc -O coff -o (Join-Path $SetupWork 'setup_rc.o')
    if ($LASTEXITCODE -ne 0) { throw "windres failed ($LASTEXITCODE)" }
    & (Join-Path $Mingw 'bin\x86_64-w64-mingw32-clang.exe') -O2 -mwindows -municode setup.c (Join-Path $SetupWork 'setup_rc.o') `
        -lwinhttp -lbcrypt -lcomctl32 -lshell32 -lole32 '-Wl,--dependent-load-flag=0x800' -o (Join-Path $App 'HookshotBuilder-Setup.exe')
    if ($LASTEXITCODE -ne 0) { throw "the setup did not build ($LASTEXITCODE)" }
} finally { Pop-Location }

$files = @(Get-ChildItem $App -Recurse -File)
'{0} files, {1:N1} MB:' -f $files.Count, (($files | Measure-Object Length -Sum).Sum / 1MB)
$files | ForEach-Object { '  {0,-24} {1,10:N0}' -f $_.Name, $_.Length }

function Test-Launcher([string]$Exe, [string]$Png) {
    # (a game folder that is not there: the launcher shows "no game yet", as for a new player)
    $tmp = Join-Path $B "$Rid\smoke"
    if (Test-Path $tmp) { Remove-Item -Recurse -Force $tmp }
    New-Item -ItemType Directory -Force (Join-Path $tmp 'builders') | Out-Null
    if (Test-Path $Png) { Remove-Item $Png }
    $si = New-Object Diagnostics.ProcessStartInfo $Exe
    $si.Arguments = "--launch `"$(Join-Path $tmp 'no-game')`""
    $si.UseShellExecute = $false
    $si.EnvironmentVariables['HOOKSHOT_UI_SHOT'] = $Png
    $si.EnvironmentVariables['HOOKSHOT_LATEST_TAG'] = 'v2.1.0,v2.0.14'
    $si.EnvironmentVariables['HOOKSHOT_SETUP_BASE'] = Join-Path $tmp 'builders'
    $watch = [Diagnostics.Stopwatch]::StartNew()
    $p = [Diagnostics.Process]::Start($si)
    if (-not $p.WaitForExit(60000)) { $p.Kill(); throw 'smoke test: the launcher did not exit within 60 s' }
    $ms = $watch.ElapsedMilliseconds
    if ($p.ExitCode -ne 0) { throw "smoke test: the launcher exited with $($p.ExitCode)" }
    if (-not (Test-Path $Png)) { throw "smoke test: no $Png" }
    # (the PNG's own header: width and height, big-endian, at bytes 16 and 20)
    $h = [IO.File]::ReadAllBytes($Png)
    $w = ([int]$h[16] -shl 24) -bor ([int]$h[17] -shl 16) -bor ([int]$h[18] -shl 8) -bor $h[19]
    $ht = ([int]$h[20] -shl 24) -bor ([int]$h[21] -shl 16) -bor ([int]$h[22] -shl 8) -bor $h[23]
    if ($w -ne 960 -or $ht -ne 640) { throw "smoke test: $Png is ${w}x$ht, not 960x640" }
    "smoke test: $Png, ${w}x$ht, $ms ms"
}
if (-not $NoSmoke) { Test-Launcher (Join-Path $App 'Hookshot.exe') (Join-Path $B "smoke-$Rid.png") }

$Zip = Join-Path $B "Hookshot-$Version-Windows-x64-portable.zip"
if (Test-Path $Zip) { Remove-Item $Zip }
Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::CreateFromDirectory($App, $Zip, [IO.Compression.CompressionLevel]::Optimal, $true)
'{0}: {1:N1} MB' -f $Zip, ((Get-Item $Zip).Length / 1MB)
