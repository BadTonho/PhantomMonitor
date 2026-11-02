using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Collections.ObjectModel;
using System.Threading;
using Avalonia.Threading;
using PhantomMonitor.App.Services;
using PhantomMonitor.Models;
using PhantomMonitor.Services;
using PhantomMonitor.Services.Telemetry;


namespace PhantomMonitor.App.ViewModels;

// Pipeline de snapshots: recepção do serviço, coalescência na UI e atualização dos textos.
public sealed partial class MainWindowViewModel
{
    private void AttachSensorService(ISensorService service)
    {
        _sensorUpdatedHandler = OnSensorData;
        service.SensorUpdated += _sensorUpdatedHandler;
    }

    private void DetachSensorService(ISensorService service)
    {
        if (_sensorUpdatedHandler is null) return;
        service.SensorUpdated -= _sensorUpdatedHandler;
        _sensorUpdatedHandler = null;
    }

    /// <summary>Snapshot aguardando drain na UI; sempre mantém o mais recente.</summary>
    private volatile SensorData? _pendingUiSnapshot;

    /// <summary>1 = existe um post do dispatcher em voo (ou a prestes a executar).</summary>
    private int _uiPostActive;

    /// <summary>
    /// Chegada de snapshot da thread do timer do serviço:
    /// 1) exportador HTTP (atribuição de referência, barato),
    /// 2) loggers/CSV/JSON/journal na thread dedicada (FIFO completo),
    /// 3) coalescência de UI.
    /// </summary>
    private void OnSensorData(SensorData data)
    {
        _telemetryHttpExporter?.Publish(data);
        _snapshotWriter?.Write(data);

        // Coalescência (antes: um Post por snapshot, sem limite). Enquanto um post
        // está na fila do dispatcher, snapshots novos apenas substituem o payload
        // pendente. Se a UI engasgar (jogo rodando), N snapshots acumulados viravam
        // N reconstruções completas de inventário/overlay para dados velhos; aqui
        // o drain sempre processa o mais recente (um único post re-disparado).
        _pendingUiSnapshot = data;
        if (Interlocked.CompareExchange(ref _uiPostActive, 1, 0) == 0)
        {
            Dispatcher.UIThread.Post(() =>
            {
                _uiPostActive = 0;
                var latest = _pendingUiSnapshot;
                if (_disposed || latest is null) return;
                UpdateSnapshot(latest);
            });
        }
    }

    private void ResetDisplayedSession()
    {
        _cpuHistory.Clear();
        _gpuHistory.Clear();
        _ramHistory.Clear();
        CpuSamples = [];
        GpuSamples = [];
        RamSamples = [];
        FrametimeSamples = [];
    }

    private void RaiseRunState()
    {
        Raise(nameof(IsRunning));
        Raise(nameof(IsPaused));
        Raise(nameof(CanStart));
        Raise(nameof(CanStop));
        Raise(nameof(CanPause));
        Raise(nameof(PauseButtonText));
    }

    private void UpdateSnapshot(SensorData data)
    {
        if (_disposed || !_started) return;

        _latestSensorData = data;
        UpdateOverlayHardwareDevices(data);
        if (IsDiagnostics && (SelectedDiagnosticSection is "errors" or "sensors"))
            RefreshDiagnosticSensorState(data);
        ReconcileInventoryGroups(SensorInventoryBuilder.Plan(data, _inventoryAggregateStats));
        UpdateInventorySession(data);
        UpdateOverlayRows(data);
        RefreshOverlaySeriesOptions();
        RefreshSensorTuningSeriesOptions();
        RefreshAlertEditor();
        HasInventorySnapshot = true;
        Raise(nameof(IsWaitingForInventorySnapshot));
        var availableCount = InventoryGroups.Sum(group => group.Rows.Count(row => row.IsAvailable));
        var unavailableCount = InventoryGroups.Sum(group => group.Rows.Count(row => !row.IsAvailable));
        InventorySummaryText = _inventoryHardwareNodeCount > 0 || _inventoryMonitorCount > 0
            ? $"{_inventoryHardwareNodeCount} nós de hardware · {_inventoryMonitorCount} monitor(es) · {availableCount} séries disponíveis · {unavailableCount} tipos comuns indisponíveis"
            : $"{availableCount} séries disponíveis · {unavailableCount} tipos comuns indisponíveis";
        InventoryUpdatedText = $"Atualizado às {data.Timestamp.ToLocalTime():HH:mm:ss}";

        CpuUsageText = Percent(data, SensorType.CpuTotal);
        CpuTempText = Temperature(data, SensorType.CpuTemperature);
        CpuClockText = Clock(data, SensorType.CpuClock);
        GpuUsageText = Percent(data, SensorType.GpuCore);
        GpuTempText = Temperature(data, SensorType.GpuTemperature);
        GpuMemoryText = MemoryPair(data, SensorType.GpuMemoryUsed, SensorType.GpuMemoryTotal);
        RamUsageText = Percent(data, SensorType.RamUsage);
        RamDetailText = MemoryPair(data, SensorType.RamUsed, SensorType.RamTotal, bytesAreGb: true);
        DiskReadText = Rate(data, SensorType.DiskRead);
        DiskWriteText = Rate(data, SensorType.DiskWrite);
        NetworkDownText = Rate(data, SensorType.NetworkDown);
        NetworkUpText = Rate(data, SensorType.NetworkUp);

        var fps = data.GetValue(SensorType.Fps);
        var frametime = data.GetValue(SensorType.FrametimeMs);
        HasFpsData = Has(data, SensorType.Fps);
        FpsText = HasFpsData ? $"{fps:F0}" : "—";
        FrametimeText = Has(data, SensorType.FrametimeMs) ? $"{frametime:F2} ms" : "—";
        FpsStatsText = Has(data, SensorType.Fps)
            ? $"Mín {data.FpsMin:F0}   Média {data.FpsAvg:F0}   1% low {data.Fps1Low:F0}   0,1% low {data.Fps01Low:F0}"
            : "Mín —   Média —   1% low —   0,1% low —";
        FpsHint = Has(data, SensorType.Fps)
            ? "Dados de FPS disponíveis."
            : "Aguardando dados de FPS de um jogo.";

        AddHistory(_cpuHistory, data.GetValue(SensorType.CpuTotal), Has(data, SensorType.CpuTotal));
        AddHistory(_gpuHistory, data.GetValue(SensorType.GpuCore), Has(data, SensorType.GpuCore));
        AddHistory(_ramHistory, data.GetValue(SensorType.RamUsage), Has(data, SensorType.RamUsage));
        CpuSamples = _cpuHistory.ToArray();
        GpuSamples = _gpuHistory.ToArray();
        RamSamples = _ramHistory.ToArray();
        FrametimeSamples = data.FrametimeHistory.Select(x => (double)x).ToArray();
        UpdateDashboardExtras(data); // detalhes/cores por faixa/placa-mãe/processos (Overview partial)
        LastUpdateText = $"Atualizado às {data.Timestamp.ToLocalTime():HH:mm:ss}";

        // Loggers/HTTP saíram daqui: rodam na thread dedicada a partir de
        // OnSensorData (a UI não grava arquivo serializado por tick).

        foreach (var alert in _alertEvaluator.Evaluate(data))
        {
            // Ações de sistema por SO (som/script/shutdown com guard) na F1.5;
            // toast + notificação de desktop continuam no RecordAlert.
            ExecuteAlertActions(alert);
            RecordAlert(alert, data.Timestamp);
        }
    }

}
