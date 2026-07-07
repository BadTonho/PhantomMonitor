#include "RivaSensorLib.h"
#include <cstring>

// Placeholder implementation
// Will be replaced with direct hardware access (MSR, PCI, NVML, WinRing0)

static bool g_initialized = false;

int RivaSensor_Init()
{
    // TODO: Initialize WinRing0, NVML, etc.
    g_initialized = true;
    return 0;
}

int RivaSensor_ReadSnapshot(struct NativeSensorSnapshot* snapshot)
{
    if (!g_initialized || !snapshot)
        return -1;

    // TODO: Read actual hardware values via direct access
    snapshot->CpuUsage = 0.0;
    snapshot->CpuTemp = 0.0;
    snapshot->GpuUsage = 0.0;
    snapshot->GpuTemp = 0.0;
    snapshot->GpuMemoryUsed = 0.0;
    snapshot->GpuMemoryTotal = 0.0;
    snapshot->RamUsage = 0.0;
    snapshot->RamTotal = 0.0;

    return 0;
}

int RivaSensor_GetSensorCount()
{
    // TODO: Return actual sensor count
    return 8;
}

int RivaSensor_GetSensorName(int index, char* buffer, int bufferSize)
{
    if (!g_initialized || !buffer || bufferSize <= 0)
        return -1;

    static const char* names[] = {
        "CPU Usage",
        "CPU Temperature",
        "GPU Usage",
        "GPU Temperature",
        "GPU Memory Used",
        "GPU Memory Total",
        "RAM Usage",
        "RAM Total"
    };

    if (index < 0 || index >= 8)
        return -1;

    std::strncpy(buffer, names[index], static_cast<size_t>(bufferSize) - 1);
    buffer[bufferSize - 1] = '\0';
    return 0;
}

int RivaSensor_GetSensorValue(int index, double* value)
{
    if (!g_initialized || !value)
        return -1;

    // TODO: Read actual value
    *value = 0.0;
    return 0;
}

void RivaSensor_Shutdown()
{
    g_initialized = false;
    // TODO: Cleanup WinRing0, NVML, etc.
}
