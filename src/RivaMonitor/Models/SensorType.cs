namespace RivaMonitor.Models;

public enum SensorType
{
    CpuTotal,
    CpuPerCore,
    CpuTemperature,
    CpuPower,
    CpuClock,
    CpuVoltage,
    GpuCore,
    GpuMemory,
    GpuTemperature,
    GpuHotSpot,
    GpuCoreClock,
    GpuMemoryClock,
    GpuVoltage,
    GpuFan,
    GpuPower,
    RamUsage,
    RamAvailable,
    DiskUsage,
    DiskRead,
    DiskWrite,
    NetworkUp,
    NetworkDown,
    MotherboardTemp,
    FanSpeed
}
