#pragma once

#if defined(_WIN32)
    #ifdef RIVASENSORLIB_EXPORTS
        #define RIVA_API __declspec(dllexport)
    #else
        #define RIVA_API __declspec(dllimport)
    #endif
#else
    #define RIVA_API __attribute__((visibility("default")))
#endif

// Layout deve ser idêntico ao NativeSensorSnapshot em Native/SensorBridge.cs
// (16 campos double, ordem fixa, sem padding).
struct NativeSensorSnapshot
{
    double CpuUsage;
    double CpuTemp;
    double CpuClock;
    double CpuVoltage;
    double CpuPower;
    double GpuUsage;
    double GpuTemp;
    double GpuCoreClock;
    double GpuMemoryClock;
    double GpuVoltage;
    double GpuMemoryUsed;
    double GpuMemoryTotal;
    double GpuPower;
    double RamUsage;
    double RamTotal;
    double FanSpeedRpm;
};

static_assert(sizeof(NativeSensorSnapshot) == 16 * sizeof(double),
              "NativeSensorSnapshot deve ter 128 bytes (compatível com o C#)");

#define RIVA_SENSOR_COUNT 16

extern "C"
{
    RIVA_API int RivaSensor_Init();
    RIVA_API int RivaSensor_ReadSnapshot(NativeSensorSnapshot* snapshot);
    RIVA_API int RivaSensor_GetSensorCount();
    RIVA_API int RivaSensor_GetSensorName(int index, char* buffer, int bufferSize);
    RIVA_API int RivaSensor_GetSensorValue(int index, double* value);
    RIVA_API void RivaSensor_Shutdown();
}
