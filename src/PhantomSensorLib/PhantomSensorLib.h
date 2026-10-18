#pragma once

#if defined(_WIN32)
    #ifdef PHANTOMSENSORLIB_EXPORTS
        #define PHANTOM_API __declspec(dllexport)
    #else
        #define PHANTOM_API __declspec(dllimport)
    #endif
#else
    #define PHANTOM_API __attribute__((visibility("default")))
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

#define PHANTOM_SENSOR_COUNT 16

extern "C"
{
    PHANTOM_API int PhantomSensor_Init();
    PHANTOM_API int PhantomSensor_ReadSnapshot(NativeSensorSnapshot* snapshot);
    PHANTOM_API int PhantomSensor_GetSensorCount();
    PHANTOM_API int PhantomSensor_GetSensorName(int index, char* buffer, int bufferSize);
    PHANTOM_API int PhantomSensor_GetSensorValue(int index, double* value);
    PHANTOM_API void PhantomSensor_Shutdown();
}
