using System.Runtime.InteropServices;
using System.Text;

namespace PhantomMonitor.Native;

/// <summary>Índices de sensores expostos por PhantomSensor_GetSensorValue (mesma ordem do C++).</summary>
public enum NativeSensorType
{
    CpuUsage = 0,
    CpuTemp = 1,
    CpuClock = 2,
    CpuVoltage = 3,
    CpuPower = 4,
    GpuUsage = 5,
    GpuTemp = 6,
    GpuCoreClock = 7,
    GpuMemoryClock = 8,
    GpuVoltage = 9,
    GpuMemoryUsed = 10,
    GpuMemoryTotal = 11,
    GpuPower = 12,
    RamUsage = 13,
    RamTotal = 14,
    FanSpeedRpm = 15
}

/// <summary>
/// Layout idêntico a struct NativeSensorSnapshot em PhantomSensorLib.h (16 doubles = 128 bytes).
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct NativeSensorSnapshot
{
    public double CpuUsage;
    public double CpuTemp;
    public double CpuClock;
    public double CpuVoltage;
    public double CpuPower;
    public double GpuUsage;
    public double GpuTemp;
    public double GpuCoreClock;
    public double GpuMemoryClock;
    public double GpuVoltage;
    public double GpuMemoryUsed;
    public double GpuMemoryTotal;
    public double GpuPower;
    public double RamUsage;
    public double RamTotal;
    public double FanSpeedRpm;
}

public static class SensorBridge
{
    private const string DllName = "PhantomSensorLib.dll";
    private const int ExpectedSnapshotSize = 16 * sizeof(double);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int PhantomSensor_Init();

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int PhantomSensor_ReadSnapshot(ref NativeSensorSnapshot snapshot);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int PhantomSensor_GetSensorCount();

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int PhantomSensor_GetSensorName(int index, IntPtr buffer, int bufferSize);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int PhantomSensor_GetSensorValue(int index, ref double value);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void PhantomSensor_Shutdown();

    public static bool IsAvailable { get; private set; }

    public static bool TryLoad()
    {
        try
        {
            if (Marshal.SizeOf<NativeSensorSnapshot>() != ExpectedSnapshotSize)
            {
                IsAvailable = false;
                return false;
            }

            IsAvailable = PhantomSensor_Init() == 0;
            return IsAvailable;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            IsAvailable = false;
            return false;
        }
    }

    public static NativeSensorSnapshot? ReadSnapshot()
    {
        if (!IsAvailable) return null;
        var snap = new NativeSensorSnapshot();
        return PhantomSensor_ReadSnapshot(ref snap) == 0 ? snap : null;
    }

    public static string? GetSensorName(int index)
    {
        if (!IsAvailable) return null;
        var ptr = Marshal.AllocHGlobal(128);
        try
        {
            if (PhantomSensor_GetSensorName(index, ptr, 128) != 0) return null;
            return Marshal.PtrToStringAnsi(ptr);
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
    }

    public static void Unload()
    {
        if (!IsAvailable) return;
        try { PhantomSensor_Shutdown(); } catch { }
        IsAvailable = false;
    }
}
