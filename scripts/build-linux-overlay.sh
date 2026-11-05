#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"
BUILD_DIR="${REPO_ROOT}/build/linux-overlay-native"
OUTPUT_DIR="${1:-${REPO_ROOT}/build/linux-overlay-payload}"

cmake -S "${REPO_ROOT}/src/PhantomVulkanLayer" -B "${BUILD_DIR}" \
    -DCMAKE_BUILD_TYPE=Release -DBUILD_TESTING=OFF
cmake --build "${BUILD_DIR}" --config Release --parallel "$(nproc)"

mkdir -p "${OUTPUT_DIR}"
cp "${BUILD_DIR}/libVkLayer_phantom_monitor.so" "${OUTPUT_DIR}/libVkLayer_phantom_monitor.so"
cp "${BUILD_DIR}/PhantomNativeOverlay/libPhantomOpenGLOverlay.so" "${OUTPUT_DIR}/libPhantomOpenGLOverlay.so"
cp "${BUILD_DIR}/phantom_vulkan_layer.json" "${OUTPUT_DIR}/phantom_vulkan_layer.json"
cp "${REPO_ROOT}/src/PhantomNativeOverlay/third_party/imgui/LICENSE.txt" "${OUTPUT_DIR}/LICENSE-Dear-ImGui.txt"

cat << 'EOF' > "${OUTPUT_DIR}/phantom-hud"
#!/usr/bin/env bash
# Phantom Monitor HUD Wrapper
# Uso: phantom-hud <comando_do_jogo> [argumentos...]

if [ $# -eq 0 ]; then
    echo "Phantom Monitor HUD (phantom-hud)"
    echo "Uso: phantom-hud <comando_do_jogo> [argumentos...]"
    echo ""
    echo "Exemplos:"
    echo "  Steam:             phantom-hud %command%"
    echo "  Prism / MultiMC:   defina o wrapper command como: phantom-hud"
    echo "  Lutris / Heroic:   coloque como prefixo de comando: phantom-hud"
    echo "  Terminal:          phantom-hud ./meujogo"
    exit 1
fi

OPENGL_LIB="${HOME}/.local/lib/libPhantomOpenGLOverlay.so"
export ENABLE_PHANTOM_OVERLAY=1
export PHANTOM_FPS_CAPTURE=1
unset DISABLE_PHANTOM_OVERLAY

if [ -f "${OPENGL_LIB}" ]; then
    export LD_PRELOAD="${OPENGL_LIB}${LD_PRELOAD:+:$LD_PRELOAD}"
fi

exec "$@"
EOF
chmod +x "${OUTPUT_DIR}/phantom-hud"

echo "Renderizadores Linux prontos em: ${OUTPUT_DIR}"
