#!/bin/bash
# The Hookshot launcher for Linux (launcher/Hookshot), as players download it: published for linux-x64 as
# launcher/Hookshot/Publish.props says (NativeAOT), checked by drawing its window with no screen, and packed into an AppImage
# from launcher/packaging/linux:
#
#   scripts/launcher/publish.sh              -> build/launcher/Hookshot-Launcher-<version>-x86_64.AppImage
#   scripts/launcher/publish.sh --jit        the JIT build instead (trimmed, ReadyToRun, one file): no clang needed
#   scripts/launcher/publish.sh --no-smoke   without the checks
#
# Needs the .NET 8 SDK, and for NativeAOT clang and zlib1g-dev. Publish on the oldest system the AppImage is for: the
# executable needs that system's glibc or newer (CI: Ubuntu 22.04, glibc 2.35, as the Builder's AppImage).
# appimagetool and the AppImage runtime are the ones scripts/linux/deps.sh pins (version and SHA-256 read from it), in its
# folder (build-linux/deps, or $DEPS): fetched here when deps.sh has not fetched them yet.
# The checks (HOOKSHOT_UI_SHOT, launcher/Hookshot/Shot.cs) draw the window with Skia and Avalonia.Headless, no display
# needed, to a 960x640 PNG: once from the AppDir's files, once from the AppImage itself (--appimage-extract-and-run, no
# FUSE needed), with GitHub's answer made up (HOOKSHOT_LATEST_TAG) and the builders' folders temporary ones: no network,
# nothing of this PC's installs touched. docs/launcher.md has more.
set -euo pipefail
cd "$(dirname "$0")/../.."
ROOT=$PWD
B=$ROOT/build/launcher
RID=linux-x64
DEPS=${DEPS:-$ROOT/build-linux/deps}
JIT=
SMOKE=1
for a in "$@"; do
    case $a in
        --jit) JIT=1 ;;
        --no-smoke) SMOKE= ;;
        *) echo "usage: $0 [--jit] [--no-smoke]"; exit 2 ;;
    esac
done
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1

# (as scripts/linux/appimage.sh names the Builder's: git describe --long, v2.1.0-rc.1-3-g1a2b3c4; -p:Version takes no v)
VERSION=${HOOKSHOT_VERSION:-$(git describe --tags --long --always --match "v[0-9]*" 2>/dev/null || true)}
[[ $VERSION == v[0-9]* ]] || VERSION=v0.0.0-0-g$(git rev-parse --short HEAD)
echo "version: $VERSION ($([ -n "$JIT" ] && echo JIT build || echo NativeAOT))"

# ---- publish (its own bin and obj under build/launcher: never mixed with a `dotnet build` of launcher/Hookshot) ----
PUB=$B/$RID/publish
rm -rf "$PUB"
dotnet publish launcher/Hookshot/Hookshot.csproj --configuration Release --runtime "$RID" \
    --artifacts-path "$B/$RID/artifacts" --output "$PUB" -p:Version="${VERSION#v}" ${JIT:+-p:HookshotJit=true}

# ---- the AppDir: AppRun, the .desktop file and the icon at its top, the launcher in usr/lib/hookshot-launcher ----
APPDIR=$B/$RID/AppDir
rm -rf "$APPDIR"
LIB=$APPDIR/usr/lib/hookshot-launcher
mkdir -p "$LIB" "$APPDIR/usr/share/icons/hicolor/256x256/apps"
# (not the symbols: Hookshot.dbg, NativeAOT's, is most of the publish folder)
(cd "$PUB" && find . -type f ! -name '*.dbg' ! -name '*.pdb' -exec cp --parents {} "$LIB/" \;)
chmod +x "$LIB/Hookshot"
P=launcher/packaging/linux
cp "$P/AppRun" "$APPDIR/AppRun" && chmod +x "$APPDIR/AppRun"
cp "$P/hookshot.desktop" "$APPDIR/"
# (the icon is the Builder's for now: builder/HookshotBuilder/Images)
cp builder/HookshotBuilder/Images/hookshot.png "$APPDIR/hookshot.png"
cp builder/HookshotBuilder/Images/hookshot.png "$APPDIR/usr/share/icons/hicolor/256x256/apps/hookshot.png"
echo "$LIB: $(du -sh "$LIB" | cut -f1)"
ls -l "$LIB"

# The glibc the launcher and its libraries need: the newest GLIBC_ symbol version any of them asks for
glibc_needed() {
    objdump -T "$@" 2>/dev/null | grep -o 'GLIBC_[0-9.]*' | sort -uV | tail -1
}
if command -v objdump >/dev/null; then
    for f in "$LIB"/Hookshot "$LIB"/*.so*; do echo "  $(basename "$f"): $(glibc_needed "$f")"; done
    echo "glibc needed: $(glibc_needed "$LIB"/Hookshot "$LIB"/*.so*) (this system: $(ldd --version | head -1 | grep -o '[0-9.]*$'))"
fi

# smoke <command...>: the window drawn with no display to $B/smoke-<name>.png, which must be 960x640
smoke() {
    local name=$1 png=$B/smoke-$1.png tmp=$B/$RID/smoke-$1
    shift
    rm -rf "$tmp" "$png" && mkdir -p "$tmp/builders" "$tmp/data" "$tmp/cache"
    local t0=$(date +%s%N)
    env -u DISPLAY -u WAYLAND_DISPLAY HOOKSHOT_UI_SHOT="$png" HOOKSHOT_LATEST_TAG=v2.1.0,v2.0.14 \
        HOOKSHOT_SETUP_BASE="$tmp/builders" HOOKSHOT_BUILDER_HOME="$tmp/data" XDG_DATA_HOME="$tmp/data" XDG_CACHE_HOME="$tmp/cache" \
        timeout 60 "$@" --launch "$tmp/no-game"
    local ms=$(( ($(date +%s%N) - t0) / 1000000 ))
    [ -f "$png" ] || { echo "smoke test ($name): no $png"; exit 1; }
    # (the PNG's own header: width and height, big-endian, at bytes 16 and 20)
    local size
    size=$(od -An -tu1 -j16 -N8 "$png" | awk '{ printf "%dx%d", $1*16777216 + $2*65536 + $3*256 + $4, $5*16777216 + $6*65536 + $7*256 + $8 }')
    [ "$size" = 960x640 ] || { echo "smoke test ($name): $png is $size, not 960x640"; exit 1; }
    echo "smoke test ($name): $png, $size, $ms ms"
}
[ -z "$SMOKE" ] || smoke "$RID" "$APPDIR/AppRun"

# ---- appimagetool and the runtime: scripts/linux/deps.sh's, at its pinned versions and checksums ----
pin() { sed -n "s/^$1=\([^ ]*\).*/\1/p" scripts/linux/deps.sh; }
APPIMAGETOOL=$(pin APPIMAGETOOL) APPIMAGETOOL_SHA256=$(pin APPIMAGETOOL_SHA256)
APPIMAGE_RUNTIME=$(pin APPIMAGE_RUNTIME) APPIMAGE_RUNTIME_SHA256=$(pin APPIMAGE_RUNTIME_SHA256)
[ -n "$APPIMAGETOOL" ] && [ -n "$APPIMAGETOOL_SHA256" ] && [ -n "$APPIMAGE_RUNTIME" ] && [ -n "$APPIMAGE_RUNTIME_SHA256" ] ||
    { echo "scripts/linux/deps.sh: the appimagetool or runtime pins were not found"; exit 1; }
# (deps.sh's marks, so neither fetches what the other has: build-linux/deps/.appimagetool and .runtime hold the versions)
get() {   # mark version url file sha256
    local file=$DEPS/$4
    if [ "$(cat "$DEPS/.$1" 2>/dev/null)" != "$2" ] || [ ! -f "$file" ]; then
        mkdir -p "$DEPS/downloads"
        curl -fL --retry 5 --retry-delay 10 --proto '=https' -o "$DEPS/downloads/$4.part" "$3"
        echo "$5  $DEPS/downloads/$4.part" | sha256sum -c --quiet - || { echo "$4: not the expected download"; exit 1; }
        mv -f "$DEPS/downloads/$4.part" "$file" && chmod +x "$file"
        echo "$2" > "$DEPS/.$1"
    fi
    echo "$5  $file" | sha256sum -c --quiet - || { echo "$file: not the pinned $1 $2 (SHA-256 differs)"; exit 1; }
}
get appimagetool "$APPIMAGETOOL" \
    "https://github.com/AppImage/appimagetool/releases/download/$APPIMAGETOOL/appimagetool-x86_64.AppImage" appimagetool "$APPIMAGETOOL_SHA256"
get runtime "$APPIMAGE_RUNTIME" \
    "https://github.com/AppImage/type2-runtime/releases/download/$APPIMAGE_RUNTIME/runtime-x86_64" runtime-x86_64 "$APPIMAGE_RUNTIME_SHA256"

# ---- the AppImage (zstd, as the Builder's: its squashfs is read faster than gzip's) ----
# (not Hookshot-<version>-x86_64.AppImage: that is the Builder's, which a release holds too, and its .zsync updates match)
OUT=$B/Hookshot-Launcher-$VERSION-x86_64.AppImage
rm -f "$OUT"
ARCH=x86_64 APPIMAGE_EXTRACT_AND_RUN=1 "$DEPS/appimagetool" --no-appstream --runtime-file "$DEPS/runtime-x86_64" --comp zstd \
    "$APPDIR" "$OUT"
echo "$OUT: $(du -h "$OUT" | cut -f1)"
[ -z "$SMOKE" ] || smoke appimage "$OUT" --appimage-extract-and-run
