using System.Runtime.InteropServices;
using System.Text;
using System.Globalization;
using PhantomMonitor.Models;
using PhantomMonitor.Services;

namespace PhantomMonitor.Services.Linux;

/// <summary>
/// Leitura opcional de métricas NVIDIA por NVML. A biblioteca vem com o driver
/// proprietário e é carregada dinamicamente para manter o Core utilizável sem NVIDIA.
/// </summary>
internal sealed class NvidiaGpuSensorReader : IDisposable
{
    private const int NvmlSuccess = 0;
    private const uint GpuTemperatureSensor = 0;
    private const double BytesPerMegabyte = 1024d * 1024d;

    private IntPtr _library;
    private bool _initialized;
    private NvmlShutdown? _shutdown;
    private DeviceGetHandleByPciBusId? _getHandleByPciBusId;
    private DeviceGetHandleByIndex? _getHandleByIndex;
    private DeviceGetName? _getName;
    private DeviceGetUtilizationRates? _getUtilizationRates;
    private DeviceGetMemoryInfo? _getMemoryInfo;
    private DeviceGetTemperature? _getTemperature;
    private string _status = "NVML ainda não inicializada.";

    public string Status => _status;

    public NvidiaGpuSensorReader()
    {
        try
        {
            if (!NativeLibrary.TryLoad("libnvidia-ml.so.1", out _library))
            {
                _status = "libnvidia-ml.so.1 não encontrada; métricas NVML indisponíveis.";
                return;
            }

            var initialize = Load<NvmlInitialize>("nvmlInit_v2", "nvmlInit");
            _shutdown = Load<NvmlShutdown>("nvmlShutdown");
            _getHandleByPciBusId = Load<DeviceGetHandleByPciBusId>(
                "nvmlDeviceGetHandleByPciBusId_v2", "nvmlDeviceGetHandleByPciBusId");
            _getHandleByIndex = Load<DeviceGetHandleByIndex>(
                "nvmlDeviceGetHandleByIndex_v2", "nvmlDeviceGetHandleByIndex");
            _getName = Load<DeviceGetName>("nvmlDeviceGetName");
            _getUtilizationRates = Load<DeviceGetUtilizationRates>("nvmlDeviceGetUtilizationRates");
            _getMemoryInfo = Load<DeviceGetMemoryInfo>("nvmlDeviceGetMemoryInfo");
            _getTemperature = Load<DeviceGetTemperature>("nvmlDeviceGetTemperature");

            if (initialize is null || _shutdown is null ||
                _getHandleByPciBusId is null || _getHandleByIndex is null ||
                _getName is null || _getUtilizationRates is null ||
                _getMemoryInfo is null || _getTemperature is null)
            {
                _status = "A biblioteca NVML foi carregada, mas faltam funções necessárias.";
                Dispose();
                return;
            }

            if (initialize() == NvmlSuccess)
            {
                _initialized = true;
                _status = "NVML inicializada.";
            }
            else
            {
                _status = "A biblioteca NVML foi encontrada, mas não conseguiu inicializar.";
                Dispose();
            }
        }
        catch (Exception ex)
        {
            _status = $"Falha ao inicializar NVML: {ex.GetType().Name}: {ex.Message}";
            ErrorLog.Write("NvidiaGpuSensorReader.Initialize", ex);
            Dispose();
        }
    }

    public bool TryRead(string? pciBusId, uint fallbackIndex, out NvidiaGpuReading reading)
    {
        reading = default;
        if (!_initialized || _getHandleByPciBusId is null || _getHandleByIndex is null)
            return false;

        try
        {
            IntPtr device = IntPtr.Zero;
            var found = false;

            if (!string.IsNullOrWhiteSpace(pciBusId))
            {
                foreach (var candidate in GetPciBusIdCandidates(pciBusId))
                {
                    if (_getHandleByPciBusId(candidate, out device) == NvmlSuccess)
                    {
                        found = true;
                        break;
                    }
                }
            }

            // Se o driver não publicou PCI_SLOT_NAME, mantém o suporte por ordem do DRM.
            if (!found && _getHandleByIndex(fallbackIndex, out device) == NvmlSuccess)
                found = true;

            if (!found || device == IntPtr.Zero)
            {
                _status = $"NVML ativa, mas não encontrou a GPU PCI {pciBusId ?? fallbackIndex.ToString(CultureInfo.InvariantCulture)}.";
                return false;
            }

            var label = $"NVIDIA GPU #{fallbackIndex + 1}";
            var nameBuffer = new byte[128];
            if (_getName is not null && _getName(device, nameBuffer, (uint)nameBuffer.Length) == NvmlSuccess)
            {
                var terminator = Array.IndexOf(nameBuffer, (byte)0);
                var length = terminator >= 0 ? terminator : nameBuffer.Length;
                var model = Encoding.UTF8.GetString(nameBuffer, 0, length).Trim();
                if (!string.IsNullOrWhiteSpace(model))
                    label = model;
            }

            double? utilization = null;
            if (_getUtilizationRates is not null &&
                _getUtilizationRates(device, out var utilizationInfo) == NvmlSuccess &&
                utilizationInfo.Gpu <= 100)
            {
                utilization = utilizationInfo.Gpu;
            }

            double? memoryUsedMb = null;
            double? memoryTotalMb = null;
            if (_getMemoryInfo is not null && _getMemoryInfo(device, out var memory) == NvmlSuccess)
            {
                memoryUsedMb = memory.Used / BytesPerMegabyte;
                memoryTotalMb = memory.Total / BytesPerMegabyte;
            }

            double? temperatureCelsius = null;
            if (_getTemperature is not null &&
                _getTemperature(device, GpuTemperatureSensor, out var temperature) == NvmlSuccess)
            {
                temperatureCelsius = temperature;
            }

            reading = new NvidiaGpuReading(label, utilization, memoryUsedMb, memoryTotalMb, temperatureCelsius);
            _status = $"NVML ativa; {label}: utilização {(utilization.HasValue ? "ok" : "não reportada")}, " +
                      $"VRAM {(memoryUsedMb.HasValue && memoryTotalMb.HasValue ? "ok" : "não reportada")}, " +
                      $"temperatura {(temperatureCelsius.HasValue ? "ok" : "não reportada")}.";
            return true;
        }
        catch (Exception ex)
        {
            _status = $"Falha ao consultar NVML: {ex.GetType().Name}: {ex.Message}";
            ErrorLog.Write("NvidiaGpuSensorReader.Read", ex);
            return false;
        }
    }

    private static IEnumerable<string> GetPciBusIdCandidates(string value)
    {
        var busId = value.Trim();
        var firstColon = busId.IndexOf(':');
        if (firstColon > 0 && firstColon <= 4)
        {
            var domain = busId[..firstColon];
            if (domain.All(Uri.IsHexDigit))
                yield return domain.PadLeft(8, '0') + busId[firstColon..];
        }

        yield return busId;
    }

    private T? Load<T>(params string[] names) where T : Delegate
    {
        foreach (var name in names)
        {
            if (NativeLibrary.TryGetExport(_library, name, out var address))
                return Marshal.GetDelegateForFunctionPointer<T>(address);
        }

        return null;
    }

    public void Dispose()
    {
        if (_initialized)
        {
            try { _shutdown?.Invoke(); }
            catch { /* Liberação best-effort. */ }
            _initialized = false;
        }

        if (_library != IntPtr.Zero)
        {
            try { NativeLibrary.Free(_library); }
            catch { /* Liberação best-effort. */ }
            _library = IntPtr.Zero;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NvmlUtilization
    {
        public uint Gpu;
        public uint Memory;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NvmlMemory
    {
        public ulong Total;
        public ulong Free;
        public ulong Used;
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int NvmlInitialize();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int NvmlShutdown();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int DeviceGetHandleByPciBusId(
        [MarshalAs(UnmanagedType.LPStr)] string pciBusId,
        out IntPtr device);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int DeviceGetHandleByIndex(uint index, out IntPtr device);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int DeviceGetName(IntPtr device, [Out] byte[] name, uint length);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int DeviceGetUtilizationRates(IntPtr device, out NvmlUtilization utilization);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int DeviceGetMemoryInfo(IntPtr device, out NvmlMemory memory);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int DeviceGetTemperature(IntPtr device, uint sensorType, out uint temperature);
}

internal readonly record struct NvidiaGpuReading(
    string Label,
    double? UtilizationPercent,
    double? MemoryUsedMb,
    double? MemoryTotalMb,
    double? TemperatureCelsius);
