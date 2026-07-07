#include "RivaSensorLib.h"
#include <cstring>

static bool g_initialized = false;

int RivaSensor_Init()
{
    g_initialized = true;
    return 0;
}

int RivaSensor_ReadSnapshot(struct NativeSensorSnapshot* snapshot)
{
    if (!g_initialized || !snapshot)
        return -1;

    snapshot->CpuUsage = 0.0;
    snapshot->CpuTemp = 0.0;
    snapshot->CpuClock = 0.0;
    snapshot->CpuVoltage = 0.0;
    snapshot->CpuPower = 0.0;
    snapshot->GpuUsage = 0.0;
    snapshot->GpuTemp = 0.0;
    snapshot->GpuCoreClock = 0.0;
    snapshot->GpuMemoryClock = 0.0;
    snapshot->GpuVoltage = 0.0;
    snapshot->GpuMemoryUsed = 0.0;
    snapshot->GpuMemoryTotal = 0.0;
    snapshot->GpuPower = 0.0;
    snapshot->RamUsage = 0.0;
    snapshot->RamTotal = 0.0;
    snapshot->FanSpeedRpm = 0.0;

    return 0;
}

int RivaSensor_GetSensorCount()
{
    return 16;
}

int RivaSensor_GetSensorName(int index, char* buffer, int bufferSize)
{
    if (!g_initialized || !buffer || bufferSize <= 0)
        return -1;

    static const char* names[] = {
        "CPU Usage", "CPU Temperature", "CPU Clock", "CPU Voltage",
        "CPU Power", "GPU Usage", "GPU Temperature", "GPU Core Clock",
        "GPU Memory Clock", "GPU Voltage", "GPU Memory Used",
        "GPU Memory Total", "GPU Power", "RAM Usage", "RAM Total",
        "Fan Speed"
    };

    if (index < 0 || index >= 16)
        return -1;

    std::strncpy(buffer, names[index], static_cast<size_t>(bufferSize) - 1);
    buffer[bufferSize - 1] = '\0';
    return 0;
}

int RivaSensor_GetSensorValue(int index, double* value)
{
    if (!g_initialized || !value)
        return -1;

    *value = 0.0;
    return 0;
}

void RivaSensor_Shutdown()
{
    g_initialized = false;
}
