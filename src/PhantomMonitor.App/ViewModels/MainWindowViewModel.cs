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
// ao lado deste: Overlay, Diagnostics, Beta, CustomSensors, SensorTuning, Alerts, ConfigApply.
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
    private string _fpsHint = "Aguardando dados de FPS de um jogo.";
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
    private IReadOnlyList<string> _linuxOverlayDiagnosticLines = [];
    private IReadOnlyList<DiagnosticSensorWarningViewModel> _diagnosticSensorWarnings = [];
    private bool _hasDiagnosticSensorSnapshot;
    private string _diagnosticsMessage = "Os diagnósticos ficam somente neste computador.";
    private string _updateIntervalText;
    private int _overlayRenderModeIndex;
    private bool _intervalEdited;
    private bool _logToCsv;
    private bool _logToJson;
    private bool _logToJsonJournal;
    private bool _telemetryExportEnabled;
    private string _telemetryExportPortText = "7317";
    private bool _telemetryExportListenAll;
    private string _telemetryExportStatusText = "Desativado";
    private string _settingsMessage = "";
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
    private readonly DelayedActionScheduler _alertToastTimer = new(TimeSpan.FromSeconds(6));
    private bool _alertEditorInitialized;
    private readonly LinuxDesktopNotificationService? _desktopNotificationService;
    private bool _windowActive;
    private bool _windowMinimized;

    public event PropertyChangedEventHandler? PropertyChanged;

    public MainWindowViewModel()
    {
        _config = AppConfig.Load(ConfigPaths.ConfigFile, out var configurationLoadError, createIfMissing: ConfigPaths.IsPortable);
        InitializeConfigurationWarning(configurationLoadError);
        _config.Normalize();
        _overlayHotkeyDraft = _config.ToggleOverlayHotkey;
        _themePalettesDraft = CloneThemePalettes(_config.ThemePalettes);
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
        _showCustomSensorsInSidebar = _config.Beta.ShowCustomSensorsInSidebar;
        _showSensorTuningInSidebar = _config.Beta.ShowSensorTuningInSidebar;
        _showAlertsInSidebar = _config.Beta.ShowAlertsInSidebar;
        _themeIndex = _config.Theme == "slate" ? 1 : 0;
        _navigationPosition = _config.NavigationPosition;
        _navigationDisplayMode = _config.NavigationDisplayMode;
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
        _overlayRenderModeIndex = _config.Overlay.RenderMode == "inGame" ? 1 : 0;
        LoadOverlayInGameTargets();
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
        ConfigureInGameOverlayHeartbeat();
        LoadDiagnosticEntries();
        ErrorLog.EntryWritten += OnDiagnosticEntryWritten;
        App.ThemeChanged += OnThemeChanged;
        InitUpdateService();
        FinishSettingsDraftInitialization();
        RefreshAttentionMessage();
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
        if (_inGameFrameChannelActive)
            PublishInGameOverlayFrame(forceHidden: true);
        _disposed = true;
        ErrorLog.EntryWritten -= OnDiagnosticEntryWritten;
        App.ThemeChanged -= OnThemeChanged;
        DetachSensorService(_sensorService);
        _overlayHotkeyBackend?.Dispose();
        if (OperatingSystem.IsWindows()) _hotkeys?.Dispose();
        if (IsRunning)
        {
            _sensorService.SetPaused(false);
            _sensorService.Stop();
            _started = false;
        }
        DisposeTelemetryExporter();
        DisposeLoggers();
        DisposeSettingsSaveConfirmationTimer();
        _updateTimer?.Stop();
        _updateTimer = null;
        _updates = null;
        _alertToastTimer.Dispose();
        _inventoryStatusClearTimer.Dispose();
        _inGameOverlayHeartbeat?.Stop();
        _inGameOverlayHeartbeat = null;
        _desktopNotificationService?.Dispose();
        CleanupWindowsOverlayState();
        _sensorService.Dispose();
        _inGameOverlayFramePublisher.Dispose();
    }
}
