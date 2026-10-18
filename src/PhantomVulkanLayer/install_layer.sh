#!/usr/bin/env bash
set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
BUILD_DIR="${SCRIPT_DIR}/build"
# IMPORTANTE (convenção do Vulkan Loader): o sufixo é SINGULAR — "implicit_layer.d".
# A instalação antiga em "explicit_layers.d" (plural) nunca era encontrada pelo
# loader, e enable_environment em diretórios de camada EXPLÍCITA é ignorado.
VULKAN_LAYER_DIR="${HOME}/.local/share/vulkan/implicit_layer.d"
LIB_DIR="${HOME}/.local/lib"
MANIFEST="${VULKAN_LAYER_DIR}/phantom_vulkan_layer.json"
LIB_PATH="${LIB_DIR}/libVkLayer_phantom_monitor.so"

echo "=== Compilando PhantomVulkanLayer ==="
cmake -B "${BUILD_DIR}" -S "${SCRIPT_DIR}" -DCMAKE_BUILD_TYPE=Release
cmake --build "${BUILD_DIR}" -j"$(nproc)"

echo "=== Instalando camada Vulkan (implícita) para o usuário ==="
mkdir -p "${VULKAN_LAYER_DIR}" "${LIB_DIR}"
cp "${BUILD_DIR}/libVkLayer_phantom_monitor.so" "${LIB_PATH}"

# library_path escapada para JSON (barra invertida e aspa dupla) — suporta
# caminhos com espaços e/ou caracteres especiais do nome do usuário.
LIB_JSON="$(printf '%s' "${LIB_PATH}" | sed -e 's/\\/\\\\/g' -e 's/"/\\"/g')"

# Camada IMPLÍCITA (type GLOBAL + diretório implicit_layer.d): o loader a carrega
# em todo processo Vulkan, mas a telemetria só é publicada com ENABLE_PHANTOM_
# OVERLAY=1 (gating por jogo). DISABLE_PHANTOM_OVERLAY=1 vence o enable.
cat <<EOF > "${MANIFEST}"
{
    "file_format_version": "1.2.0",
    "layer": {
        "name": "VK_LAYER_PHANTOM_monitor",
        "type": "GLOBAL",
        "library_path": "${LIB_JSON}",
        "api_version": "1.3.0",
        "implementation_version": "1",
        "description": "PhantomMonitor Native Vulkan Frametime & FPS Telemetry Layer",
        "functions": {
            "vkGetInstanceProcAddr": "Phantom_vkGetInstanceProcAddr",
            "vkGetDeviceProcAddr": "Phantom_vkGetDeviceProcAddr"
        },
        "enable_environment": {
            "ENABLE_PHANTOM_OVERLAY": "1"
        },
        "disable_environment": {
            "DISABLE_PHANTOM_OVERLAY": "1"
        }
    }
}
EOF

# Migração: remove a instalação antiga no diretório plural (nunca era carregada).
OLD_DIR="${HOME}/.local/share/vulkan/explicit_layers.d"
if [ -f "${OLD_DIR}/phantom_vulkan_layer.json" ]; then
    rm -f "${OLD_DIR}/phantom_vulkan_layer.json"
    rmdir "${OLD_DIR}" 2>/dev/null || true
fi

echo "Camada instalada em: ${MANIFEST}"
echo ""
echo "=== Como usar ==="
echo "A camada é IMPLÍCITA: todo processo Vulkan a carrega, mas só publica telemetria"
echo "com ENABLE_PHANTOM_OVERLAY=1 (gate por jogo)."
echo ""
echo "Steam (Opções de inicialização do jogo):"
echo "  ENABLE_PHANTOM_OVERLAY=1 %command%"
echo ""
echo "Lutris / Heroic / terminal:"
echo "  ENABLE_PHANTOM_OVERLAY=1 ./game"
echo ""
echo "Desativar por sessão (mesmo com o enable no jogo):"
echo "  DISABLE_PHANTOM_OVERLAY=1 ./game"
echo ""
echo "Remover: rm -f '${MANIFEST}' '${LIB_PATH}'"
echo "==================================="
