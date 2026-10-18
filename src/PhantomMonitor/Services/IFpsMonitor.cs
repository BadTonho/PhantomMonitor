namespace PhantomMonitor.Services;

/// <summary>
/// Contrato unificado para monitoramento de taxas de quadros (FPS, frametime, 1% low e 0.1% low)
/// em múltiplos sistemas operacionais (Windows via PresentMon/ETW, Linux via PhantomVulkanLayer).
/// </summary>
public interface IFpsMonitor : IDisposable
{
    float Fps { get; }
    float FrametimeMs { get; }
    float FpsAvg { get; }
    float FpsMin { get; }
    float Fps1Low { get; }
    float Fps01Low { get; }
    float FpsMax { get; }
    bool HasData { get; }
    FrametimeBuffer FrametimeHistory { get; }
    string DebugInfo { get; }

    void Start();
    void Stop();
    void Reset();
}
