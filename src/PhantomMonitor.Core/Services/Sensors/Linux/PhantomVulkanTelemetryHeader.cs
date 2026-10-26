using System.Runtime.InteropServices;

namespace PhantomMonitor.Services.Linux;

/// <summary>Legacy 100-byte prefix retained in both Vulkan telemetry protocol versions.</summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct PhantomVulkanTelemetryHeader
{
    public const uint ExpectedMagic = 0x50484E54;
    public uint Magic;
    public uint Version;
    public uint Pid;
    public ulong FrameCount;
    public ulong TimestampNs;
    public float FrametimeMs;
    public float InstantFps;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
    public string ProcessName;
}
