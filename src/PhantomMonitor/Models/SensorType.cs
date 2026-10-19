namespace PhantomMonitor.Models;

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
    GpuMemoryUsed,
    GpuMemoryTotal,
    GpuTemperature,
    GpuHotSpot,
    GpuCoreClock,
    GpuMemoryClock,
    GpuVoltage,
    GpuFan,
    GpuPower,
    RamUsage,
    RamAvailable,
    RamUsed,
    RamTotal,
    DiskUsage,
    DiskRead,
    DiskWrite,
    NetworkUp,
    NetworkDown,
    MotherboardTemp,
    FanSpeed,
    Fps,
    FrametimeMs,
    FpsAvg,
    FpsMin,
    Fps1Low,
    Fps01Low,
    FpsMax,

    // ---- Séries adicionais (todas também aceitam instâncias "#i") ----

    /// <summary>Temperatura por disco/NVMe (0 = indisponível).</summary>
    DiskTemperature,

    /// <summary>Saúde do SSD/NVMe em % (100 = saudável; derivado do SMART "Percentage Used").</summary>
    DiskHealth,

    /// <summary>Total lido do disco em GB desde a fabricação (SMART "Data Read").</summary>
    DiskTotalRead,

    /// <summary>Total gravado no disco em GB desde a fabricação (SMART "Data Written").</summary>
    DiskTotalWritten,

    /// <summary>Tensão de rail da placa-mãe (+12V, +5V, +3.3V, VCore...); instância por rail.</summary>
    RailVoltage,

    /// <summary>Temperatura de zona VRM; instância por sensor encontrado.</summary>
    VrmTemperature,

    /// <summary>Temperatura de módulo de RAM (poucas placas/EC expõem).</summary>
    RamTemperature,

    /// <summary>Total de bytes enviados na sessão atual (somatório de todas as interfaces).</summary>
    NetworkUpTotal,

    /// <summary>Total de bytes recebidos na sessão atual.</summary>
    NetworkDownTotal,

    /// <summary>Carga remanescente da bateria em % (100 = cheia; notebooks/UPs).</summary>
    BatteryLevel,

    /// <summary>Tensão da célula/células da bateria em V.</summary>
    BatteryVoltage,

    /// <summary>Potência da bateria em W: positivo consumindo (descarregando), negativo carregando.</summary>
    BatteryPower,

    /// <summary>Desgaste da bateria em % (1 − capacidade cheia/capacidade de projeto) × 100.</summary>
    BatteryWearLevel,

    /// <summary>Fonte AC conectada (1 = na tomada/carregando disponível, 0 = na bateria).</summary>
    BatteryAcPower,

    /// <summary>Ocupação da GPU em % (coluna "GpuBusy%"/"GPU Busy" do PresentMon; 0 = indisponível).</summary>
    GpuBusy,

    /// <summary>Custo de CPU da pilha de present em % (coluna "CpuBusy%"; 0 = indisponível).</summary>
    CpuBusy,

    /// <summary>Tempo de CPU ociosa esperando present em % (coluna "CpuWait%"; 0 = indisponível).</summary>
    CpuWait,

    /// <summary>Latência de present em ms (coluna "MsUntilDisplayed"; 0 = indisponível).</summary>
    PresentLatency,

    /// <summary>Percentil 1 de FPS da janela de estatísticas (bruto, da mesma janela de 60 s do Avg/Min).</summary>
    FpsP1,

    /// <summary>Percentil 99 de FPS da janela de estatísticas.</summary>
    FpsP99,

    /// <summary>Percentil 1 de frametime (ms) da janela de estatísticas.</summary>
    FrametimeP1,

    /// <summary>Percentil 99 de frametime (ms) da janela de estatísticas.</summary>
    FrametimeP99
}
