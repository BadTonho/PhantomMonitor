#pragma once

#ifdef RIVASENSORLIB_EXPORTS
#define RIVA_API __declspec(dllexport)
#else
#define RIVA_API __declspec(dllimport)
#endif

extern "C"
{
    RIVA_API int RivaSensor_Init();
    RIVA_API int RivaSensor_ReadSnapshot(struct NativeSensorSnapshot* snapshot);
    RIVA_API int RivaSensor_GetSensorCount();
    RIVA_API int RivaSensor_GetSensorName(int index, char* buffer, int bufferSize);
    RIVA_API int RivaSensor_GetSensorValue(int index, double* value);
    RIVA_API void RivaSensor_Shutdown();
}

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
