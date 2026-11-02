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

// Propriedades de binding do dashboard: leituras, FPS, inventário, diagnóstico, alertas e amostras.
public sealed partial class MainWindowViewModel
{
    public string StatusText
    {
        get => _statusText;
        private set
        {
            if (Set(ref _statusText, value)) OnNavigationStatusChanged();
        }
    }
    public string LastUpdateText { get => _lastUpdateText; private set => Set(ref _lastUpdateText, value); }
    public string CpuUsageText { get => _cpuUsageText; private set => Set(ref _cpuUsageText, value); }
    public string CpuTempText { get => _cpuTempText; private set => Set(ref _cpuTempText, value); }
    public string CpuClockText { get => _cpuClockText; private set => Set(ref _cpuClockText, value); }
    public string GpuUsageText { get => _gpuUsageText; private set => Set(ref _gpuUsageText, value); }
    public string GpuTempText { get => _gpuTempText; private set => Set(ref _gpuTempText, value); }
    public string GpuMemoryText { get => _gpuMemoryText; private set => Set(ref _gpuMemoryText, value); }
    public string RamUsageText { get => _ramUsageText; private set => Set(ref _ramUsageText, value); }
    public string RamDetailText { get => _ramDetailText; private set => Set(ref _ramDetailText, value); }
    public string DiskReadText { get => _diskReadText; private set => Set(ref _diskReadText, value); }
    public string DiskWriteText { get => _diskWriteText; private set => Set(ref _diskWriteText, value); }
    public string NetworkDownText { get => _networkDownText; private set => Set(ref _networkDownText, value); }
    public string NetworkUpText { get => _networkUpText; private set => Set(ref _networkUpText, value); }
    public bool HasFpsData { get => _hasFpsData; private set => Set(ref _hasFpsData, value); }
    private bool _hasFpsData;
    public string FpsText { get => _fpsText; private set => Set(ref _fpsText, value); }
    public string FrametimeText { get => _frametimeText; private set => Set(ref _frametimeText, value); }
    public string FpsStatsText { get => _fpsStatsText; private set => Set(ref _fpsStatsText, value); }
    public string FpsHint { get => _fpsHint; private set => Set(ref _fpsHint, value); }
    public string InventorySummaryText { get => _inventorySummaryText; private set => Set(ref _inventorySummaryText, value); }
    public string InventoryUpdatedText { get => _inventoryUpdatedText; private set => Set(ref _inventoryUpdatedText, value); }
    // Referência estável: a coleção notifica estruturalmente (ObservableCollection);
    // nunca é substituída, então não há mais PropertyChanged da lista por snapshot.
    public IReadOnlyList<InventoryGroupViewModel> InventoryGroups => _inventoryGroups;
    public bool HasInventorySnapshot { get => _hasInventorySnapshot; private set => Set(ref _hasInventorySnapshot, value); }
    public bool IsWaitingForInventorySnapshot => !HasInventorySnapshot;
    public bool IsOverview { get => _isOverview; private set => Set(ref _isOverview, value); }
    public bool IsInventory => !IsOverview && !IsSettings && !IsAlerts && !IsCustomSensors && !IsSensorTuning && !IsDiagnostics;
    public bool IsSettings { get => _isSettings; private set => Set(ref _isSettings, value); }
    public bool IsAlerts { get => _isAlerts; private set => Set(ref _isAlerts, value); }
    public bool IsCustomSensors { get => _isCustomSensors; private set => Set(ref _isCustomSensors, value); }
    public bool IsSensorTuning { get => _isSensorTuning; private set => Set(ref _isSensorTuning, value); }
    public bool IsDiagnostics { get => _isDiagnostics; private set => Set(ref _isDiagnostics, value); }
    public string SelectedDiagnosticSection => _selectedDiagnosticSection;
    public bool IsDiagnosticSummary => SelectedDiagnosticSection == "summary";
    public bool IsDiagnosticErrors => SelectedDiagnosticSection == "errors";
    public bool IsDiagnosticSensors => SelectedDiagnosticSection == "sensors";
    public ObservableCollection<ErrorLogEntry> DiagnosticEntries => _diagnosticEntries;
    public IReadOnlyList<string> DiagnosticSensorLines { get => _diagnosticSensorLines; private set => Set(ref _diagnosticSensorLines, value); }
    public IReadOnlyList<string> LinuxOverlayDiagnosticLines { get => _linuxOverlayDiagnosticLines; private set => Set(ref _linuxOverlayDiagnosticLines, value); }
    public bool IsLinuxOverlayDiagnosticsVisible => OperatingSystem.IsLinux() && LinuxOverlayDiagnosticLines.Count > 0;
    public IReadOnlyList<DiagnosticSensorWarningViewModel> DiagnosticSensorWarnings
    {
        get => _diagnosticSensorWarnings;
        private set
        {
            if (!Set(ref _diagnosticSensorWarnings, value)) return;
            Raise(nameof(HasDiagnosticSensorWarnings));
            Raise(nameof(DiagnosticSensorWarningsSummary));
        }
    }
    public bool HasDiagnosticSensorSnapshot
    {
        get => _hasDiagnosticSensorSnapshot;
        private set
        {
            if (!Set(ref _hasDiagnosticSensorSnapshot, value)) return;
            Raise(nameof(DiagnosticSensorWarningsSummary));
        }
    }
    public string DiagnosticsMessage { get => _diagnosticsMessage; private set => Set(ref _diagnosticsMessage, value); }
    public string ErrorLogPathText => ErrorLog.LogFilePath;
    public bool HasDiagnosticEntries => DiagnosticEntries.Count > 0;
    public bool HasDiagnosticSensorWarnings => DiagnosticSensorWarnings.Count > 0;
    public string DiagnosticSensorWarningsSummary => !HasDiagnosticSensorSnapshot
        ? "Aguardando a primeira amostra de sensores."
        : DiagnosticSensorWarnings.Any(warning => warning.IsGeneralWarning)
            ? "Nenhuma GPU enumerada pelo coletor nesta amostra."
        : HasDiagnosticSensorWarnings
            ? $"{DiagnosticSensorWarnings.Count} {(DiagnosticSensorWarnings.Count == 1 ? "métrica indisponível" : "métricas indisponíveis")} nesta amostra."
            : "Todas as métricas GPU consultadas estão disponíveis.";
    public string DiagnosticEntriesSummary => HasDiagnosticEntries
        ? $"Exibindo {DiagnosticEntries.Count} registros recentes."
        : "Nenhum erro registrado nesta execução ou no arquivo local.";
    public string DiagnosticErrorCountText => $"{DiagnosticEntries.Count} {(DiagnosticEntries.Count == 1 ? "erro recente" : "erros recentes")}";
    public string DiagnosticLatestErrorHeader => DiagnosticEntries.LastOrDefault() is { } entry
        ? $"{entry.TimestampText} · {entry.Context}"
        : "";
    public string DiagnosticLatestErrorMessage => DiagnosticEntries.LastOrDefault()?.Message ?? "";
    public ObservableCollection<AlertRuleEditorViewModel> AlertRules => _alertRules;
    public ObservableCollection<AlertHistoryEntryViewModel> AlertHistory => _alertHistory;
    public ObservableCollection<CustomSensorEditorViewModel> CustomSensorEditors => _customSensorEditors;
    public string CustomSensorsMessage { get => _customSensorsMessage; private set => Set(ref _customSensorsMessage, value); }
    public ObservableCollection<SensorTuningEditorViewModel> SensorTuningEditors => _sensorTuningEditors;
    public IReadOnlyList<string> SensorTuningSeriesOptions { get => _sensorTuningSeriesOptions; private set => Set(ref _sensorTuningSeriesOptions, value); }
    public string SensorTuningMessage { get => _sensorTuningMessage; private set => Set(ref _sensorTuningMessage, value); }
    public bool HasAlertHistory => AlertHistory.Count > 0;
    public IReadOnlyList<string> AlertSensorOptions { get => _alertSensorOptions; private set => Set(ref _alertSensorOptions, value); }
    public string AlertsCooldownText { get => _alertsCooldownText; set => Set(ref _alertsCooldownText, value); }
    public bool AlertsEnabled { get => _alertsEnabled; set => Set(ref _alertsEnabled, value); }
    public string AlertsMessage { get => _alertsMessage; private set => Set(ref _alertsMessage, value); }
    public bool AlertToastVisible { get => _alertToastVisible; private set => Set(ref _alertToastVisible, value); }
    public string AlertToastMessage { get => _alertToastMessage; private set => Set(ref _alertToastMessage, value); }
    public IReadOnlyList<double> CpuSamples { get => _cpuSamples; private set => Set(ref _cpuSamples, value); }
    public IReadOnlyList<double> GpuSamples { get => _gpuSamples; private set => Set(ref _gpuSamples, value); }
    public IReadOnlyList<double> RamSamples { get => _ramSamples; private set => Set(ref _ramSamples, value); }
    public IReadOnlyList<double> FrametimeSamples { get => _frametimeSamples; private set => Set(ref _frametimeSamples, value); }
    public bool IsRunning => _started && _sensorService.IsRunning;
    public bool IsPaused => _sensorService.Paused;
    public bool CanStart => !IsRunning;
    public bool CanStop => IsRunning;
    public bool CanPause => IsRunning;
    public string PauseButtonText => IsPaused ? "Retomar" : "Pausar";

}
