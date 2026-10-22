using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Collections.ObjectModel;
using System.Threading;
using Avalonia.Threading;
using PhantomMonitor.App.Services;
using PhantomMonitor.Models;
using PhantomMonitor.Services;
using PhantomMonitor.Services.Linux;
using PhantomMonitor.Services.Telemetry;

namespace PhantomMonitor.App.ViewModels;

public sealed class MainWindowViewModel : INotifyPropertyChanged, IDisposable
{
    private const int HistoryLength = 60;
    private readonly AppConfig _config;
    private ISensorService _sensorService;
    private Action<SensorData>? _sensorUpdatedHandler;
    private readonly Queue<double> _cpuHistory = new();
    private readonly Queue<double> _gpuHistory = new();
    private readonly Queue<double> _ramHistory = new();
    private CsvLogger? _csvLogger;
    private JsonLogger? _jsonLogger;
    private JsonJournalLogger? _jsonJournalLogger;
    private TelemetryHttpExporter? _telemetryHttpExporter;
    private SensorData? _latestSensorData;
    private int? _activeTelemetryPort;
    private bool? _activeTelemetryListenAll;
    private bool _started;
    private bool _disposed;
    private string _statusText = "Iniciando monitoramento…";
    private string _lastUpdateText = "Aguardando primeira leitura";
    private string _cpuUsageText = "—";
    private string _cpuTempText = "—";
    private string _cpuClockText = "—";
    private string _gpuUsageText = "—";
    private string _gpuTempText = "—";
    private string _gpuMemoryText = "—";
    private string _ramUsageText = "—";
    private string _ramDetailText = "—";
    private string _diskReadText = "—";
    private string _diskWriteText = "—";
    private string _networkDownText = "—";
    private string _networkUpText = "—";
    private string _fpsText = "—";
    private string _frametimeText = "—";
    private string _fpsStatsText = "Mín —   Média —   1% low —   0,1% low —";
    private string _fpsHint = "Aguardando jogo Vulkan com a camada Phantom ativada.";
    private string _inventorySummaryText = "Aguardando primeira leitura dos sensores…";
    private string _inventoryUpdatedText = "";
    // Cascas persistentes do inventário: os 8 grupos fixos existem desde o arranque;
    // "Outros" entra/sai apenas do fim da lista. Antes, a lista era substituída por
    // uma nova a cada snapshot (grupos + rows recriados).
    private readonly ObservableCollection<InventoryGroupViewModel> _inventoryGroups =
        new(SensorInventoryBuilder.CreateShellGroups());
    private InventoryGroupViewModel? _inventoryOtherGroup;
    private bool _hasInventorySnapshot;
    private bool _isOverview = true;
    private bool _isSettings;
    private bool _isAlerts;
    private bool _isCustomSensors;
    private bool _isSensorTuning;
    private bool _isDiagnostics;
    private readonly ObservableCollection<ErrorLogEntry> _diagnosticEntries = new();
    private IReadOnlyList<string> _diagnosticSensorLines = ["Aguardando a primeira leitura dos sensores."];
    private string _diagnosticsMessage = "Os diagnósticos ficam somente neste computador.";
    private string _updateIntervalText;
    private bool _intervalEdited;
    private bool _logToCsv;
    private bool _logToJson;
    private bool _logToJsonJournal;
    private bool _telemetryExportEnabled;
    private string _telemetryExportPortText = "7317";
    private bool _telemetryExportListenAll;
    private string _telemetryExportStatusText = "Desativado";
    private string _settingsMessage = "As alterações são aplicadas ao salvar.";
    private bool _overlayShowCpu;
    private bool _overlayShowGpu;
    private bool _overlayShowRam;
    private bool _overlayShowDisk;
    private bool _overlayShowNetwork;
    private bool _overlayShowFans;
    private bool _overlayShowMotherboard;
    private bool _overlayShowFps;
    private bool _overlayShowFrametimeGraph;
    private string _overlayWidthText = "220";
    private double _overlayOpacityPercent = 85;
    private string _overlayFontSizeText = "11.5";
    private double _overlayFontSizeSlider = 11.5;
    private bool _overlayCompactModeSetting;
    private readonly ObservableCollection<OverlayMetricViewModel> _overlayRows = new();
    private readonly ObservableCollection<OverlaySeriesPatternViewModel> _overlaySeriesPatterns = new();
    private IReadOnlyList<string> _overlaySeriesOptions = [];
    private string? _selectedOverlaySeries;
    private string _overlaySeriesPatternInput = "";
    private IReadOnlyList<double> _cpuSamples = [];
    private IReadOnlyList<double> _gpuSamples = [];
    private IReadOnlyList<double> _ramSamples = [];
    private IReadOnlyList<double> _frametimeSamples = [];
    private readonly ObservableCollection<AlertRuleEditorViewModel> _alertRules = new();
    private readonly ObservableCollection<AlertHistoryEntryViewModel> _alertHistory = new();
    private readonly ObservableCollection<CustomSensorEditorViewModel> _customSensorEditors = new();
    private string _customSensorsMessage = "As alterações são aplicadas ao salvar.";
    private readonly ObservableCollection<SensorTuningEditorViewModel> _sensorTuningEditors = new();
    private IReadOnlyList<string> _sensorTuningSeriesOptions = [];
    private string _sensorTuningMessage = "As alterações são aplicadas ao salvar.";
    private AlertRuleEvaluator _alertEvaluator;
    private IReadOnlyList<string> _alertSensorOptions = [];
    private string _alertsCooldownText;
    private bool _alertsEnabled;
    private string _alertsMessage = "As alterações são aplicadas ao salvar.";
    private bool _alertToastVisible;
    private string _alertToastMessage = "";
    private DispatcherTimer? _alertToastTimer;
    private bool _alertEditorInitialized;
    private readonly LinuxDesktopNotificationService? _desktopNotificationService;
    private readonly LinuxAlertScriptRunner _alertScriptRunner = new();
    private bool _windowActive;
    private bool _windowMinimized;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string StatusText { get => _statusText; private set => Set(ref _statusText, value); }
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
    public ObservableCollection<ErrorLogEntry> DiagnosticEntries => _diagnosticEntries;
    public IReadOnlyList<string> DiagnosticSensorLines { get => _diagnosticSensorLines; private set => Set(ref _diagnosticSensorLines, value); }
    public string DiagnosticsMessage { get => _diagnosticsMessage; private set => Set(ref _diagnosticsMessage, value); }
    public string ErrorLogPathText => ErrorLog.LogFilePath;
    public bool HasDiagnosticEntries => DiagnosticEntries.Count > 0;
    public string DiagnosticEntriesSummary => HasDiagnosticEntries
        ? $"Exibindo {DiagnosticEntries.Count} registros recentes."
        : "Nenhum erro registrado nesta execução ou no arquivo local.";
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
    public string UpdateIntervalText
    {
        get => _updateIntervalText;
        set
        {
            if (!Set(ref _updateIntervalText, value)) return;
            var unchanged = int.TryParse(value, NumberStyles.Integer, CultureInfo.CurrentCulture, out var parsed)
                && parsed == _config.UpdateIntervalMs;
            if (Set(ref _intervalEdited, !unchanged, nameof(IsIntervalEdited)))
                Raise(nameof(IsLegacyIntervalOutOfRange));
        }
    }
    public bool IsIntervalEdited => _intervalEdited;
    public bool IsLegacyIntervalOutOfRange => !IsIntervalEdited && _config.UpdateIntervalMs > 60_000;
    public string OverlayWidthText { get => _overlayWidthText; set => Set(ref _overlayWidthText, value); }
    public string OverlayFontSizeText
    {
        get => _overlayFontSizeText;
        set
        {
            if (!Set(ref _overlayFontSizeText, value)) return;
            if (double.TryParse(value, NumberStyles.Float, CultureInfo.CurrentCulture, out var parsed)
                && double.IsFinite(parsed) && parsed is >= 8 and <= 20
                && Math.Abs(parsed * 2 - Math.Round(parsed * 2)) < 0.000001)
                OverlayFontSizeSlider = parsed;
        }
    }
    public double OverlayFontSizeSlider
    {
        get => _overlayFontSizeSlider;
        set
        {
            if (!Set(ref _overlayFontSizeSlider, value)) return;
            OverlayFontSizeText = value.ToString("0.#", CultureInfo.CurrentCulture);
        }
    }
    public string OverlayFontSizeLabel => $"{OverlayFontSizeSlider:0.#} pt";
    public bool OverlayCompactModeSetting { get => _overlayCompactModeSetting; set => Set(ref _overlayCompactModeSetting, value); }
    public double OverlayOpacityPercent
    {
        get => _overlayOpacityPercent;
        set
        {
            if (Set(ref _overlayOpacityPercent, value)) Raise(nameof(OverlayOpacityLabel));
        }
    }
    public string OverlayOpacityLabel => $"{OverlayOpacityPercent:0}%";
    public bool LogToCsv { get => _logToCsv; set => Set(ref _logToCsv, value); }
    public bool LogToJson { get => _logToJson; set => Set(ref _logToJson, value); }
    public bool LogToJsonJournal { get => _logToJsonJournal; set => Set(ref _logToJsonJournal, value); }
    public bool TelemetryExportEnabled { get => _telemetryExportEnabled; set => Set(ref _telemetryExportEnabled, value); }
    public string TelemetryExportPortText
    {
        get => _telemetryExportPortText;
        set
        {
            if (Set(ref _telemetryExportPortText, value)) Raise(nameof(TelemetryExportEndpointText));
        }
    }
    public bool TelemetryExportListenAll
    {
        get => _telemetryExportListenAll;
        set
        {
            if (Set(ref _telemetryExportListenAll, value)) Raise(nameof(TelemetryExportEndpointText));
        }
    }
    public string TelemetryExportStatusText { get => _telemetryExportStatusText; private set => Set(ref _telemetryExportStatusText, value); }
    public string TelemetryExportEndpointText => TelemetryExportListenAll
        ? $"http://<host>:{TelemetryExportPortText}/values · http://<host>:{TelemetryExportPortText}/health"
        : $"http://127.0.0.1:{TelemetryExportPortText}/values · http://127.0.0.1:{TelemetryExportPortText}/health";
    public string LogDirectoryText => UserDataPaths.LogDirectory;
    public string SettingsMessage { get => _settingsMessage; private set => Set(ref _settingsMessage, value); }
    public ObservableCollection<OverlayMetricViewModel> OverlayRows => _overlayRows;
    public ObservableCollection<OverlaySeriesPatternViewModel> OverlaySeriesPatterns => _overlaySeriesPatterns;
    public IReadOnlyList<string> OverlaySeriesOptions { get => _overlaySeriesOptions; private set => Set(ref _overlaySeriesOptions, value); }
    public string? SelectedOverlaySeries { get => _selectedOverlaySeries; set => Set(ref _selectedOverlaySeries, value); }
    public string OverlaySeriesPatternInput { get => _overlaySeriesPatternInput; set => Set(ref _overlaySeriesPatternInput, value); }
    public bool OverlayVisible => _config.ShowOverlay;
    public string OverlayButtonText => OverlayVisible ? "Ocultar overlay" : "Mostrar overlay";
    public double OverlayOpacity => _config.OverlayOpacity;
    public double OverlayWidth => _config.Overlay.Width;
    public double OverlayFontSize => _config.Overlay.FontSize;
    public bool OverlayCompactMode => _config.Overlay.CompactMode;
    public double OverlayMetricNameFontSize => Math.Max(7, _config.Overlay.FontSize - 2);
    public double OverlayMetricValueFontSize => _config.Overlay.FontSize;
    public double OverlayHeaderFontSize => Math.Max(8, _config.Overlay.FontSize - 1.5);
    public double OverlaySecondaryFontSize => Math.Max(7, _config.Overlay.FontSize - 3.5);
    public Avalonia.Thickness OverlayOuterMargin => _config.Overlay.CompactMode ? new(4) : new(8);
    public Avalonia.Thickness OverlayPadding => _config.Overlay.CompactMode ? new(6, 4) : new(12);
    public double OverlayContentSpacing => _config.Overlay.CompactMode ? 4 : 8;
    public double OverlayHeaderSpacing => _config.Overlay.CompactMode ? 1 : 2;
    public double OverlayColumnSpacing => _config.Overlay.CompactMode ? 5 : 8;
    public Avalonia.Thickness OverlayRowMargin => _config.Overlay.CompactMode ? new(0, 0.5) : new(0, 2);
    public double OverlayHideButtonMinSize => _config.Overlay.CompactMode ? 20 : 24;
    public Avalonia.Thickness OverlayHideButtonPadding => _config.Overlay.CompactMode ? new(4, 0) : new(6, 0);
    public double OverlayFrametimeGraphHeight => _config.Overlay.CompactMode ? 18 : 24;
    public bool OverlayShowCpu { get => _overlayShowCpu; set => Set(ref _overlayShowCpu, value); }
    public bool OverlayShowGpu { get => _overlayShowGpu; set => Set(ref _overlayShowGpu, value); }
    public bool OverlayShowRam { get => _overlayShowRam; set => Set(ref _overlayShowRam, value); }
    public bool OverlayShowDisk { get => _overlayShowDisk; set => Set(ref _overlayShowDisk, value); }
    public bool OverlayShowNetwork { get => _overlayShowNetwork; set => Set(ref _overlayShowNetwork, value); }
    public bool OverlayShowFans { get => _overlayShowFans; set => Set(ref _overlayShowFans, value); }
    public bool OverlayShowMotherboard { get => _overlayShowMotherboard; set => Set(ref _overlayShowMotherboard, value); }
    public bool OverlayShowFps { get => _overlayShowFps; set => Set(ref _overlayShowFps, value); }
    public bool OverlayShowFrametimeGraph { get => _overlayShowFrametimeGraph; set => Set(ref _overlayShowFrametimeGraph, value); }
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

    public MainWindowViewModel()
    {
        _config = AppConfig.Load(ConfigPaths.ConfigFile);
        _updateIntervalText = _config.UpdateIntervalMs.ToString(CultureInfo.CurrentCulture);
        _logToCsv = _config.LogToCsv;
        _logToJson = _config.LogToJson;
        _logToJsonJournal = _config.LogToJsonJournal;
        _telemetryExportEnabled = _config.TelemetryExport.Enabled;
        _telemetryExportPortText = _config.TelemetryExport.Port.ToString(CultureInfo.InvariantCulture);
        _telemetryExportListenAll = _config.TelemetryExport.ListenAll;
        _telemetryExportStatusText = _config.TelemetryExport.Enabled ? "Aguardando coleta" : "Desativado";
        _alertsCooldownText = _config.Alerts.CooldownSeconds.ToString(CultureInfo.CurrentCulture);
        _alertsEnabled = _config.Alerts.Enabled;
        _overlayShowCpu = _config.Overlay.ShowCpu;
        _overlayShowGpu = _config.Overlay.ShowGpu;
        _overlayShowRam = _config.Overlay.ShowRam;
        _overlayShowDisk = _config.Overlay.ShowDisk;
        _overlayShowNetwork = _config.Overlay.ShowNetwork;
        _overlayShowFans = _config.Overlay.ShowFans;
        _overlayShowMotherboard = _config.Overlay.ShowMotherboard;
        _overlayShowFps = _config.Overlay.ShowFps;
        _overlayShowFrametimeGraph = _config.Overlay.ShowFrametimeGraph;
        _overlayWidthText = _config.Overlay.Width.ToString(CultureInfo.CurrentCulture);
        _overlayOpacityPercent = _config.OverlayOpacity * 100;
        _overlayFontSizeText = _config.Overlay.FontSize.ToString("0.#", CultureInfo.CurrentCulture);
        _overlayFontSizeSlider = _config.Overlay.FontSize;
        _overlayCompactModeSetting = _config.Overlay.CompactMode;
        LoadOverlaySeriesPatterns();
        _alertEvaluator = new AlertRuleEvaluator(_config.Alerts);
        _desktopNotificationService = OperatingSystem.IsLinux() ? new LinuxDesktopNotificationService() : null;
        LoadCustomSensorEditors();
        LoadSensorTuningEditors();
        RefreshAlertEditor();
        _sensorService = LinuxSensorServiceFactory.Create(_config);
        AttachSensorService(_sensorService);
        UpdateOverlayRows(null);
        LoadDiagnosticEntries();
        ErrorLog.EntryWritten += OnDiagnosticEntryWritten;
    }

    public void Start()
    {
        if (_disposed || IsRunning) return;
        StartLoggers();
        _sensorService.Start();
        _started = true;
        ConfigureTelemetryExporter();
        StatusText = "Monitorando";
        Raise(nameof(IsRunning));
        Raise(nameof(IsPaused));
        Raise(nameof(CanStart));
        Raise(nameof(CanStop));
        Raise(nameof(CanPause));
        Raise(nameof(PauseButtonText));
    }

    public void Stop()
    {
        if (!IsRunning) return;
        _sensorService.SetPaused(false);
        _sensorService.Stop();
        _started = false;
        DisposeTelemetryExporter();
        _latestSensorData = null;
        UpdateTelemetryExportStatus();
        DisposeLoggers();
        StatusText = "Parado";
        FpsHint = "Monitoramento parado. Inicie novamente para continuar.";
        Raise(nameof(IsRunning));
        Raise(nameof(IsPaused));
        Raise(nameof(CanStart));
        Raise(nameof(CanStop));
        Raise(nameof(CanPause));
        Raise(nameof(PauseButtonText));
    }

    public void TogglePause()
    {
        if (!IsRunning) return;
        _sensorService.SetPaused(!_sensorService.Paused);
        StatusText = IsPaused ? "Pausado" : "Monitorando";
        Raise(nameof(IsPaused));
        Raise(nameof(PauseButtonText));
    }

    public void ToggleOverlay()
    {
        SetOverlayVisible(!_config.ShowOverlay);
    }

    public void HideOverlay()
    {
        if (_config.ShowOverlay)
            SetOverlayVisible(false);
    }

    private void SetOverlayVisible(bool visible)
    {
        var previous = _config.ShowOverlay;
        _config.ShowOverlay = visible;
        if (!_config.Save(ConfigPaths.ConfigFile))
        {
            _config.ShowOverlay = previous;
            SettingsMessage = "Não foi possível salvar showOverlay no config.json.";
            return;
        }

        Raise(nameof(OverlayVisible));
        Raise(nameof(OverlayButtonText));
        SettingsMessage = _config.ShowOverlay ? "Overlay ativado." : "Overlay ocultado.";
    }

    private void UpdateOverlayRows(SensorData? data)
    {
        var selection = new OverlayConfigSelection(
            OverlayShowCpu,
            OverlayShowGpu,
            OverlayShowRam,
            OverlayShowDisk,
            OverlayShowNetwork,
            OverlayShowFans,
            OverlayShowMotherboard,
            OverlayShowFps,
            _config.Overlay.ShowFrametimeGraph);

        // Reconciliação de rows persistentes: rows são reutilizadas e só a string que
        // mudou dispara PropertyChanged. Sem isso, OverlayRows era Clear+re-add
        // por snapshot — o ItemsControl re-media/re-renderiza tudo (até 10x/s).
        ReconcileOverlayRows(OverlayMetricsBuilder.Plan(data, selection, _config.Overlay.ShownSeries));
    }

    private void ReconcileOverlayRows(IReadOnlyList<OverlayMetricsBuilder.OverlayRowPlan> plan)
    {
        var rows = OverlayRows;
        var position = 0;
        for (var p = 0; p < plan.Count; p++)
        {
            var entry = plan[p];
            OverlayMetricViewModel row;

            if (position < rows.Count && rows[position].Identity == entry.Identity)
            {
                row = rows[position];
            }
            else
            {
                // A row pode existir depois (reordenação dos planos: ventoinha
                // detectada, ordem de instâncias): move para a posição correta.
                int found = -1;
                for (var s = position + 1; s < rows.Count && found < 0; s++)
                    if (rows[s].Identity == entry.Identity)
                        found = s;
                if (found > 0)
                {
                    row = rows[found];
                    rows.RemoveAt(found);
                    rows.Insert(position, row);
                }
                else
                {
                    row = new OverlayMetricViewModel(entry.Identity, entry.Name);
                    rows.Insert(position, row);
                }
            }

            row.Name = entry.Name;
            row.Value = entry.Value;
            row.ShowFrametimeGraph = entry.ShowFrametimeGraph;
            position++;
        }

        // Sobras: plan encolheu (série sumiu, opção desligada).
        while (rows.Count > position)
            rows.RemoveAt(rows.Count - 1);
    }

    // Inventário persistente, pelas mesmas razões do overlay: antes, todo snapshot
    // substituía grupos e rows e o ItemsControl re-media toda a árvore (até 10x/s),
    // mesmo com a aba de inventário fora de vista.
    private void ReconcileInventoryGroups(IReadOnlyList<InventoryGroupPlan> plan)
    {
        var groups = _inventoryGroups;

        // O plano contém "Outros" somente quando há séries fora dos tipos fixos;
        // a casca entra/sai apenas do fim da lista e é criada uma única vez.
        while (groups.Count > plan.Count)
            groups.RemoveAt(groups.Count - 1);
        while (groups.Count < plan.Count)
            groups.Add(_inventoryOtherGroup ??= SensorInventoryBuilder.CreateOtherGroup());

        for (var index = 0; index < plan.Count; index++)
            ReconcileInventoryRows(groups[index], plan[index].Rows);
    }

    private void ReconcileInventoryRows(InventoryGroupViewModel group, IReadOnlyList<InventoryRowPlan> plan)
    {
        var rows = group.Rows;
        var position = 0;
        for (var p = 0; p < plan.Count; p++)
        {
            var entry = plan[p];
            InventoryRowViewModel row;

            if (position < rows.Count && rows[position].Key == entry.Key)
            {
                row = rows[position];
            }
            else
            {
                // A row já pode existir mais adiante (série por instância nova
                // detectada, reordenação do plano): move para o lugar certo.
                // Move dispara um único evento CollectionChanged.
                int found = -1;
                for (var s = position + 1; s < rows.Count && found < 0; s++)
                    if (rows[s].Key == entry.Key)
                        found = s;
                if (found > 0)
                {
                    row = rows[found];
                    rows.Move(found, position);
                }
                else
                {
                    row = new InventoryRowViewModel(entry.Key);
                    rows.Insert(position, row);
                }
            }

            row.Apply(entry.Source, entry.Value, entry.Unit, entry.Available);
            position++;
        }

        // Sobras: o plano encolheu (série deixou de ser publicada); a casca
        // do grupo permanece.
        while (rows.Count > position)
            rows.RemoveAt(rows.Count - 1);
    }

    private void RaiseOverlayAppearanceProperties()
    {
        Raise(nameof(OverlayFontSize));
        Raise(nameof(OverlayCompactMode));
        Raise(nameof(OverlayMetricNameFontSize));
        Raise(nameof(OverlayMetricValueFontSize));
        Raise(nameof(OverlayHeaderFontSize));
        Raise(nameof(OverlaySecondaryFontSize));
        Raise(nameof(OverlayOuterMargin));
        Raise(nameof(OverlayPadding));
        Raise(nameof(OverlayContentSpacing));
        Raise(nameof(OverlayHeaderSpacing));
        Raise(nameof(OverlayColumnSpacing));
        Raise(nameof(OverlayRowMargin));
        Raise(nameof(OverlayHideButtonMinSize));
        Raise(nameof(OverlayHideButtonPadding));
        Raise(nameof(OverlayFrametimeGraphHeight));
    }

    private void LoadOverlaySeriesPatterns()
    {
        _overlaySeriesPatterns.Clear();
        foreach (var pattern in _config.Overlay.ShownSeries ?? ["Gpu*", "FanSpeed*", "Disk*"])
            _overlaySeriesPatterns.Add(new OverlaySeriesPatternViewModel(pattern));
    }

    public void AddDetectedOverlaySeries()
    {
        if (!SensorKey.TryParse(SelectedOverlaySeries, out var key) || key.IsPrimary)
        {
            SettingsMessage = "Selecione uma série por instância detectada.";
            return;
        }
        AddOverlaySeriesPattern(key.ToString());
    }

    public void AddManualOverlaySeriesPattern()
    {
        var pattern = OverlaySeriesPatternInput.Trim();
        if (!IsValidOverlaySeriesPattern(pattern))
        {
            SettingsMessage = "Informe uma chave de sensor válida ou um padrão com * no início/fim (ex.: Gpu*).";
            return;
        }
        AddOverlaySeriesPattern(pattern);
        OverlaySeriesPatternInput = "";
    }

    private void AddOverlaySeriesPattern(string pattern)
    {
        if (_overlaySeriesPatterns.Any(item => string.Equals(item.Pattern, pattern, StringComparison.OrdinalIgnoreCase)))
        {
            SettingsMessage = "Esse padrão já está na lista.";
            return;
        }
        _overlaySeriesPatterns.Add(new OverlaySeriesPatternViewModel(pattern));
        SettingsMessage = "Padrão adicionado. Salve para aplicar ao overlay.";
    }

    public void RemoveOverlaySeriesPattern(OverlaySeriesPatternViewModel item)
    {
        _overlaySeriesPatterns.Remove(item);
    }

    public void RestoreDefaultOverlaySeriesPatterns()
    {
        _overlaySeriesPatterns.Clear();
        foreach (var pattern in new[] { "Gpu*", "FanSpeed*", "Disk*" })
            _overlaySeriesPatterns.Add(new OverlaySeriesPatternViewModel(pattern));
        SettingsMessage = "Padrões do overlay restaurados. Salve para aplicar.";
    }

    private static bool IsValidOverlaySeriesPattern(string pattern)
    {
        if (pattern == "*") return true;
        if (pattern.Count(character => character == '*') == 0)
            return SensorKey.TryParse(pattern, out var key) && !key.IsPrimary && Enum.IsDefined(key.Type);
        if (pattern.Count(character => character == '*') != 1) return false;
        if (pattern.EndsWith('*')) return pattern.Length > 1;
        return pattern.StartsWith('*') && pattern.Length > 1;
    }

    private void RefreshOverlaySeriesOptions()
    {
        var options = _latestSensorData?.Series.Keys
            .Where(key => !key.IsPrimary)
            .Select(key => key.ToString())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(key => key, StringComparer.OrdinalIgnoreCase)
            .ToArray() ?? [];
        if (!OverlaySeriesOptions.SequenceEqual(options, StringComparer.OrdinalIgnoreCase))
            OverlaySeriesOptions = options;
        if (SelectedOverlaySeries is not null && !options.Contains(SelectedOverlaySeries, StringComparer.OrdinalIgnoreCase))
            SelectedOverlaySeries = null;
    }

    public void ShowOverview()
    {
        IsOverview = true;
        IsSettings = false;
        IsAlerts = false;
        IsCustomSensors = false;
        IsSensorTuning = false;
        IsDiagnostics = false;
        Raise(nameof(IsInventory));
    }

    public void ShowInventory()
    {
        IsOverview = false;
        IsSettings = false;
        IsAlerts = false;
        IsCustomSensors = false;
        IsSensorTuning = false;
        IsDiagnostics = false;
        Raise(nameof(IsInventory));
    }

    public void ShowSettings()
    {
        IsOverview = false;
        IsSettings = true;
        IsAlerts = false;
        IsCustomSensors = false;
        IsSensorTuning = false;
        IsDiagnostics = false;
        Raise(nameof(IsInventory));
    }

    public void ShowAlerts()
    {
        IsOverview = false;
        IsSettings = false;
        IsAlerts = true;
        IsCustomSensors = false;
        IsSensorTuning = false;
        IsDiagnostics = false;
        Raise(nameof(IsInventory));
    }

    public void ShowCustomSensors()
    {
        IsOverview = false;
        IsSettings = false;
        IsAlerts = false;
        IsCustomSensors = true;
        IsSensorTuning = false;
        IsDiagnostics = false;
        Raise(nameof(IsInventory));
    }

    public void ShowSensorTuning()
    {
        IsOverview = false;
        IsSettings = false;
        IsAlerts = false;
        IsCustomSensors = false;
        IsSensorTuning = true;
        IsDiagnostics = false;
        Raise(nameof(IsInventory));
    }

    public void ShowDiagnostics()
    {
        IsOverview = false;
        IsSettings = false;
        IsAlerts = false;
        IsCustomSensors = false;
        IsSensorTuning = false;
        IsDiagnostics = true;
        LoadDiagnosticEntries();
        RefreshDiagnosticSensorLines();
        Raise(nameof(IsInventory));
    }

    public void RefreshDiagnostics()
    {
        LoadDiagnosticEntries();
        RefreshDiagnosticSensorLines();
        DiagnosticsMessage = "Diagnóstico atualizado.";
    }

    /// <summary>
    /// Alimenta a lista de linhas de diagnóstico apenas sob demanda (aba aberta ou
    /// botão de atualizar). O array de debug chega a cada snapshot, mas reatribuí-lo
    /// por tick re-renderizava o ItemsControl mesmo com a aba invisível.
    /// </summary>
    private void RefreshDiagnosticSensorLines()
    {
        if (_latestSensorData is not null)
            DiagnosticSensorLines = _latestSensorData.SensorDebugInfo.ToArray();
    }

    public void SetDiagnosticsMessage(string message) => DiagnosticsMessage = message;

    private void LoadDiagnosticEntries()
    {
        DiagnosticEntries.Clear();
        foreach (var entry in ErrorLog.ReadRecentEntries())
            DiagnosticEntries.Add(entry);
        Raise(nameof(HasDiagnosticEntries));
        Raise(nameof(DiagnosticEntriesSummary));
    }

    private void OnDiagnosticEntryWritten(ErrorLogEntry entry)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_disposed) return;
            if (DiagnosticEntries.Contains(entry)) return;
            DiagnosticEntries.Add(entry);
            while (DiagnosticEntries.Count > 500)
                DiagnosticEntries.RemoveAt(0);
            Raise(nameof(HasDiagnosticEntries));
            Raise(nameof(DiagnosticEntriesSummary));
        });
    }

    public void AddCustomSensor()
    {
        if (CustomSensorEditors.Count >= CustomSensorEngine.MaxSensors)
        {
            CustomSensorsMessage = $"O limite de {CustomSensorEngine.MaxSensors} sensores customizados foi atingido.";
            return;
        }

        var name = "Novo sensor";
        var suffix = 2;
        while (CustomSensorEditors.Any(editor => string.Equals(editor.Name.Trim(), name, StringComparison.OrdinalIgnoreCase)))
            name = $"Novo sensor {suffix++}";

        CustomSensorEditors.Add(new CustomSensorEditorViewModel(name, "", "", CustomSensorEditors.Count));
        UpdateCustomSensorPositions();
        CustomSensorsMessage = "Sensor adicionado. Preencha a fórmula e salve para aplicar.";
    }

    public void RemoveCustomSensor(CustomSensorEditorViewModel editor)
    {
        if (!CustomSensorEditors.Remove(editor)) return;
        UpdateCustomSensorPositions();
        CustomSensorsMessage = "Sensor removido. Salve para aplicar a alteração.";
    }

    public void MoveCustomSensor(CustomSensorEditorViewModel editor, int offset)
    {
        var oldIndex = CustomSensorEditors.IndexOf(editor);
        var newIndex = oldIndex + offset;
        if (oldIndex < 0 || newIndex < 0 || newIndex >= CustomSensorEditors.Count) return;

        CustomSensorEditors.Move(oldIndex, newIndex);
        UpdateCustomSensorPositions();
        CustomSensorsMessage = "Ordem alterada. Salvar pode mudar os índices Custom#i usados em alertas.";
    }

    public void SaveCustomSensors()
    {
        if (CustomSensorEditors.Count > CustomSensorEngine.MaxSensors)
        {
            CustomSensorsMessage = $"O limite é de {CustomSensorEngine.MaxSensors} sensores customizados.";
            return;
        }

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var candidate = new List<CustomSensorConfig>(CustomSensorEditors.Count);
        for (var index = 0; index < CustomSensorEditors.Count; index++)
        {
            var editor = CustomSensorEditors[index];
            var name = editor.Name.Trim();
            var formula = editor.Formula.Trim();
            var unit = editor.Unit.Trim();

            if (string.IsNullOrWhiteSpace(name))
            {
                CustomSensorsMessage = $"Informe um nome para {editor.SeriesKey}.";
                return;
            }
            if (!names.Add(name))
            {
                CustomSensorsMessage = $"O nome '{name}' está duplicado. Use nomes únicos.";
                return;
            }
            if (!CustomSensorEngine.IsFormulaValid(formula))
            {
                CustomSensorsMessage = $"A fórmula de {editor.SeriesKey} ({name}) é inválida.";
                return;
            }

            candidate.Add(new CustomSensorConfig { Name = name, Formula = formula, Unit = unit });
        }

        if (CustomSensorConfigsEqual(_config.CustomSensors, candidate))
        {
            CustomSensorsMessage = "Nenhuma alteração para salvar.";
            return;
        }

        var previous = _config.CustomSensors;
        var wasRunning = IsRunning;
        var wasPaused = IsPaused;
        _config.CustomSensors = candidate;
        if (!_config.Save(ConfigPaths.ConfigFile))
        {
            _config.CustomSensors = previous;
            CustomSensorsMessage = "Não foi possível salvar o config.json. As fórmulas em execução não foram alteradas.";
            return;
        }

        ReplaceSensorService();
        LoadCustomSensorEditors();
        CustomSensorsMessage = wasRunning
            ? wasPaused
                ? "Sensores salvos e aplicados. Coleta retomada pausada; as estatísticas da sessão foram zeradas."
                : "Sensores salvos e aplicados; as estatísticas da sessão foram zeradas."
            : "Sensores salvos e aplicados à próxima coleta.";
    }

    private void LoadCustomSensorEditors()
    {
        CustomSensorEditors.Clear();
        for (var index = 0; index < _config.CustomSensors.Count; index++)
        {
            var sensor = _config.CustomSensors[index];
            CustomSensorEditors.Add(new CustomSensorEditorViewModel(sensor.Name, sensor.Formula, sensor.Unit, index));
        }
        UpdateCustomSensorPositions();
    }

    private void UpdateCustomSensorPositions()
    {
        for (var index = 0; index < CustomSensorEditors.Count; index++)
            CustomSensorEditors[index].SetPosition(index, CustomSensorEditors.Count);
    }

    private static bool CustomSensorConfigsEqual(IReadOnlyList<CustomSensorConfig> left, IReadOnlyList<CustomSensorConfig> right) =>
        left.Count == right.Count && left.Zip(right).All(pair =>
            string.Equals(pair.First.Name, pair.Second.Name, StringComparison.Ordinal)
            && string.Equals(pair.First.Formula, pair.Second.Formula, StringComparison.Ordinal)
            && string.Equals(pair.First.Unit, pair.Second.Unit, StringComparison.Ordinal));

    public void AddSensorTuning()
    {
        if (SensorTuningEditors.Count >= SensorTuningEngine.MaxTunings)
        {
            SensorTuningMessage = $"O limite de {SensorTuningEngine.MaxTunings} regras foi atingido.";
            return;
        }

        var series = SensorTuningSeriesOptions.FirstOrDefault() ?? "";
        var tuning = new SensorTuningConfig { Series = series };
        SensorTuningEditors.Add(new SensorTuningEditorViewModel(tuning, SensorTuningSeriesOptions, SensorTuningEditors.Count));
        UpdateSensorTuningPositions();
        SensorTuningMessage = "Regra adicionada. Configure a série e salve para aplicar.";
    }

    public void RemoveSensorTuning(SensorTuningEditorViewModel editor)
    {
        if (!SensorTuningEditors.Remove(editor)) return;
        UpdateSensorTuningPositions();
        SensorTuningMessage = "Regra removida. Salve para aplicar a alteração.";
    }

    public void MoveSensorTuning(SensorTuningEditorViewModel editor, int offset)
    {
        var oldIndex = SensorTuningEditors.IndexOf(editor);
        var newIndex = oldIndex + offset;
        if (oldIndex < 0 || newIndex < 0 || newIndex >= SensorTuningEditors.Count) return;

        SensorTuningEditors.Move(oldIndex, newIndex);
        UpdateSensorTuningPositions();
        SensorTuningMessage = "Ordem alterada. A última regra correspondente à série terá prioridade.";
    }

    public void SaveSensorTunings()
    {
        if (SensorTuningEditors.Count > SensorTuningEngine.MaxTunings)
        {
            SensorTuningMessage = $"O limite é de {SensorTuningEngine.MaxTunings} regras.";
            return;
        }

        var candidate = new List<SensorTuningConfig>(SensorTuningEditors.Count);
        for (var index = 0; index < SensorTuningEditors.Count; index++)
        {
            var editor = SensorTuningEditors[index];
            var series = editor.Series.Trim();
            if (string.IsNullOrWhiteSpace(series))
            {
                SensorTuningMessage = $"Informe uma série ou padrão para a regra {index + 1}.";
                return;
            }

            if (!int.TryParse(editor.IntervalText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var intervalMs)
                || intervalMs < 0
                || (intervalMs > 0 && intervalMs is < 100 or > 3_600_000))
            {
                SensorTuningMessage = $"O intervalo da regra {index + 1} deve ser 0 ou entre 100 e 3.600.000 ms.";
                return;
            }

            if (!int.TryParse(editor.SmoothingText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var smoothing)
                || smoothing is < 0 or > SensorTuningEngine.MaxSmoothing)
            {
                SensorTuningMessage = $"A suavização da regra {index + 1} deve ficar entre 0 e {SensorTuningEngine.MaxSmoothing}.";
                return;
            }

            if (intervalMs > 0)
                intervalMs = Math.Max(_config.UpdateIntervalMs, Math.Clamp(intervalMs, 100, 3_600_000));
            candidate.Add(new SensorTuningConfig { Series = series, IntervalMs = intervalMs, Smoothing = smoothing });
        }

        if (SensorTuningConfigsEqual(_config.SensorTunings, candidate))
        {
            SensorTuningMessage = "Nenhuma alteração para salvar.";
            return;
        }

        var previous = _config.SensorTunings;
        var wasRunning = IsRunning;
        var wasPaused = IsPaused;
        _config.SensorTunings = candidate;
        if (!_config.Save(ConfigPaths.ConfigFile))
        {
            _config.SensorTunings = previous;
            SensorTuningMessage = "Não foi possível salvar o config.json. As regras em execução não foram alteradas.";
            return;
        }

        ReplaceSensorService();
        LoadSensorTuningEditors();
        SensorTuningMessage = wasRunning
            ? wasPaused
                ? "Ajustes salvos e aplicados. Coleta continua pausada; as estatísticas da sessão foram zeradas."
                : "Ajustes salvos e aplicados; as estatísticas da sessão foram zeradas."
            : "Ajustes salvos e aplicados à próxima coleta.";
    }

    private void LoadSensorTuningEditors()
    {
        RefreshSensorTuningSeriesOptions();
        SensorTuningEditors.Clear();
        for (var index = 0; index < _config.SensorTunings.Count; index++)
            SensorTuningEditors.Add(new SensorTuningEditorViewModel(_config.SensorTunings[index], SensorTuningSeriesOptions, index));
        UpdateSensorTuningPositions();
    }

    private void RefreshSensorTuningSeriesOptions()
    {
        var series = new HashSet<string>(_config.SensorTunings.Select(tuning => tuning.Series), StringComparer.OrdinalIgnoreCase);
        foreach (var group in InventoryGroups)
            foreach (var row in group.Rows.Where(row => row.IsAvailable))
                series.Add(row.Key);

        var options = series.Where(value => !string.IsNullOrWhiteSpace(value))
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (SensorTuningSeriesOptions.SequenceEqual(options, StringComparer.OrdinalIgnoreCase)) return;

        SensorTuningSeriesOptions = options;
        foreach (var editor in SensorTuningEditors)
            editor.SeriesOptions = options;
    }

    private void UpdateSensorTuningPositions()
    {
        for (var index = 0; index < SensorTuningEditors.Count; index++)
            SensorTuningEditors[index].SetPosition(index, SensorTuningEditors.Count);
    }

    private static bool SensorTuningConfigsEqual(IReadOnlyList<SensorTuningConfig> left, IReadOnlyList<SensorTuningConfig> right) =>
        left.Count == right.Count && left.Zip(right).All(pair =>
            string.Equals(pair.First.Series, pair.Second.Series, StringComparison.Ordinal)
            && pair.First.IntervalMs == pair.Second.IntervalMs
            && pair.First.Smoothing == pair.Second.Smoothing);

    public void AddAlertRule()
    {
        if (AlertSensorOptions.Count == 0)
        {
            AlertsMessage = "Aguarde a primeira leitura para detectar séries de sensores.";
            return;
        }

        AlertRules.Add(new AlertRuleEditorViewModel(new AlertRule
        {
            Sensor = SensorKey.TryParse(AlertSensorOptions[0], out var key) ? key.Type : SensorType.CpuTemperature,
            SensorIndex = key.Index,
            Threshold = 80,
            Popup = true
        }, AlertSensorOptions));
        AlertsMessage = "Regra adicionada. Salve para aplicá-la.";
    }

    public void RemoveAlertRule(AlertRuleEditorViewModel rule)
    {
        AlertRules.Remove(rule);
        AlertsMessage = "Regra removida. Salve para aplicar a alteração.";
    }

    public void SaveAlerts()
    {
        if (!int.TryParse(AlertsCooldownText, NumberStyles.Integer, CultureInfo.CurrentCulture, out var cooldown)
            || cooldown is < 1 or > 3600)
        {
            AlertsMessage = "O cooldown global deve ficar entre 1 e 3.600 segundos.";
            return;
        }

        var candidateRules = new List<AlertRule>(AlertRules.Count);
        foreach (var editor in AlertRules)
        {
            if (!SensorKey.TryParse(editor.SensorKey, out var sensorKey))
            {
                AlertsMessage = $"Sensor inválido na regra: {editor.SensorKey}";
                return;
            }
            if (!double.TryParse(editor.ThresholdText, NumberStyles.Float, CultureInfo.CurrentCulture, out var threshold)
                || !double.IsFinite(threshold))
            {
                AlertsMessage = $"Informe um limite numérico válido para {editor.SensorKey}.";
                return;
            }
            if (!int.TryParse(editor.CooldownText, NumberStyles.Integer, CultureInfo.CurrentCulture, out var ruleCooldown)
                || ruleCooldown is < 0 or > 3600)
            {
                AlertsMessage = $"O cooldown da regra {editor.SensorKey} deve ficar entre 0 e 3.600 segundos.";
                return;
            }
            if (!Enum.TryParse<AlertComparison>(editor.Comparison, out _))
            {
                AlertsMessage = $"Comparação inválida na regra {editor.SensorKey}.";
                return;
            }
            if (sensorKey.Index < 0)
            {
                AlertsMessage = $"Índice de sensor inválido: {editor.SensorKey}";
                return;
            }

            candidateRules.Add(editor.CreateRule(threshold, ruleCooldown));
        }

        var previous = _config.Alerts;
        var candidate = new AlertsConfig
        {
            Enabled = AlertsEnabled,
            CooldownSeconds = cooldown,
            AllowShutdown = previous.AllowShutdown,
            Rules = candidateRules
        };
        _config.Alerts = candidate;
        if (!_config.Save(ConfigPaths.ConfigFile))
        {
            _config.Alerts = previous;
            AlertsMessage = "Não foi possível salvar o config.json. As regras em execução não foram alteradas.";
            return;
        }

        _alertEvaluator = new AlertRuleEvaluator(candidate);
        _alertsEnabled = candidate.Enabled;
        _alertsCooldownText = candidate.CooldownSeconds.ToString(CultureInfo.CurrentCulture);
        AlertsMessage = "Alertas salvos e aplicados. Serão avaliados na próxima leitura.";
        Raise(nameof(AlertsEnabled));
        Raise(nameof(AlertsCooldownText));
    }

    private void RefreshAlertEditor()
    {
        var keys = new HashSet<string>(_config.Alerts.Rules.Select(rule => new SensorKey(rule.Sensor, rule.SensorIndex).ToString()), StringComparer.Ordinal);
        foreach (var group in InventoryGroups)
            foreach (var row in group.Rows.Where(row => row.IsAvailable))
                keys.Add(row.Key);

        // Reatribuir ItemsSource a cada snapshot (até 10x/s) re-renderizava cada
        // ComboBox de regra; só troca quando o conjunto de chaves mudar — mesmo
        // guard usado por RefreshOverlaySeriesOptions/RefreshSensorTuningSeriesOptions.
        var options = keys.OrderBy(key => key, StringComparer.Ordinal).ToArray();
        if (AlertSensorOptions is null || !AlertSensorOptions.SequenceEqual(options, StringComparer.Ordinal))
        {
            AlertSensorOptions = options;
            foreach (var rule in AlertRules)
                rule.SensorOptions = options;
        }

        if (!_alertEditorInitialized)
        {
            foreach (var rule in _config.Alerts.Rules)
                AlertRules.Add(new AlertRuleEditorViewModel(rule, AlertSensorOptions));
            _alertEditorInitialized = true;
        }
    }

    private void RecordAlert(AlertTrigger trigger, DateTime timestamp)
    {
        AlertHistory.Insert(0, new AlertHistoryEntryViewModel(
            timestamp.ToLocalTime().ToString("HH:mm:ss"),
            trigger.SensorName,
            trigger.Message,
            trigger.Value.ToString("F1", CultureInfo.CurrentCulture)));
        while (AlertHistory.Count > 50) AlertHistory.RemoveAt(AlertHistory.Count - 1);
        Raise(nameof(HasAlertHistory));

        if (!trigger.Rule.Popup) return;
        AlertToastMessage = trigger.Message;
        AlertToastVisible = true;
        _alertToastTimer ??= new DispatcherTimer { Interval = TimeSpan.FromSeconds(6) };
        _alertToastTimer.Stop();
        _alertToastTimer.Tick -= OnAlertToastTimerTick;
        _alertToastTimer.Tick += OnAlertToastTimerTick;
        _alertToastTimer.Start();

        if (!_windowActive || _windowMinimized)
            _ = SendDesktopNotificationAsync(trigger.Message);
    }

    public void SetWindowPresentationState(bool isActive, bool isMinimized)
    {
        _windowActive = isActive;
        _windowMinimized = isMinimized;
    }

    private async Task SendDesktopNotificationAsync(string message)
    {
        if (_desktopNotificationService is null || _disposed) return;
        try
        {
            await _desktopNotificationService.NotifyAsync(message);
        }
        catch (Exception ex)
        {
            ErrorLog.Write("LinuxDesktopNotification", ex);
        }
    }

    private void OnAlertToastTimerTick(object? sender, EventArgs e)
    {
        _alertToastTimer?.Stop();
        AlertToastVisible = false;
    }

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

    public void SaveSettings()
    {
        if (!double.TryParse(OverlayFontSizeText, NumberStyles.Float, CultureInfo.CurrentCulture, out var overlayFontSize)
            || !double.IsFinite(overlayFontSize)
            || overlayFontSize is < 8 or > 20
            || Math.Abs(overlayFontSize * 2 - Math.Round(overlayFontSize * 2)) >= 0.000001)
        {
            SettingsMessage = "O tamanho da fonte deve ficar entre 8 e 20 pt, em passos de 0,5 pt.";
            return;
        }

        if (!int.TryParse(OverlayWidthText, NumberStyles.Integer, CultureInfo.CurrentCulture, out var overlayWidth)
            || overlayWidth is < 140 or > 700)
        {
            SettingsMessage = "A largura do overlay deve ficar entre 140 e 700 pixels.";
            return;
        }

        if (!double.IsFinite(OverlayOpacityPercent) || OverlayOpacityPercent is < 10 or > 100)
        {
            SettingsMessage = "A opacidade do overlay deve ficar entre 10% e 100%.";
            return;
        }

        int? newInterval = null;
        if (IsIntervalEdited)
        {
            if (!int.TryParse(UpdateIntervalText, NumberStyles.Integer, CultureInfo.CurrentCulture, out var parsed)
                || parsed is < 100 or > 60_000)
            {
                SettingsMessage = "Informe um intervalo entre 100 e 60.000 ms.";
                return;
            }
            newInterval = parsed;
        }

        if (!int.TryParse(TelemetryExportPortText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var telemetryPort)
            || telemetryPort is < 1024 or > 65_535)
        {
            SettingsMessage = "A porta do exportador HTTP deve ficar entre 1024 e 65.535.";
            return;
        }

        var oldInterval = _config.UpdateIntervalMs;
        var oldCsv = _config.LogToCsv;
        var oldJson = _config.LogToJson;
        var oldJournal = _config.LogToJsonJournal;
        var oldTelemetryEnabled = _config.TelemetryExport.Enabled;
        var oldTelemetryPort = _config.TelemetryExport.Port;
        var oldTelemetryListenAll = _config.TelemetryExport.ListenAll;
        var oldOverlayCpu = _config.Overlay.ShowCpu;
        var oldOverlayWidth = _config.Overlay.Width;
        var oldOverlayOpacity = _config.OverlayOpacity;
        var oldOverlayFontSize = _config.Overlay.FontSize;
        var oldOverlayCompactMode = _config.Overlay.CompactMode;
        var oldOverlayGpu = _config.Overlay.ShowGpu;
        var oldOverlayRam = _config.Overlay.ShowRam;
        var oldOverlayDisk = _config.Overlay.ShowDisk;
        var oldOverlayNetwork = _config.Overlay.ShowNetwork;
        var oldOverlayFans = _config.Overlay.ShowFans;
        var oldOverlayMotherboard = _config.Overlay.ShowMotherboard;
        var oldOverlayFps = _config.Overlay.ShowFps;
        var oldOverlayShowFrametimeGraph = _config.Overlay.ShowFrametimeGraph;
        var oldOverlayShownSeries = _config.Overlay.ShownSeries?.ToList() ?? ["Gpu*", "FanSpeed*", "Disk*"];
        var newOverlayShownSeries = _overlaySeriesPatterns.Select(item => item.Pattern).ToList();
        if (newOverlayShownSeries.Count == 0)
        {
            SettingsMessage = "Adicione ao menos um padrão de série ou restaure os padrões antes de salvar.";
            return;
        }
        var invalidPattern = newOverlayShownSeries.FirstOrDefault(pattern => !IsValidOverlaySeriesPattern(pattern));
        if (invalidPattern is not null)
        {
            SettingsMessage = $"O padrão '{invalidPattern}' é inválido.";
            return;
        }
        var collectionWasRunning = IsRunning;
        var collectionWasPaused = IsPaused;
        var intervalChanged = newInterval.HasValue && newInterval.Value != oldInterval;
        var loggingChanged = LogToCsv != oldCsv || LogToJson != oldJson || LogToJsonJournal != oldJournal;
        var telemetryChanged = TelemetryExportEnabled != oldTelemetryEnabled
            || telemetryPort != oldTelemetryPort
            || TelemetryExportListenAll != oldTelemetryListenAll;
        var overlayVisualChanged = overlayWidth != oldOverlayWidth
            || Math.Abs(OverlayOpacityPercent / 100.0 - oldOverlayOpacity) > 0.000001;
        var overlayAppearanceChanged = Math.Abs(overlayFontSize - oldOverlayFontSize) > 0.000001
            || OverlayCompactModeSetting != oldOverlayCompactMode;
        var overlayChanged = OverlayShowCpu != oldOverlayCpu
            || OverlayShowGpu != oldOverlayGpu
            || OverlayShowRam != oldOverlayRam
            || OverlayShowDisk != oldOverlayDisk
            || OverlayShowNetwork != oldOverlayNetwork
            || OverlayShowFans != oldOverlayFans
            || OverlayShowMotherboard != oldOverlayMotherboard
            || OverlayShowFps != oldOverlayFps
            || OverlayShowFrametimeGraph != oldOverlayShowFrametimeGraph
            || !oldOverlayShownSeries.SequenceEqual(newOverlayShownSeries, StringComparer.OrdinalIgnoreCase)
            || overlayVisualChanged
            || overlayAppearanceChanged;

        if (!intervalChanged && !loggingChanged && !telemetryChanged && !overlayChanged)
        {
            ConfigureTelemetryExporter();
            SettingsMessage = "Nenhuma alteração para salvar.";
            return;
        }

        if (newInterval.HasValue) _config.UpdateIntervalMs = newInterval.Value;
        _config.LogToCsv = LogToCsv;
        _config.LogToJson = LogToJson;
        _config.LogToJsonJournal = LogToJsonJournal;
        _config.TelemetryExport.Enabled = TelemetryExportEnabled;
        _config.TelemetryExport.Port = telemetryPort;
        _config.TelemetryExport.ListenAll = TelemetryExportListenAll;
        _config.Overlay.ShowCpu = OverlayShowCpu;
        _config.Overlay.ShowGpu = OverlayShowGpu;
        _config.Overlay.ShowRam = OverlayShowRam;
        _config.Overlay.ShowDisk = OverlayShowDisk;
        _config.Overlay.ShowNetwork = OverlayShowNetwork;
        _config.Overlay.ShowFans = OverlayShowFans;
        _config.Overlay.ShowMotherboard = OverlayShowMotherboard;
        _config.Overlay.ShowFps = OverlayShowFps;
        _config.Overlay.ShowFrametimeGraph = OverlayShowFrametimeGraph;
        _config.Overlay.ShownSeries = newOverlayShownSeries;
        _config.Overlay.Width = overlayWidth;
        _config.OverlayOpacity = OverlayOpacityPercent / 100.0;
        _config.Overlay.FontSize = overlayFontSize;
        _config.Overlay.CompactMode = OverlayCompactModeSetting;

        if (!_config.Save(ConfigPaths.ConfigFile))
        {
            _config.UpdateIntervalMs = oldInterval;
            _config.LogToCsv = oldCsv;
            _config.LogToJson = oldJson;
            _config.LogToJsonJournal = oldJournal;
            _config.TelemetryExport.Enabled = oldTelemetryEnabled;
            _config.TelemetryExport.Port = oldTelemetryPort;
            _config.TelemetryExport.ListenAll = oldTelemetryListenAll;
            _config.Overlay.ShowCpu = oldOverlayCpu;
            _config.Overlay.ShowGpu = oldOverlayGpu;
            _config.Overlay.ShowRam = oldOverlayRam;
            _config.Overlay.ShowDisk = oldOverlayDisk;
            _config.Overlay.ShowNetwork = oldOverlayNetwork;
            _config.Overlay.ShowFans = oldOverlayFans;
            _config.Overlay.ShowMotherboard = oldOverlayMotherboard;
            _config.Overlay.ShowFps = oldOverlayFps;
            _config.Overlay.ShowFrametimeGraph = oldOverlayShowFrametimeGraph;
            _config.Overlay.ShownSeries = oldOverlayShownSeries;
            _config.Overlay.Width = oldOverlayWidth;
            _config.OverlayOpacity = oldOverlayOpacity;
            _config.Overlay.FontSize = oldOverlayFontSize;
            _config.Overlay.CompactMode = oldOverlayCompactMode;
            OverlayShowCpu = oldOverlayCpu;
            OverlayShowGpu = oldOverlayGpu;
            OverlayShowRam = oldOverlayRam;
            OverlayShowDisk = oldOverlayDisk;
            OverlayShowNetwork = oldOverlayNetwork;
            OverlayShowFans = oldOverlayFans;
            OverlayShowMotherboard = oldOverlayMotherboard;
            OverlayShowFps = oldOverlayFps;
            OverlayShowFrametimeGraph = oldOverlayShowFrametimeGraph;
            UpdateOverlayRows(_latestSensorData);
            LoadOverlaySeriesPatterns();
            SettingsMessage = "Não foi possível salvar o config.json. As opções em execução não foram alteradas.";
            return;
        }

        if (intervalChanged)
        {
            ReplaceSensorService();
        }
        else if (loggingChanged)
        {
            DisposeLoggers();
            if (IsRunning) StartLoggers();
        }

        ConfigureTelemetryExporter();

        LoadOverlaySeriesPatterns();
        UpdateOverlayRows(_latestSensorData);
        OverlayShowFrametimeGraph = _config.Overlay.ShowFrametimeGraph;
        OverlayWidthText = _config.Overlay.Width.ToString(CultureInfo.CurrentCulture);
        OverlayOpacityPercent = _config.OverlayOpacity * 100;
        OverlayFontSizeSlider = _config.Overlay.FontSize;
        OverlayCompactModeSetting = _config.Overlay.CompactMode;
        if (overlayVisualChanged)
        {
            Raise(nameof(OverlayWidth));
            Raise(nameof(OverlayOpacity));
        }
        if (overlayAppearanceChanged)
            RaiseOverlayAppearanceProperties();

        UpdateIntervalText = _config.UpdateIntervalMs.ToString(CultureInfo.CurrentCulture);
        TelemetryExportPortText = _config.TelemetryExport.Port.ToString(CultureInfo.InvariantCulture);
        SettingsMessage = intervalChanged
            ? collectionWasRunning
                ? collectionWasPaused
                    ? "Coleta reiniciada pausada; as estatísticas da sessão foram zeradas."
                    : "Coleta reiniciada; as estatísticas da sessão foram zeradas."
                : "Intervalo salvo. Será usado quando a coleta for iniciada."
            : overlayChanged && !loggingChanged && !telemetryChanged
                ? "Opções do overlay salvas e aplicadas."
            : loggingChanged && telemetryChanged
                ? collectionWasRunning
                    ? "Logs e exportador HTTP atualizados sem reiniciar a coleta."
                    : "Opções de logs e exportador salvas para a próxima coleta."
                : telemetryChanged
                    ? collectionWasRunning
                        ? "Exportador HTTP atualizado sem reiniciar a coleta."
                        : "Opções do exportador salvas para a próxima coleta."
                    : collectionWasRunning
                        ? "Logs atualizados sem reiniciar a coleta."
                        : "Opções de log salvas para a próxima coleta.";
    }

    private void ReplaceSensorService()
    {
        var wasRunning = IsRunning;
        var wasPaused = IsPaused;
        var oldService = _sensorService;

        if (oldService.IsRunning)
        {
            oldService.SetPaused(false);
            oldService.Stop();
        }
        _started = false;
        DetachSensorService(oldService);
        DisposeLoggers();
        oldService.Dispose();

        _sensorService = LinuxSensorServiceFactory.Create(_config);
        AttachSensorService(_sensorService);
        ResetDisplayedSession();

        if (wasRunning)
        {
            if (wasPaused) _sensorService.SetPaused(true);
            Start();
            if (wasPaused)
            {
                StatusText = "Pausado";
                Raise(nameof(IsPaused));
                Raise(nameof(PauseButtonText));
            }
        }
        else
        {
            StatusText = "Parado";
            RaiseRunState();
            UpdateTelemetryExportStatus();
        }
    }

    private void ConfigureTelemetryExporter()
    {
        var options = _config.TelemetryExport;
        if (!options.Enabled)
        {
            DisposeTelemetryExporter();
            UpdateTelemetryExportStatus();
            return;
        }

        if (!IsRunning)
        {
            DisposeTelemetryExporter();
            UpdateTelemetryExportStatus();
            return;
        }

        if (_telemetryHttpExporter is not null
            && _activeTelemetryPort == options.Port
            && _activeTelemetryListenAll == options.ListenAll
            && _telemetryHttpExporter.IsListening)
        {
            UpdateTelemetryExportStatus();
            return;
        }

        DisposeTelemetryExporter();
        var exporter = new TelemetryHttpExporter(options.Port, options.ListenAll);
        _telemetryHttpExporter = exporter;
        _activeTelemetryPort = options.Port;
        _activeTelemetryListenAll = options.ListenAll;
        exporter.StateChanged += OnTelemetryExporterStateChanged;
        if (_latestSensorData is not null)
            exporter.Publish(_latestSensorData);

        exporter.Start();
        UpdateTelemetryExportStatus();
    }

    private void OnTelemetryExporterStateChanged(TelemetryHttpExporter exporter) => Dispatcher.UIThread.Post(() =>
    {
        if (_disposed || !ReferenceEquals(exporter, _telemetryHttpExporter)) return;
        UpdateTelemetryExportStatus();
    });

    private void UpdateTelemetryExportStatus()
    {
        if (!_config.TelemetryExport.Enabled)
        {
            TelemetryExportStatusText = "Desativado";
            return;
        }

        if (!IsRunning)
        {
            TelemetryExportStatusText = "Aguardando coleta";
            return;
        }

        if (_telemetryHttpExporter is null)
        {
            TelemetryExportStatusText = "Aguardando inicialização";
            return;
        }

        TelemetryExportStatusText = _telemetryHttpExporter.IsListening
            ? $"Escutando em {_telemetryHttpExporter.Endpoint}"
            : $"Falha ao abrir o listener: {_telemetryHttpExporter.LastError ?? "erro desconhecido"}";
    }

    private void DisposeTelemetryExporter()
    {
        var exporter = _telemetryHttpExporter;
        _telemetryHttpExporter = null;
        _activeTelemetryPort = null;
        _activeTelemetryListenAll = null;
        if (exporter is null) return;
        exporter.StateChanged -= OnTelemetryExporterStateChanged;
        exporter.Dispose();
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
        RefreshOverlaySeriesOptions();
        ReconcileInventoryGroups(SensorInventoryBuilder.Plan(data));
        UpdateOverlayRows(data);
        RefreshSensorTuningSeriesOptions();
        RefreshAlertEditor();
        HasInventorySnapshot = true;
        Raise(nameof(IsWaitingForInventorySnapshot));
        var availableCount = InventoryGroups.Sum(group => group.Rows.Count(row => row.IsAvailable));
        var unavailableCount = InventoryGroups.Sum(group => group.Rows.Count(row => !row.IsAvailable));
        InventorySummaryText = $"{availableCount} séries disponíveis · {unavailableCount} tipos comuns indisponíveis";
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
        FpsText = Has(data, SensorType.Fps) ? $"{fps:F0}" : "—";
        FrametimeText = Has(data, SensorType.FrametimeMs) ? $"{frametime:F2} ms" : "—";
        FpsStatsText = Has(data, SensorType.Fps)
            ? $"Mín {data.FpsMin:F0}   Média {data.FpsAvg:F0}   1% low {data.Fps1Low:F0}   0,1% low {data.Fps01Low:F0}"
            : "Mín —   Média —   1% low —   0,1% low —";
        FpsHint = Has(data, SensorType.Fps)
            ? "FPS recebido pela camada Phantom Vulkan."
            : "Aguardando jogo Vulkan com ENABLE_PHANTOM_OVERLAY=1 nas opções de inicialização.";

        AddHistory(_cpuHistory, data.GetValue(SensorType.CpuTotal), Has(data, SensorType.CpuTotal));
        AddHistory(_gpuHistory, data.GetValue(SensorType.GpuCore), Has(data, SensorType.GpuCore));
        AddHistory(_ramHistory, data.GetValue(SensorType.RamUsage), Has(data, SensorType.RamUsage));
        CpuSamples = _cpuHistory.ToArray();
        GpuSamples = _gpuHistory.ToArray();
        RamSamples = _ramHistory.ToArray();
        FrametimeSamples = data.FrametimeHistory.Select(x => (double)x).ToArray();
        LastUpdateText = $"Atualizado às {data.Timestamp.ToLocalTime():HH:mm:ss}";

        // Loggers/HTTP saíram daqui: rodam na thread dedicada a partir de
        // OnSensorData (a UI não grava arquivo serializado por tick).

        foreach (var alert in _alertEvaluator.Evaluate(data))
        {
            _alertScriptRunner.Run(alert);
            RecordAlert(alert, data.Timestamp);
        }
    }

    private void StartLoggers()
    {
        var logDirectory = UserDataPaths.LogDirectory;
        var sessionStamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture);

        if (_config.LogToCsv)
            _csvLogger = new CsvLogger(Path.Combine(logDirectory, $"sensors_{sessionStamp}.csv"));

        if (_config.LogToJson)
            _jsonLogger = new JsonLogger(Path.Combine(logDirectory, "sensors_latest.json"));

        if (_config.LogToJsonJournal)
        {
            _jsonJournalLogger = new JsonJournalLogger(Path.Combine(logDirectory, $"sensors_{sessionStamp}.jsonl"));
            _jsonJournalLogger.WriteNote("início da sessão");
        }

        // Loggers em thread dedicada (antes: CSV + JSON + journal serializados na
        // UI thread a cada snapshot, em até 10 Hz). Mesma bomba do frontend WPF;
        // Drain no DisposeLoggers garante drenagem antes de dispor os escritores.
        _snapshotWriter?.Dispose();
        _snapshotWriter = new BackgroundSnapshotWriter();
        if (_csvLogger is not null) _snapshotWriter.AddWriter(_csvLogger.Write);
        if (_jsonLogger is not null) _snapshotWriter.AddWriter(_jsonLogger.Write);
        if (_jsonJournalLogger is not null) _snapshotWriter.AddWriter(_jsonJournalLogger.Write);
    }

    /// <summary>Bomba de fundo para loggers; vida útil = sessão de loggers.
    /// Ver BackgroundSnapshotWriter (drenagem antes de dispor os escritores).</summary>
    private BackgroundSnapshotWriter? _snapshotWriter;

    private void DisposeLoggers()
    {
        _snapshotWriter?.Dispose();
        _snapshotWriter = null;
        _csvLogger?.Dispose();
        _jsonLogger?.Dispose();
        _jsonJournalLogger?.Dispose();
        _csvLogger = null;
        _jsonLogger = null;
        _jsonJournalLogger = null;
    }

    private static bool Has(SensorData data, SensorType type) => data.Values.ContainsKey(type);

    private static string Percent(SensorData data, SensorType type) =>
        Has(data, type) ? $"{data.GetValue(type):F0}%" : "—";

    private static string Temperature(SensorData data, SensorType type) =>
        Has(data, type) ? $"{data.GetValue(type):F0} °C" : "—";

    private static string Clock(SensorData data, SensorType type)
    {
        // Primeira chave da categoria (canônicas precedem as por instância em
        // GetSeriesKeys). Loop explícito em vez de FirstOrDefault: o default do
        // struct (enum 0) casava com `key.Type == type` quando nenhuma chave existe.
        var keys = data.GetSeriesKeys();
        for (var i = 0; i < keys.Count; i++)
        {
            if (keys[i].Type != type) continue;
            var key = keys[i];
            if (key.IsPrimary || data.Series.ContainsKey(key))
                return $"{data.GetValue(key):F0} MHz";
            return "—";
        }
        return "—";
    }

    private static string MemoryPair(SensorData data, SensorType usedType, SensorType totalType, bool bytesAreGb = false)
    {
        if (!Has(data, usedType) || !Has(data, totalType)) return "—";
        var used = data.GetValue(usedType);
        var total = data.GetValue(totalType);
        if (bytesAreGb) return $"{used:F1} / {total:F1} GB";
        return $"{used / 1024:F1} / {total / 1024:F1} GB";
    }

    private static string Rate(SensorData data, SensorType type)
    {
        if (!Has(data, type)) return "—";
        var bytesPerSecond = data.GetValue(type);
        return bytesPerSecond >= 1024 * 1024
            ? $"{bytesPerSecond / (1024 * 1024):F1} MB/s"
            : $"{bytesPerSecond / 1024:F0} KB/s";
    }

    private static void AddHistory(Queue<double> history, double value, bool hasValue)
    {
        if (!hasValue) return;
        history.Enqueue(value);
        while (history.Count > HistoryLength) history.Dequeue();
    }

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        Raise(propertyName);
        return true;
    }

    private void Raise(string? propertyName) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        ErrorLog.EntryWritten -= OnDiagnosticEntryWritten;
        DetachSensorService(_sensorService);
        if (IsRunning)
        {
            _sensorService.SetPaused(false);
            _sensorService.Stop();
            _started = false;
        }
        DisposeTelemetryExporter();
        DisposeLoggers();
        _alertToastTimer?.Stop();
        _desktopNotificationService?.Dispose();
        _sensorService.Dispose();
    }
}
