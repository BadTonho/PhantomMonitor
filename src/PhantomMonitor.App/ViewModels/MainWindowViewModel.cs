using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Collections.ObjectModel;
using Avalonia.Threading;
using PhantomMonitor.App.Services;
using PhantomMonitor.Models;
using PhantomMonitor.Services;
using PhantomMonitor.Services.Linux;

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
    private IReadOnlyList<InventoryGroupViewModel> _inventoryGroups = [];
    private bool _hasInventorySnapshot;
    private bool _isOverview = true;
    private bool _isSettings;
    private bool _isAlerts;
    private string _updateIntervalText;
    private bool _intervalEdited;
    private bool _logToCsv;
    private bool _logToJson;
    private bool _logToJsonJournal;
    private string _settingsMessage = "As alterações são aplicadas ao salvar.";
    private IReadOnlyList<double> _cpuSamples = [];
    private IReadOnlyList<double> _gpuSamples = [];
    private IReadOnlyList<double> _ramSamples = [];
    private IReadOnlyList<double> _frametimeSamples = [];
    private readonly ObservableCollection<AlertRuleEditorViewModel> _alertRules = new();
    private readonly ObservableCollection<AlertHistoryEntryViewModel> _alertHistory = new();
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
    public IReadOnlyList<InventoryGroupViewModel> InventoryGroups { get => _inventoryGroups; private set => Set(ref _inventoryGroups, value); }
    public bool HasInventorySnapshot { get => _hasInventorySnapshot; private set => Set(ref _hasInventorySnapshot, value); }
    public bool IsWaitingForInventorySnapshot => !HasInventorySnapshot;
    public bool IsOverview { get => _isOverview; private set => Set(ref _isOverview, value); }
    public bool IsInventory => !IsOverview && !IsSettings && !IsAlerts;
    public bool IsSettings { get => _isSettings; private set => Set(ref _isSettings, value); }
    public bool IsAlerts { get => _isAlerts; private set => Set(ref _isAlerts, value); }
    public ObservableCollection<AlertRuleEditorViewModel> AlertRules => _alertRules;
    public ObservableCollection<AlertHistoryEntryViewModel> AlertHistory => _alertHistory;
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
    public bool LogToCsv { get => _logToCsv; set => Set(ref _logToCsv, value); }
    public bool LogToJson { get => _logToJson; set => Set(ref _logToJson, value); }
    public bool LogToJsonJournal { get => _logToJsonJournal; set => Set(ref _logToJsonJournal, value); }
    public string LogDirectoryText => UserDataPaths.LogDirectory;
    public string SettingsMessage { get => _settingsMessage; private set => Set(ref _settingsMessage, value); }
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
        _alertsCooldownText = _config.Alerts.CooldownSeconds.ToString(CultureInfo.CurrentCulture);
        _alertsEnabled = _config.Alerts.Enabled;
        _alertEvaluator = new AlertRuleEvaluator(_config.Alerts);
        _desktopNotificationService = OperatingSystem.IsLinux() ? new LinuxDesktopNotificationService() : null;
        RefreshAlertEditor();
        _sensorService = LinuxSensorServiceFactory.Create(_config);
        AttachSensorService(_sensorService);
    }

    public void Start()
    {
        if (_disposed || IsRunning) return;
        StartLoggers();
        _sensorService.Start();
        _started = true;
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
        Raise(nameof(IsInventory));
    }

    public void ShowInventory()
    {
        IsOverview = false;
        IsSettings = false;
        IsAlerts = false;
        Raise(nameof(IsInventory));
    }

    public void ShowSettings()
    {
        IsOverview = false;
        IsSettings = true;
        IsAlerts = false;
        Raise(nameof(IsInventory));
    }

    public void ShowAlerts()
    {
        IsOverview = false;
        IsSettings = false;
        IsAlerts = true;
        Raise(nameof(IsInventory));
    }

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

        AlertSensorOptions = keys.OrderBy(key => key, StringComparer.Ordinal).ToArray();
        foreach (var rule in AlertRules)
            rule.SensorOptions = AlertSensorOptions;

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
        _sensorUpdatedHandler = data => Dispatcher.UIThread.Post(() =>
        {
            if (ReferenceEquals(service, _sensorService))
                UpdateSnapshot(data);
        });
        service.SensorUpdated += _sensorUpdatedHandler;
    }

    private void DetachSensorService(ISensorService service)
    {
        if (_sensorUpdatedHandler is null) return;
        service.SensorUpdated -= _sensorUpdatedHandler;
        _sensorUpdatedHandler = null;
    }

    public void SaveSettings()
    {
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

        var oldInterval = _config.UpdateIntervalMs;
        var oldCsv = _config.LogToCsv;
        var oldJson = _config.LogToJson;
        var oldJournal = _config.LogToJsonJournal;
        var collectionWasRunning = IsRunning;
        var collectionWasPaused = IsPaused;
        var intervalChanged = newInterval.HasValue && newInterval.Value != oldInterval;
        var loggingChanged = LogToCsv != oldCsv || LogToJson != oldJson || LogToJsonJournal != oldJournal;

        if (!intervalChanged && !loggingChanged)
        {
            SettingsMessage = "Nenhuma alteração para salvar.";
            return;
        }

        if (newInterval.HasValue) _config.UpdateIntervalMs = newInterval.Value;
        _config.LogToCsv = LogToCsv;
        _config.LogToJson = LogToJson;
        _config.LogToJsonJournal = LogToJsonJournal;

        if (!_config.Save(ConfigPaths.ConfigFile))
        {
            _config.UpdateIntervalMs = oldInterval;
            _config.LogToCsv = oldCsv;
            _config.LogToJson = oldJson;
            _config.LogToJsonJournal = oldJournal;
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

        UpdateIntervalText = _config.UpdateIntervalMs.ToString(CultureInfo.CurrentCulture);
        SettingsMessage = intervalChanged
            ? collectionWasRunning
                ? collectionWasPaused
                    ? "Coleta reiniciada pausada; as estatísticas da sessão foram zeradas."
                    : "Coleta reiniciada; as estatísticas da sessão foram zeradas."
                : "Intervalo salvo. Será usado quando a coleta for iniciada."
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

        InventoryGroups = SensorInventoryBuilder.Build(data);
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

        _csvLogger?.Write(data);
        _jsonLogger?.Write(data);
        _jsonJournalLogger?.Write(data);

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
    }

    private void DisposeLoggers()
    {
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
        var key = data.GetSeriesKeys().FirstOrDefault(k => k.Type == type);
        return key.Type == type && (key.IsPrimary || data.Series.ContainsKey(key))
            ? $"{data.GetValue(key):F0} MHz"
            : "—";
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
        DetachSensorService(_sensorService);
        if (IsRunning)
        {
            _sensorService.SetPaused(false);
            _sensorService.Stop();
            _started = false;
        }
        DisposeLoggers();
        _alertToastTimer?.Stop();
        _desktopNotificationService?.Dispose();
        _sensorService.Dispose();
    }
}
