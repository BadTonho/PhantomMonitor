#pragma once

#ifdef RIVASENSORLIB_EXPORTS
#define RIVA_API __declspec(dllexport)
#else
#define RIVA_API __declspec(dllimport)
#endif

extern "C"
{
    /// <summary>
    /// Initialize the sensor library.
    /// </summary>
    /// <returns>0 on success, non-zero error code on failure.</returns>
    RIVA_API int RivaSensor_Init();

    /// <summary>
    /// Read all sensor values into a snapshot struct.
    /// </summary>
    /// <param name="snapshot">Pointer to a NativeSensorSnapshot struct to fill.</param>
    /// <returns>0 on success, non-zero on error.</returns>
    RIVA_API int RivaSensor_ReadSnapshot(struct NativeSensorSnapshot* snapshot);

    /// <summary>
    /// Get the total number of available sensors.
    /// </summary>
    RIVA_API int RivaSensor_GetSensorCount();

    /// <summary>
    /// Get the name of a sensor by index.
    /// </summary>
    /// <param name="index">Sensor index (0-based).</param>
    /// <param name="buffer">Output buffer for the name string.</param>
    /// <param name="bufferSize">Size of buffer in bytes.</param>
    /// <returns>0 on success, non-zero on error.</returns>
    RIVA_API int RivaSensor_GetSensorName(int index, char* buffer, int bufferSize);

    /// <summary>
    /// Get the value of a sensor by index.
    /// </summary>
    /// <param name="index">Sensor index (0-based).</param>
    /// <param name="value">Output double for the sensor value.</param>
    /// <returns>0 on success, non-zero on error.</returns>
    RIVA_API int RivaSensor_GetSensorValue(int index, double* value);

    /// <summary>
    /// Shutdown the sensor library and free resources.
    /// </summary>
    RIVA_API void RivaSensor_Shutdown();
}

/// <summary>
/// Snapshot of all primary sensor values.
/// </summary>
struct NativeSensorSnapshot
{
    double CpuUsage;
    double CpuTemp;
    double GpuUsage;
    double GpuTemp;
    double GpuMemoryUsed;
    double GpuMemoryTotal;
    double RamUsage;
    double RamTotal;
};
