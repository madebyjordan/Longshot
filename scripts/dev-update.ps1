# Everything a dev build needs to be the newest, in one go, so that nobody has to remember which of them is behind:
#
#   powershell -File scripts/dev-update.ps1
#
#   1. the checkout this script is in is brought forward to GitHub's dev (a fast-forward: only when nothing in it is
#      changed and not committed, and it has no commits of its own that GitHub's dev lacks; else it is left as it is,
#      and the line says why);
#   2. the dev game folder is built again from it (scripts/build-game.ps1 -Channel dev: only what changed is compiled);
#   3. the Longshot Builder that is installed, which is also the launcher, is built again from it when its source
#      (builder/HookshotBuilder, builder/setup) differs from what the installed one was built from, and put in its place.
#
# The launcher's Update for a dev build runs this, hidden, and shows what it says (builder/HookshotBuilder/Launcher.cs): the
# build tools' own lines (@@stage, @@progress, ...) and "@@stage source" / "@@stage launcher" around them, "@@done <folder>"
# at the end or "@@error <why>". A launcher that is running cannot be replaced: with -Launcher <its folder> the new one is
# left ready (bin\ready) and named in "@@launcher <folder>", and the launcher puts it in place as it closes
# (scripts/install-launcher.ps1). Started by hand, it is put in place at once when no Longshot Builder is running.
#
# Options: -Longshot <folder> (the folder holding game\; found from this script's place when left out), -Jobs <n>,
# -Launcher <folder> (the installed builder's folder: the launcher's own, when it starts this), -LauncherCommit <commit>
# (what that one was built from, when the launcher knows; else read from its file), -NoLauncher (the game only),
# -ForceLauncher (the launcher built again though its source is the same), -NoPull (the checkout as it is).
param([string]$Longshot = "", [int]$Jobs = 0, [string]$Launcher = "", [string]$LauncherCommit = "", [switch]$NoLauncher, [switch]$ForceLauncher,
      [switch]$NoPull)
$ErrorActionPreference = "Stop"
[Console]::OutputEncoding = New-Object Text.UTF8Encoding $false   # (what it says is read as UTF-8: a folder may have any name)
$src = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$byLauncher = [bool]$Launcher
$why = ""

function Say([string]$line) { [Console]::Out.WriteLine($line); [Console]::Out.Flush() }
# (git.exe by its whole name: a command is looked up without regard to capitals, and "git" here would be this function)
function Git { $ErrorActionPreference = "Continue"; $said = & git.exe -C $src @args 2>$null; $script:gitCode = $LASTEXITCODE; $said }

try {
    # ---- 1: the source ----
    Say "@@stage source Bringing the source up to date"
    $branch = "$(Git rev-parse --abbrev-ref HEAD)".Trim()
    $changed = @(Git status --porcelain --untracked-files=no).Count
    Git fetch --quiet origin dev | Out-Null
    $fetched = $gitCode -eq 0
    $behind = if ($fetched) { [int]"$(Git rev-list --count HEAD..origin/dev)" } else { 0 }
    $ahead = if ($fetched) { [int]"$(Git rev-list --count origin/dev..HEAD)" } else { 0 }
    if ($NoPull) { Say "the source is left as it is (-NoPull)" }
    elseif (-not $fetched) { Say "GitHub's dev could not be asked for: the source is built as it is" }
    elseif (-not $behind) { Say "the source has all of GitHub's dev" }
    elseif ($changed) { Say "the source is $behind commit(s) behind GitHub's dev, and $changed file(s) in it are changed and not committed: it is built as it is. Commit them, and update again." }
    elseif ($ahead) { Say "the source is $behind commit(s) behind GitHub's dev and has $ahead of its own: it is built as it is. Merge or rebase it by hand." }
    elseif ($branch -ne "dev") { Say "the source is on '$branch', not dev: it is built as it is" }
    else {
        Git merge --ff-only --quiet origin/dev | Out-Null
        if ($gitCode) { throw "The source could not be brought forward to GitHub's dev (git merge --ff-only stopped with $gitCode)." }
        Say "the source was brought forward $behind commit(s), to $("$(Git rev-parse --short=7 HEAD)".Trim())"
    }

    # ---- 2: the game (this checkout's own build script: the one just brought forward) ----
    $build = @{ Channel = "dev"; Source = $src; Lines = $true }
    if ($Longshot) { $build.Longshot = $Longshot }
    if ($Jobs -gt 0) { $build.Jobs = $Jobs }
    $done = ""
    $why = ""
    & (Join-Path $src "scripts\build-game.ps1") @build | ForEach-Object {
        $line = "$_"
        if ($line.StartsWith("@@done ")) { $done = $line.Substring(7) }   # (said at the very end, after the launcher)
        elseif ($line.StartsWith("@@error ")) { $why = $line.Substring(8) }   # (the build tools' own reason: said once, below)
        else { Say $line }
    }
    if (-not $done) { throw "The dev build did not say where it put the game." }

    # ---- 3: the launcher ----
    if (-not $NoLauncher) {
        if (-not $Launcher) {
            $current = "$env:LOCALAPPDATA\LongshotBuilder\versions\current.txt"
            if (Test-Path $current) { $Launcher = "$env:LOCALAPPDATA\LongshotBuilder\versions\$((Get-Content $current -TotalCount 1).Trim())\HookshotBuilder" }
        }
        $dll = if ($Launcher) { Join-Path $Launcher "LongshotBuilder.dll" } else { "" }
        if (-not $dll -or -not (Test-Path $dll)) { Say "no Longshot Builder is installed here: the launcher is not built" }
        else {
            # (what the installed one was built from: its product version ends in "+<commit>", as .NET writes it)
            if (-not $LauncherCommit -and ((Get-Item $dll).VersionInfo.ProductVersion -match '\+([0-9a-f]{7,40})$')) { $LauncherCommit = $matches[1] }
            $differs = $true
            if ($LauncherCommit -and -not $ForceLauncher) {
                Git diff --quiet $LauncherCommit HEAD -- builder/HookshotBuilder builder/setup | Out-Null
                # (1: it differs; more: that commit is not in this checkout, so it cannot be told, and it is built)
                $differs = $gitCode -ne 0
                # (its own source changed and not committed: a build of it is not what a commit says)
                if (-not $differs -and @(Git status --porcelain --untracked-files=no -- builder/HookshotBuilder builder/setup).Count) { $differs = $true }
            }
            if (-not $differs) { Say "the launcher is built from the same builder source: left as it is" }
            else {
                Say "@@stage launcher Building the launcher"
                $dotnet = @("$env:DOTNET_ROOT\dotnet.exe", "$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe") | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1
                if (-not $dotnet) { $dotnet = (Get-Command dotnet -ErrorAction SilentlyContinue).Source }
                $mingw = Join-Path $Launcher "tools\llvm-mingw\bin"
                if (-not $dotnet) { Say "the launcher was not built: no .NET SDK on this PC (it is the installed one still)" }
                elseif (-not (Test-Path "$mingw\x86_64-w64-mingw32-clang.exe")) { Say "the launcher was not built: the installed builder has no compiler for its setup ($mingw)" }
                else {
                    $ready = Join-Path $src "builder\HookshotBuilder\bin\ready"
                    $env:DOTNET_ROOT = Split-Path $dotnet
                    $env:DOTNET_CLI_TELEMETRY_OPTOUT = "1"
                    $ErrorActionPreference = "Continue"
                    $said = & $dotnet publish (Join-Path $src "builder\HookshotBuilder\HookshotBuilder.csproj") -c Release -r win-x64 --self-contained true -m:1 -p:NuGetAudit=false -nologo -v:q -o $ready 2>&1
                    $code = $LASTEXITCODE
                    $ErrorActionPreference = "Stop"
                    if ($code) { $said | Select-Object -Last 12 | ForEach-Object { Say "$_" }; throw "The launcher did not build (dotnet publish stopped with $code). The game was built: it is the installed launcher still." }
                    Say "@@progress 0.8"
                    $ErrorActionPreference = "Continue"
                    & "$mingw\x86_64-w64-mingw32-windres.exe" (Join-Path $src "builder\setup\setup.rc") -I (Join-Path $src "builder\setup") -O coff -o "$ready\setup_rc.o"
                    & "$mingw\x86_64-w64-mingw32-clang.exe" -O2 -mwindows -municode (Join-Path $src "builder\setup\setup.c") "$ready\setup_rc.o" -lwinhttp -lbcrypt -lcomctl32 -lshell32 -lole32 "-Wl,--dependent-load-flag=0x800" -o "$ready\LongshotBuilder-Setup.exe" 2>&1 | ForEach-Object { Say "$_" }   # (as the release's is built: .github/workflows/windows-builder.yml)
                    $code = $LASTEXITCODE
                    $ErrorActionPreference = "Stop"
                    Remove-Item "$ready\setup_rc.o" -ErrorAction SilentlyContinue
                    if ($code) { throw "The launcher's setup did not compile ($code). The game was built: it is the installed launcher still." }
                    Say "@@progress 1"
                    if ($byLauncher) { Say "@@launcher $ready" }
                    elseif (Get-Process LongshotBuilder -ErrorAction SilentlyContinue) {
                        Say "a Longshot Builder is open, so the new launcher is left ready in $ready. Close it and run: powershell -File scripts/install-launcher.ps1"
                    } else {
                        & (Join-Path $src "scripts\install-launcher.ps1") -From $ready -To $Launcher | ForEach-Object { Say "$_" }
                    }
                }
            }
        }
    }
    Say "@@done $done"
} catch {
    Say "@@error $(if ($why) { $why } else { $_.Exception.Message })"
    exit 1
}
