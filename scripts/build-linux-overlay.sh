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

echo "Renderizadores Linux prontos em: ${OUTPUT_DIR}"
