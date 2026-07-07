using System.Runtime.InteropServices;

namespace RivaMonitor.Native;

public enum NativeSensorType
{
    CpuUsage = 0,
    CpuTemp = 1,
    GpuUsage = 2,
    GpuTemp = 3,
    GpuMemoryUsed = 4,
    RamUsage = 5,
    RamTotal = 6
}

[StructLayout(LayoutKind.Sequential)]
public struct NativeSensorSnapshot
{
    public double CpuUsage;
    public double CpuTemp;
    public double GpuUsage;
    public double GpuTemp;
    public double GpuMemoryUsed;
    public double GpuMemoryTotal;
    public double RamUsage;
    public double RamTotal;
}

public static class SensorBridge
{
    private const string DllName = "RivaSensorLib.dll";

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int RivaSensor_Init();

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int RivaSensor_ReadSnapshot(ref NativeSensorSnapshot snapshot);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int RivaSensor_GetSensorCount();

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int RivaSensor_GetSensorName(int index, IntPtr buffer, int bufferSize);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int RivaSensor_GetSensorValue(int index, ref double value);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void RivaSensor_Shutdown();

    public static bool IsAvailable { get; private set; }

    public static bool TryLoad()
    {
        try
        {
            var result = RivaSensor_Init();
            IsAvailable = result == 0;
            return IsAvailable;
        }
        catch (DllNotFoundException)
        {
            IsAvailable = false;
            return false;
        }
    }

    public static NativeSensorSnapshot? ReadSnapshot()
    {
        if (!IsAvailable) return null;
        var snap = new NativeSensorSnapshot();
        var result = RivaSensor_ReadSnapshot(ref snap);
        return result == 0 ? snap : null;
    }
}
