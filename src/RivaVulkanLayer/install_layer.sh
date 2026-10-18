#!/usr/bin/env bash
set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
BUILD_DIR="${SCRIPT_DIR}/build"
VULKAN_LAYER_DIR="${HOME}/.local/share/vulkan/explicit_layers.d"
LIB_DIR="${HOME}/.local/lib"

echo "=== Compilando RivaVulkanLayer ==="
cmake -B "${BUILD_DIR}" -S "${SCRIPT_DIR}" -DCMAKE_BUILD_TYPE=Release
cmake --build "${BUILD_DIR}" -j"$(nproc)"

echo "=== Instalando camada Vulkan para o usuário ==="
mkdir -p "${VULKAN_LAYER_DIR}"
mkdir -p "${LIB_DIR}"

cp "${BUILD_DIR}/libVkLayer_riva_monitor.so" "${LIB_DIR}/libVkLayer_riva_monitor.so"

# Gerar arquivo de manifesto com o caminho absoluto correto
cat <<EOF > "${VULKAN_LAYER_DIR}/riva_vulkan_layer.json"
{
    "file_format_version": "1.0.0",
    "layer": {
        "name": "VK_LAYER_RIVA_monitor",
        "type": "GLOBAL",
        "library_path": "${LIB_DIR}/libVkLayer_riva_monitor.so",
        "api_version": "1.3.0",
        "implementation_version": "1",
        "description": "RivaShopee Native Vulkan Frametime & FPS Telemetry Layer",
        "functions": {
            "vkGetInstanceProcAddr": "Riva_vkGetInstanceProcAddr",
            "vkGetDeviceProcAddr": "Riva_vkGetDeviceProcAddr"
        },
        "enable_environment": {
            "ENABLE_RIVA_OVERLAY": "1"
        }
    }
}
EOF

echo "Camada instalada em: ${VULKAN_LAYER_DIR}/riva_vulkan_layer.json"
echo ""
echo "=== Como usar ==="
echo "Para ativar em qualquer jogo individual (ex: Steam opções de inicialização):"
echo "  ENABLE_RIVA_OVERLAY=1 %command%"
echo "ou:"
echo "  VK_INSTANCE_LAYERS=VK_LAYER_RIVA_monitor %command%"
echo ""
echo "Para rodar com um jogo nativo ou Proton via terminal:"
echo "  ENABLE_RIVA_OVERLAY=1 ./game"
echo "==================================================="
