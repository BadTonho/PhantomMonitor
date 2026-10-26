#!/bin/sh
# Phantom Monitor: empacotador EXPERIMENTAL de AppImage — AppDir coberto por regressão;
# geração/execução do AppImage real exige appimagetool e validação em desktop.
#
# Requisitos (rode no Linux): appimagetool no PATH (AppImageKit) + o publish
# linux-x64 do frontend Avalonia gerado antes (scripts/release.ps1 ou
# dotnet publish -r linux-x64 --self-contained -p:PublishSingleFile=true).
#
# Uso: appimage.sh <dir-do-publish-linux> <saida>.AppImage
# Resultado: PhantomMonitor-<versão>-linux-x64.AppImage (nome esperado pelo updater).
set -eu

PUBLISH_DIR=${1:?Uso: appimage.sh <dir-do-publish-linux> <saida>.AppImage}
OUTPUT_APPIMAGE=${2:?Uso: appimage.sh <dir-do-publish-linux> <saida>.AppImage}
APPNAME=PhantomMonitor

[ -f "$PUBLISH_DIR/$APPNAME" ] || {
    echo "ERROR: $PUBLISH_DIR/$APPNAME not found/not executable. Publish first." >&2
    exit 1
}

: "${APPIMAGE_TOOL:=appimagetool}"
command -v "$APPIMAGE_TOOL" >/dev/null 2>&1 || {
    echo "ERROR: appimagetool not found in PATH (AppImageKit)." >&2
    exit 1
}

APPDIR=$(mktemp -d)
trap 'rm -rf "$APPDIR"' EXIT

# Conteúdo do publish vai para usr/bin (AppImage convenção).
mkdir -p "$APPDIR/usr/bin"
cp -r "$PUBLISH_DIR/." "$APPDIR/usr/bin/"
chmod +x "$APPDIR/usr/bin/$APPNAME"

# Ícone provisório (fantasma.png do repo); substitua quando houver ícone oficial.
REPO_ROOT=$(cd "$(dirname "$0")/../.." && pwd)
mkdir -p "$APPDIR/usr/share/icons/hicolor/256x256/apps"
cp "$REPO_ROOT/fantasma.png" "$APPDIR/usr/share/icons/hicolor/256x256/apps/phantom-monitor.png"
cp "$REPO_ROOT/fantasma.png" "$APPDIR/.DirIcon"
cp "$REPO_ROOT/fantasma.png" "$APPDIR/phantom-monitor.png"

# AppRun mínimo: executa o binário .NET junto dos assets empacotados.
cat > "$APPDIR/AppRun" <<EOF
#!/bin/sh
HERE=\$(dirname "\$(readlink -f "\$0")")
exec "\$HERE/usr/bin/$APPNAME" "\$@"
EOF
chmod +x "$APPDIR/AppRun"

cat > "$APPDIR/phantom-monitor.desktop" <<'EOF'
[Desktop Entry]
Type=Application
Name=Phantom Monitor
Comment=Hardware, FPS and frametime local monitor
Exec=PhantomMonitor
Icon=phantom-monitor
Categories=System;Utility;
EOF

"$APPIMAGE_TOOL" "$APPDIR" "$OUTPUT_APPIMAGE"
echo "AppImage gerado: $OUTPUT_APPIMAGE"
