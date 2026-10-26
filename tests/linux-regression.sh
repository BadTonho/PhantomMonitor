#!/bin/sh
set -eu
repo=$(CDPATH= cd -- "$(dirname "$0")/.." && pwd)
work=$(mktemp -d)
trap 'rm -rf -- "$work"' EXIT
payload="$work/payload's data"
target="$work/installed app"
mkdir -p "$payload/native/nested" "$target/native/nested"
printf '#!/bin/sh\nexit 0\n' > "$payload/PhantomMonitor"
chmod +x "$payload/PhantomMonitor"
printf new > "$payload/native/nested/renderer.so"
printf old > "$target/native/nested/renderer.so"
printf user-settings > "$target/config.json"
printf payload-must-not-overwrite > "$payload/config.json"
printf portable > "$payload/phantom.portable"
printf '#!/bin/sh\nexit 0\n' > "$target/PhantomMonitor"
sh "$repo/src/PhantomMonitor.Core/Services/Update/Scripts/linux.sh" 0 "$payload" "$target" PhantomMonitor portable
[ "$(cat "$target/native/nested/renderer.so")" = new ]
[ -x "$target/PhantomMonitor" ]
[ "$(cat "$target/config.json")" = user-settings ]
[ -f "$target/phantom.portable" ]
echo 'PASS: Linux portable updater copies subfolders and handles spaces/apostrophes'

# Inject one failed move through a PATH-local wrapper. Real installation paths
# are never used; this exercises rollback after the old native directory moved.
mkdir "$work/commands"
real_mv=$(command -v mv)
cat > "$work/commands/mv" <<EOF
#!/bin/sh
case "\$*" in */incoming/native*) exit 1 ;; esac
exec "$real_mv" "\$@"
EOF
chmod +x "$work/commands/mv"
printf previous > "$target/native/nested/renderer.so"
if PATH="$work/commands:$PATH" sh "$repo/src/PhantomMonitor.Core/Services/Update/Scripts/linux.sh" 0 "$payload" "$target" PhantomMonitor portable; then
    echo 'FAIL: injected move failure was ignored' >&2
    exit 1
fi
[ "$(cat "$target/native/nested/renderer.so")" = previous ]
[ -x "$target/PhantomMonitor" ]
[ "$(cat "$target/config.json")" = user-settings ]
echo 'PASS: Linux portable updater restores previously installed entries after failure'

appimage="$work/Monitor's app.AppImage"
printf '#!/bin/sh\nexit 0\n' > "$appimage"
sh "$repo/src/PhantomMonitor.Core/Services/Update/Scripts/linux.sh" 0 "$payload/PhantomMonitor" "$appimage" PhantomMonitor appimage
[ -x "$appimage" ]
[ "$(cat "$target/config.json")" = user-settings ]
echo 'PASS: AppImage updater replaces atomically and keeps a backup'

if command -v dpkg-deb >/dev/null 2>&1; then
    chmod -x "$payload/PhantomMonitor"
    sh "$repo/packaging/linux/deb.sh" "$payload" 1.2.3 "$work/packages"
    mkdir "$work/deb"
    dpkg-deb -x "$work/packages/phantom-monitor_1.2.3_amd64.deb" "$work/deb"
    [ "$(readlink "$work/deb/usr/bin/phantom-monitor")" = ../lib/phantom-monitor/PhantomMonitor ]
    [ -f "$work/deb/usr/bin/phantom-monitor" ]
    [ -x "$work/deb/usr/bin/phantom-monitor" ]
    [ -f "$work/deb/usr/lib/phantom-monitor/phantom.installed" ]
    [ ! -e "$work/deb/usr/lib/phantom-monitor/phantom.portable" ]
    [ ! -e "$work/deb/usr/lib/phantom-monitor/config.json" ]
    echo 'PASS: deb symlink resolves inside the package without usrmerge'
fi

cat > "$work/appimagetool" <<'EOF'
#!/bin/sh
set -eu
[ -x "$1/usr/bin/PhantomMonitor" ]
[ -f "$1/phantom-monitor.png" ]
[ -f "$1/.DirIcon" ]
[ -f "$1/phantom-monitor.desktop" ]
[ ! -e "$1/usr/bin/phantom.portable" ]
[ ! -e "$1/usr/bin/config.json" ]
: > "$2"
EOF
chmod +x "$work/appimagetool"
APPIMAGE_TOOL="$work/appimagetool" sh "$repo/packaging/linux/appimage.sh" "$payload" "$work/test.AppImage"
echo 'PASS: AppDir contains root icon, desktop entry and executable payload'
