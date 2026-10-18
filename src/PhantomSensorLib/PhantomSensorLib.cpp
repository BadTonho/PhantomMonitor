#include "RivaSensorLib.h"
#include <atomic>
#include <cstring>

static std::atomic<bool> g_initialized{false};

// Stub: ainda não há leitura real de hardware; os valores são zerados.
// A leitura real de sensores é feita em C# via LibreHardwareMonitor.

int RivaSensor_Init()
{
    g_initialized = true;
    return 0;
}

int RivaSensor_ReadSnapshot(NativeSensorSnapshot* snapshot)
{
    if (!g_initialized || !snapshot)
        return -1;

    std::memset(snapshot, 0, sizeof(NativeSensorSnapshot));
    return 0;
}

int RivaSensor_GetSensorCount()
{
    return RIVA_SENSOR_COUNT;
}

int RivaSensor_GetSensorName(int index, char* buffer, int bufferSize)
{
    if (!g_initialized || !buffer || bufferSize <= 0)
        return -1;

    static const char* names[RIVA_SENSOR_COUNT] = {
        "CPU Usage", "CPU Temperature", "CPU Clock", "CPU Voltage",
        "CPU Power", "GPU Usage", "GPU Temperature", "GPU Core Clock",
        "GPU Memory Clock", "GPU Voltage", "GPU Memory Used",
        "GPU Memory Total", "GPU Power", "RAM Usage", "RAM Total",
        "Fan Speed"
    };

    if (index < 0 || index >= RIVA_SENSOR_COUNT)
        return -1;

    std::strncpy(buffer, names[index], static_cast<size_t>(bufferSize) - 1);
    buffer[bufferSize - 1] = '\0';
    return 0;
}

int RivaSensor_GetSensorValue(int index, double* value)
{
    if (!g_initialized || !value)
        return -1;

    if (index < 0 || index >= RIVA_SENSOR_COUNT)
        return -1;

    *value = 0.0;
    return 0;
}

void RivaSensor_Shutdown()
{
    g_initialized = false;
}
