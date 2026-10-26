#!/bin/sh
# Copy the entire payload before moving installed entries; keep recovery backups.
set -eu
PID=$1; PAYLOAD=$2; TARGET=$3; EXE=$4; MODE=$5
i=0
while [ "$PID" != 0 ] && kill -0 "$PID" 2>/dev/null; do
    [ "$i" -lt 60 ] || { echo 'Application did not exit; installation unchanged.' >&2; exit 1; }
    sleep 1
    i=$((i + 1))
done
if [ "$MODE" = appimage ]; then
    directory=$(dirname "$TARGET")
    incoming=$(mktemp "$directory/.phantom-update-new.XXXXXX")
    backup=$(mktemp "$directory/.phantom-update-backup.XXXXXX")
    cp -p "$PAYLOAD" "$incoming" && chmod +x "$incoming" || exit 1
    cp -p "$TARGET" "$backup" || exit 1
    mv -f "$incoming" "$TARGET" || { cp -p "$backup" "$TARGET"; exit 1; }
    cd "$directory"
    nohup "$TARGET" >/dev/null 2>&1 &
    exit 0
fi
TARGET=$(cd "$TARGET" && pwd -P)
PAYLOAD=$(cd "$PAYLOAD" && pwd -P)
case "$PAYLOAD/" in "$TARGET/"*) echo 'Payload must be outside installation.' >&2; exit 1 ;; esac
case "$TARGET/" in "$PAYLOAD/"*) echo 'Installation must be outside payload.' >&2; exit 1 ;; esac
[ -f "$PAYLOAD/$EXE" ] || exit 1
# Package members are top-level names; reject links and line breaks used by the journal.
[ -z "$(find "$PAYLOAD" -type l -print -quit)" ] || exit 1
backup=$(mktemp -d "$TARGET/.phantom-update-backup.XXXXXX")
incoming="$backup/incoming"
mkdir "$incoming" "$backup/original"
cp -a "$PAYLOAD/." "$incoming/" || exit 1
chmod +x "$incoming/$EXE" || exit 1
: > "$backup/installed.list"
: > "$backup/original.list"
rollback() {
    trap - HUP INT TERM
    failed=0
    while IFS= read -r name; do
        case "$name" in ''|*/*|.|..) failed=1; continue ;; esac
        rm -rf -- "$TARGET/$name" || failed=1
    done < "$backup/installed.list"
    while IFS= read -r name; do
        case "$name" in ''|*/*|.|..) failed=1; continue ;; esac
        mv -- "$backup/original/$name" "$TARGET/$name" || failed=1
    done < "$backup/original.list"
    if [ "$failed" = 0 ]; then echo rolled-back > "$backup/status"; else echo recovery-required > "$backup/status"; fi
    echo "Update failed. Recovery backup: $backup" >&2
    exit 1
}
trap rollback HUP INT TERM
for src in "$incoming"/* "$incoming"/.[!.]* "$incoming"/..?*; do
    [ -e "$src" ] || continue
    name=${src##*/}
    case "$name" in config.json|phantom.installed) continue ;; esac
    case "$name" in .phantom-update-*|*'
'*) rollback ;; esac
    if [ -e "$TARGET/$name" ] || [ -L "$TARGET/$name" ]; then
        mv -- "$TARGET/$name" "$backup/original/$name" || rollback
        printf '%s\n' "$name" >> "$backup/original.list"
    fi
    printf '%s\n' "$name" >> "$backup/installed.list"
    mv -- "$src" "$TARGET/$name" || rollback
done
echo committed > "$backup/status"
trap - HUP INT TERM
cd "$TARGET"
nohup "./$EXE" >/dev/null 2>&1 &
