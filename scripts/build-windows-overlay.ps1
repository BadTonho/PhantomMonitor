<# Compiles the allowlist-only in-game overlay hook, launcher, and Vulkan layer for x86/x64. #>
param(
    [Parameter(Mandatory = $true)]
    [string]$OutDirectory,
    [string]$Generator = "Visual Studio 17 2022"
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$source = Join-Path $repoRoot "src\PhantomNativeOverlay"
$buildRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("phantom-native-overlay-" + [guid]::NewGuid().ToString("N"))

try {
    foreach ($architecture in @(
        @{ Name = "x64"; CMake = "x64" },
        @{ Name = "x86"; CMake = "Win32" }
    )) {
        $build = Join-Path $buildRoot $architecture.Name
        & cmake -S $source -B $build -G $Generator -A $architecture.CMake -DBUILD_TESTING=OFF
        if ($LASTEXITCODE -ne 0) { throw "CMake configure do hook $($architecture.Name) falhou." }
        & cmake --build $build --config Release --target PhantomOverlayHook PhantomOverlayLauncher --parallel
        if ($LASTEXITCODE -ne 0) { throw "Build do hook $($architecture.Name) falhou." }

        $destination = Join-Path $OutDirectory $architecture.Name
        New-Item -ItemType Directory -Force -Path $destination | Out-Null
        foreach ($file in @("PhantomOverlayHook.dll", "PhantomOverlayLauncher.exe")) {
            $built = Join-Path $build (Join-Path "Release" $file)
            if (-not (Test-Path $built)) { throw "Artefato nativo ausente: $built" }
            Copy-Item $built (Join-Path $destination $file) -Force
        }
        Copy-Item (Join-Path $source "third_party\imgui\LICENSE.txt") `
            (Join-Path $destination "LICENSE-Dear-ImGui.txt") -Force
        Copy-Item (Join-Path $source "third_party\minhook\LICENSE.txt") `
            (Join-Path $destination "LICENSE-MinHook.txt") -Force

        $vulkanBuild = Join-Path $buildRoot ("vulkan-" + $architecture.Name)
        $vulkanSource = Join-Path $repoRoot "src\PhantomVulkanLayer"
        & cmake -S $vulkanSource -B $vulkanBuild -G $Generator -A $architecture.CMake -DBUILD_TESTING=OFF
        if ($LASTEXITCODE -ne 0) { throw "CMake configure da camada Vulkan $($architecture.Name) falhou. Instale o Vulkan SDK." }
        & cmake --build $vulkanBuild --config Release --target VkLayer_phantom_monitor --parallel
        if ($LASTEXITCODE -ne 0) { throw "Build da camada Vulkan $($architecture.Name) falhou." }
        Copy-Item (Join-Path $vulkanBuild "Release\VkLayer_phantom_monitor.dll") `
            (Join-Path $destination "VkLayer_phantom_monitor.dll") -Force
        Copy-Item (Join-Path $vulkanBuild "phantom_vulkan_layer.json") `
            (Join-Path $destination "phantom_vulkan_layer.json") -Force
    }
}
finally {
    Remove-Item $buildRoot -Recurse -Force -ErrorAction SilentlyContinue
}
