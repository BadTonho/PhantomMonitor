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

// ViewModel principal do dashboard Avalonia. Cada domínio vive em um arquivo partial
// ao lado deste: Overlay, Diagnostics, CustomSensors, SensorTuning, Alerts, ConfigApply.
public sealed partial class MainWindowViewModel : INotifyPropertyChanged, IDisposable
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
    private readonly InventoryAggregateStatsTracker _inventoryAggregateStats = new();
    private InventoryGroupViewModel? _inventoryOtherGroup;
    private bool _hasInventorySnapshot;
    private bool _isOverview = true;
    private bool _isSettings;
    private bool _isAlerts;
    private bool _isCustomSensors;
    private bool _isSensorTuning;
    private bool _isDiagnostics;
    private string _selectedDiagnosticSection = "summary";
    private readonly ObservableCollection<ErrorLogEntry> _diagnosticEntries = new();
    private IReadOnlyList<string> _diagnosticSensorLines = ["Aguardando a primeira leitura dos sensores."];
    private IReadOnlyList<DiagnosticSensorWarningViewModel> _diagnosticSensorWarnings = [];
    private bool _hasDiagnosticSensorSnapshot;
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
    private string _overlayWidthText = "220";
    private double _overlayOpacityPercent = 85;
    private string _overlayFontSizeText = "11.5";
    private double _overlayFontSizeSlider = 11.5;
    private bool _overlayCompactModeSetting;
    private readonly ObservableCollection<OverlayMetricViewModel> _overlayRows = new();
    private readonly ObservableCollection<OverlayCanvasBlockViewModel> _overlayBlocks = new();
    private readonly ObservableCollection<OverlayCanvasBlockViewModel> _overlayAppliedBlocks = new();
    private readonly ObservableCollection<OverlayGraph> _overlayGraphsDraft = new();
    private readonly ObservableCollection<OverlayTemplate> _overlayTemplatesDraft = new();
    private readonly ObservableCollection<object> _overlayAppliedExtraRows = new();
    private double _overlayCanvasWidth = 960;
    private double _overlayCanvasHeight = 620;
    private double _overlayEditorCanvasWidth = 960;
    private double _overlayEditorCanvasHeight = 760;
    private IReadOnlyList<string> _overlaySeriesOptions = [];
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
    public string SelectedDiagnosticSection => _selectedDiagnosticSection;
    public bool IsDiagnosticSummary => SelectedDiagnosticSection == "summary";
    public bool IsDiagnosticErrors => SelectedDiagnosticSection == "errors";
    public bool IsDiagnosticSensors => SelectedDiagnosticSection == "sensors";
    public ObservableCollection<ErrorLogEntry> DiagnosticEntries => _diagnosticEntries;
    public IReadOnlyList<string> DiagnosticSensorLines { get => _diagnosticSensorLines; private set => Set(ref _diagnosticSensorLines, value); }
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
    public string OverlayWidthText
    {
        get => _overlayWidthText;
        set
        {
            if (Set(ref _overlayWidthText, value)) RaiseOverlayPreviewProperties();
        }
    }
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
            RaiseOverlayPreviewProperties();
        }
    }
    public string OverlayFontSizeLabel => $"{OverlayFontSizeSlider:0.#} pt";
    public bool OverlayCompactModeSetting
    {
        get => _overlayCompactModeSetting;
        set
        {
            if (Set(ref _overlayCompactModeSetting, value)) RaiseOverlayPreviewProperties();
        }
    }
    public double OverlayOpacityPercent
    {
        get => _overlayOpacityPercent;
        set
        {
            if (Set(ref _overlayOpacityPercent, value) is var changed && changed)
            {
                Raise(nameof(OverlayOpacityLabel));
                RaiseOverlayPreviewProperties();
            }
        }
    }
    public string OverlayOpacityLabel => $"{OverlayOpacityPercent:0}%";
    // ---- Tema visual (Fase 3: seletor em Configurações) ----
    // Índice 0 = neptune, 1 = slate (ordem idêntica a ThemeOptions).
    public IReadOnlyList<string> ThemeOptions { get; } =
    [
        "Neptune (visual original do Avalonia)",
        "Slate (porta verbatim do visual do WPF)"
    ];
    public int ThemeIndex { get => _themeIndex; set => Set(ref _themeIndex, value); }
    private int _themeIndex;

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
    public ObservableCollection<OverlayCanvasBlockViewModel> OverlayBlocks => _overlayBlocks;
    public ObservableCollection<OverlayCanvasBlockViewModel> OverlayAppliedBlocks => _overlayAppliedBlocks;
    public ObservableCollection<OverlayGraph> OverlayGraphsDraft => _overlayGraphsDraft;
    public ObservableCollection<OverlayTemplate> OverlayTemplatesDraft => _overlayTemplatesDraft;
    public double OverlayCanvasWidth { get => _overlayCanvasWidth; private set => Set(ref _overlayCanvasWidth, value); }
    public double OverlayCanvasHeight { get => _overlayCanvasHeight; private set => Set(ref _overlayCanvasHeight, value); }
    public IReadOnlyList<string> OverlaySeriesOptions { get => _overlaySeriesOptions; private set => Set(ref _overlaySeriesOptions, value); }
    public bool OverlayVisible => _config.ShowOverlay;
    public string OverlayButtonText => OverlayVisible ? "Ocultar overlay" : "Mostrar overlay";
    public double OverlayOpacity => _overlayOpacityEffective;
    public double OverlayWidth => _overlayWidthEffective;
    public double OverlayFontSize => _overlayFontEffective;
    public bool OverlayCompactMode => _config.Overlay.CompactMode;
    public double OverlayMetricNameFontSize => Math.Max(7, _overlayFontEffective - 2);
    public double OverlayMetricValueFontSize => _overlayFontEffective;
    public double OverlayHeaderFontSize => Math.Max(8, _overlayFontEffective - 1.5);
    public double OverlaySecondaryFontSize => Math.Max(7, _overlayFontEffective - 3.5);
    public double OverlayProfileTagFontSize => Math.Max(7, _overlayFontEffective - 2.5);
    public Avalonia.Thickness OverlayOuterMargin => _config.Overlay.CompactMode ? new(4) : new(8);
    public Avalonia.Thickness OverlayPadding => _config.Overlay.CompactMode ? new(6, 4) : new(12);
    public double OverlayContentSpacing => _config.Overlay.CompactMode ? 4 : 8;
    public double OverlayHeaderSpacing => _config.Overlay.CompactMode ? 1 : 2;
    public Avalonia.Thickness OverlayRowMargin => _config.Overlay.CompactMode ? new(0, 0.5) : new(0, 2);
    public double OverlayHideButtonMinSize => _config.Overlay.CompactMode ? 20 : 24;
    public Avalonia.Thickness OverlayHideButtonPadding => _config.Overlay.CompactMode ? new(4, 0) : new(6, 0);
    public double OverlayFrametimeGraphHeight => _config.Overlay.CompactMode ? 18 : 24;
    public Avalonia.Thickness OverlayBlockPadding => _config.Overlay.CompactMode ? new(4, 2.5) : new(7, 4.5);

    // ---- Prévia do overlay (Configurações → Overlay flutuante) ----
    // A janela real só assume os valores ao "Salvar e aplicar"; a prévia deriva os
    // mesmos cálculos dos campos de edição no ato, para avaliar o overlay antes de
    // salvar. Perfis por aplicativo ativos continuam podendo sobrepor a janela real.
    private double OverlayPreviewFont => Math.Clamp(_overlayFontSizeSlider, 8, 20);
    private bool OverlayPreviewCompact => _overlayCompactModeSetting;
    private bool TryParseOverlayPreviewWidth(out int width)
        => int.TryParse(_overlayWidthText, NumberStyles.Integer, CultureInfo.CurrentCulture, out width)
            && width >= 140 && width <= 700;
    public double OverlayPreviewWidth => TryParseOverlayPreviewWidth(out var width) ? width : _overlayWidthEffective;
    public bool OverlayPreviewWidthInvalid => !TryParseOverlayPreviewWidth(out _);
    public double OverlayPreviewOpacity => Math.Clamp(_overlayOpacityPercent / 100.0, 0.1, 1.0);
    public double OverlayPreviewNameFontSize => Math.Max(7, OverlayPreviewFont - 2);
    public double OverlayPreviewValueFontSize => OverlayPreviewFont;
    public double OverlayPreviewSecondaryFontSize => Math.Max(7, OverlayPreviewFont - 3.5);
    public double OverlayPreviewProfileTagFontSize => Math.Max(7, OverlayPreviewFont - 2.5);
    public double OverlayPreviewContentSpacing => OverlayPreviewCompact ? 4 : 8;
    public Avalonia.Thickness OverlayPreviewRowMargin => OverlayPreviewCompact ? new(0, 0.5) : new(0, 2);
    public Avalonia.Thickness OverlayPreviewPadding => OverlayPreviewCompact ? new(6, 4) : new(12);
    public Avalonia.Thickness OverlayPreviewBlockPadding => OverlayPreviewCompact ? new(4, 2.5) : new(7, 4.5);
    public double OverlayPreviewFrametimeGraphHeight => OverlayPreviewCompact ? 18 : 24;
    public double OverlayEditorCanvasWidth { get => _overlayEditorCanvasWidth; private set => Set(ref _overlayEditorCanvasWidth, value); }
    public double OverlayEditorCanvasHeight { get => _overlayEditorCanvasHeight; private set => Set(ref _overlayEditorCanvasHeight, value); }

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
        _config.Normalize();
        _overlayFontEffective = _config.Overlay.FontSize;
        _overlayWidthEffective = _config.Overlay.Width;
        _overlayOpacityEffective = Math.Clamp(_config.OverlayOpacity <= 0 ? 0.85 : _config.OverlayOpacity, 0.1, 1.0);
        FollowOffsetX = _config.OverlayPosition.X;
        FollowOffsetY = _config.OverlayPosition.Y;
        foreach (var graph in _config.Overlay.Graphs) _overlayGraphsDraft.Add(CloneOverlayGraph(graph));
        foreach (var template in _config.Overlay.Templates) _overlayTemplatesDraft.Add(CloneOverlayTemplate(template));
        InitializeOverlayBlocks();
        BuildOverlayExtraRows();
        BuildOverlayAppliedExtraRows();
        ApplyOverlayProfile(null);
        _updateIntervalText = _config.UpdateIntervalMs.ToString(CultureInfo.CurrentCulture);
        _logToCsv = _config.LogToCsv;
        _logToJson = _config.LogToJson;
        _logToJsonJournal = _config.LogToJsonJournal;
        _themeIndex = _config.Theme == "slate" ? 1 : 0;
        _telemetryExportEnabled = _config.TelemetryExport.Enabled;
        _telemetryExportPortText = _config.TelemetryExport.Port.ToString(CultureInfo.InvariantCulture);
        _telemetryExportListenAll = _config.TelemetryExport.ListenAll;
        _telemetryExportStatusText = _config.TelemetryExport.Enabled ? "Aguardando coleta" : "Desativado";
        _alertsCooldownText = _config.Alerts.CooldownSeconds.ToString(CultureInfo.CurrentCulture);
        _alertsEnabled = _config.Alerts.Enabled;
        _overlayWidthText = _config.Overlay.Width.ToString(CultureInfo.CurrentCulture);
        _overlayOpacityPercent = _config.OverlayOpacity * 100;
        OverlayFontFamilyOptions = LoadOverlayFontFamilyOptions();
        LoadOverlayAppearanceDraft();
        _overlayFontSizeText = _config.Overlay.FontSize.ToString("0.#", CultureInfo.CurrentCulture);
        _overlayFontSizeSlider = _config.Overlay.FontSize;
        _overlayCompactModeSetting = _config.Overlay.CompactMode;
        _alertEvaluator = new AlertRuleEvaluator(_config.Alerts);
        _desktopNotificationService = OperatingSystem.IsLinux() ? new LinuxDesktopNotificationService() : null;
        LoadCustomSensorEditors();
        LoadSensorTuningEditors();
        RefreshAlertEditor();
        _sensorService = SensorServiceFactory.Create(_config.UpdateIntervalMs, _config);
        AttachSensorService(_sensorService);
        RefreshStartWithWindows();
        ApplyHotkeys();
        UpdateOverlayRows(null);
        LoadDiagnosticEntries();
        ErrorLog.EntryWritten += OnDiagnosticEntryWritten;
        App.ThemeChanged += OnThemeChanged;
        InitUpdateService();
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
        SelectDiagnosticSection("summary");
        LoadDiagnosticEntries();
        RefreshDiagnosticSensorState();
        Raise(nameof(IsInventory));
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
            {
                var estimate = data.GetLabel(key).Contains("aprox", StringComparison.OrdinalIgnoreCase) ? "≈ " : "";
                return $"{estimate}{data.GetValue(key):F0} MHz";
            }
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
        return OverlayMetricsBuilder.RateText(data.GetValue(type));
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
        App.ThemeChanged -= OnThemeChanged;
        DetachSensorService(_sensorService);
        if (OperatingSystem.IsWindows()) _hotkeys?.Dispose();
        if (IsRunning)
        {
            _sensorService.SetPaused(false);
            _sensorService.Stop();
            _started = false;
        }
        DisposeTelemetryExporter();
        DisposeLoggers();
        _updateTimer?.Stop();
        _updateTimer = null;
        _updates = null;
        _alertToastTimer?.Stop();
        _desktopNotificationService?.Dispose();
        _sensorService.Dispose();
    }
}
