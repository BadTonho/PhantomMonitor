#!/bin/sh
# Phantom Monitor: empacotador EXPERIMENTAL de .deb — estrutura coberta por regressão;
# execução do aplicativo instalado ainda exige validação em desktop.
#
# Instalação de sistema (/usr/lib/phantom-monitor) NÃO é atualizável pelo
# app (pasta protegida; updater fica em modo notificação): atualizar exige
# instalar o .deb novo. Kebab do pacote: phantom-monitor.
#
# Uso: deb.sh <dir-do-publish-linux> <versão> <dir-de-saída>
# Saída: phantom-monitor_<versão>_amd64.deb
set -eu

PUBLISH_DIR=${1:?Uso: deb.sh <dir-do-publish-linux> <versão> <dir-de-saída>}
PACKAGE_VERSION=${2:?Uso: deb.sh <dir-do-publish-linux> <versão> <dir-de-saída>}
OUT_DIR=${3:?Uso: deb.sh <dir-do-publish-linux> <versão> <dir-de-saída>}
APPNAME=PhantomMonitor

[ -f "$PUBLISH_DIR/$APPNAME" ] || {
    echo "ERROR: $PUBLISH_DIR/$APPNAME not found/not executable. Publish first." >&2
    exit 1
}
command -v dpkg-deb >/dev/null 2>&1 || {
    echo "ERROR: dpkg-deb not found (distros Debian-family only)." >&2
    exit 1
}

WORK=$(mktemp -d)
trap 'rm -rf "$WORK"' EXIT

mkdir -p "$WORK/DEBIAN" "$WORK/usr/bin" "$WORK/usr/lib/phantom-monitor" "$WORK/usr/share/applications"

cp -r "$PUBLISH_DIR/." "$WORK/usr/lib/phantom-monitor/"
chmod +x "$WORK/usr/lib/phantom-monitor/$APPNAME"
ln -s ../lib/phantom-monitor/$APPNAME "$WORK/usr/bin/phantom-monitor"

# Dependências de runtime do .NET/Avalonia em self-contained; revise/ajuste.
cat > "$WORK/DEBIAN/control" <<EOF
Package: phantom-monitor
Version: $PACKAGE_VERSION
Architecture: amd64
Maintainer: BadTonho
Depends: libc6, libstdc++6, zlib1g, libssl3t64 | libssl3, libicu78 | libicu77 | libicu76 | libicu74 | libicu72 | libicu70, libfontconfig1, libfreetype6, libx11-6, libxext6, libxrender1, libxrandr2, libxcursor1, libxi6
Section: utils
Priority: optional
Description: Hardware, FPS and frametime local monitor (Avalonia dashboard for Linux)
EOF

# Sem postinst especial: o symlink já cobre o PATH; .desktop aponta pro binário.
cat > "$WORK/usr/share/applications/phantom-monitor.desktop" <<'EOF'
[Desktop Entry]
Type=Application
Name=Phantom Monitor
Comment=Hardware, FPS and frametime local monitor
Exec=/usr/bin/phantom-monitor
Categories=System;Utility;
EOF

mkdir -p "$OUT_DIR"
dpkg-deb --build "$WORK" "$OUT_DIR/phantom-monitor_${PACKAGE_VERSION}_amd64.deb"
echo "deb gerado em $OUT_DIR/phantom-monitor_${PACKAGE_VERSION}_amd64.deb"
