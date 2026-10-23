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
using PhantomMonitor.Services.Update;

namespace PhantomMonitor.App.ViewModels;

// Aplicação de configurações: SaveSettings, troca do serviço de sensores e exportador HTTP.
public sealed partial class MainWindowViewModel
{
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

        // Repository vazio usa o repositório oficial (decisão tomada no serviço).
        var updateRepository = UpdateRepositoryText.Trim();
        if (updateRepository.Length > 0 && UpdateService.NormalizeRepository(updateRepository) is null)
        {
            SettingsMessage = "O repositório deve estar como owner/repo ou em uma URL do GitHub.";
            return;
        }

        var oldInterval = _config.UpdateIntervalMs;
        var oldTheme = _config.Theme;
        var oldCsv = _config.LogToCsv;
        var oldJson = _config.LogToJson;
        var oldJournal = _config.LogToJsonJournal;
        var oldTelemetryEnabled = _config.TelemetryExport.Enabled;
        var oldTelemetryPort = _config.TelemetryExport.Port;
        var oldTelemetryListenAll = _config.TelemetryExport.ListenAll;
        var oldUpdateAutoCheck = _config.Update.AutoCheck;
        var oldUpdateBeta = _config.Update.CheckBeta;
        var oldUpdateRepository = _config.Update.Repository;
        var oldOverlayWidth = _config.Overlay.Width;
        var oldOverlayOpacity = _config.OverlayOpacity;
        var oldOverlayFontSize = _config.Overlay.FontSize;
        var oldOverlayCompactMode = _config.Overlay.CompactMode;
        var oldOverlayShowFrametimeGraph = _config.Overlay.ShowFrametimeGraph;
        var oldOverlayBlocks = (_config.Overlay.Blocks ?? []).Select(CloneOverlayBlock).ToList();
        var oldOverlayGraphs = _config.Overlay.Graphs.Select(CloneOverlayGraph).ToList();
        var oldOverlayTemplates = _config.Overlay.Templates.Select(CloneOverlayTemplate).ToList();
        var collectionWasRunning = IsRunning;
        var collectionWasPaused = IsPaused;
        var intervalChanged = newInterval.HasValue && newInterval.Value != oldInterval;
        var loggingChanged = LogToCsv != oldCsv || LogToJson != oldJson || LogToJsonJournal != oldJournal;
        var telemetryChanged = TelemetryExportEnabled != oldTelemetryEnabled
            || telemetryPort != oldTelemetryPort
            || TelemetryExportListenAll != oldTelemetryListenAll;
        var updateChanged = UpdateAutoCheck != oldUpdateAutoCheck
            || UpdateBetaChannel != oldUpdateBeta
            || updateRepository != oldUpdateRepository;
        var themeChanged = (ThemeIndex == 1 ? "slate" : "neptune") != oldTheme;
        var overlayVisualChanged = overlayWidth != oldOverlayWidth
            || Math.Abs(OverlayOpacityPercent / 100.0 - oldOverlayOpacity) > 0.000001;
        var overlayAppearanceChanged = Math.Abs(overlayFontSize - oldOverlayFontSize) > 0.000001
            || OverlayCompactModeSetting != oldOverlayCompactMode
            || OverlayShowFrametimeGraph != oldOverlayShowFrametimeGraph;
        var overlayLayoutChanged = OverlayLayoutHasChanges();
        var overlayChanged = overlayLayoutChanged
            || overlayVisualChanged
            || overlayAppearanceChanged;

        if (!intervalChanged && !loggingChanged && !telemetryChanged && !overlayChanged && !updateChanged
            && !themeChanged)
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
        _config.Update.AutoCheck = UpdateAutoCheck;
        _config.Update.CheckBeta = UpdateBetaChannel;
        _config.Update.Repository = updateRepository;
        _config.Theme = ThemeIndex == 1 ? "slate" : "neptune";
        _config.Overlay.Width = overlayWidth;
        _config.OverlayOpacity = OverlayOpacityPercent / 100.0;
        _config.Overlay.FontSize = overlayFontSize;
        _config.Overlay.CompactMode = OverlayCompactModeSetting;
        _config.Overlay.ShowFrametimeGraph = OverlayShowFrametimeGraph;
        CopyOverlayLayoutToConfig();

        if (!_config.Save(ConfigPaths.ConfigFile))
        {
            _config.UpdateIntervalMs = oldInterval;
            _config.LogToCsv = oldCsv;
            _config.LogToJson = oldJson;
            _config.LogToJsonJournal = oldJournal;
            _config.TelemetryExport.Enabled = oldTelemetryEnabled;
            _config.TelemetryExport.Port = oldTelemetryPort;
            _config.TelemetryExport.ListenAll = oldTelemetryListenAll;
            _config.Update.AutoCheck = oldUpdateAutoCheck;
            _config.Update.CheckBeta = oldUpdateBeta;
            _config.Update.Repository = oldUpdateRepository;
            _config.Theme = oldTheme;
            _config.Overlay.Width = oldOverlayWidth;
            _config.OverlayOpacity = oldOverlayOpacity;
            _config.Overlay.FontSize = oldOverlayFontSize;
            _config.Overlay.CompactMode = oldOverlayCompactMode;
            _config.Overlay.ShowFrametimeGraph = oldOverlayShowFrametimeGraph;
            _config.Overlay.Blocks = oldOverlayBlocks;
            _config.Overlay.Graphs = oldOverlayGraphs;
            _config.Overlay.Templates = oldOverlayTemplates;
            ReloadOverlayLayoutDraft();
            SettingsMessage = "Não foi possível salvar o config.json. As opções em execução não foram alteradas.";
            return;
        }

        _config.Overlay.BlocksMigrated = false;
        if (overlayLayoutChanged)
            ApplyOverlayLayoutDraft();

        // Fase 3 — troca de dicionário em runtime (isolada ou junto das demais
        // alterações). As views reagem pelos DynamicResource e a VM repinta os
        // pincéis resolvidos por token no evento ThemeChanged.
        if (themeChanged)
            App.ApplyTheme(_config.Theme);

        // Tema mudou sozinho: nada mais a reconfigurar; mensagem dedicada.
        if (themeChanged && !intervalChanged && !loggingChanged && !telemetryChanged
            && !updateChanged && !overlayChanged)
        {
            SettingsMessage = "Tema salvo e aplicado sem reiniciar.";
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

        // Base do overlay mudou (largura/fonte/opacidade/acento): reaplica o perfil
        // em vigor sobre ela (perfis P3 — F1.3).
        OnOverlayBaseConfigChanged();
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

        if (updateChanged && !intervalChanged && !loggingChanged && !telemetryChanged && !overlayChanged)
        {
            SettingsMessage = "Preferências de atualização salvas.";
        }
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

        _sensorService = SensorServiceFactory.Create(_config.UpdateIntervalMs, _config);
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

    // Fase 3 — disparado por App.ApplyTheme DEPOIS da troca do dicionário.
    // Invalida o cache de pincéis da VM e repinta o que está visível na hora,
    // sem depender do próximo tick (importante com a coleta parada/pausada).
    private void OnThemeChanged()
    {
        if (_disposed) return;
        DashboardBrushCache.Clear();
        if (_latestSensorData is not null)
            UpdateDashboardExtras(_latestSensorData);
        ApplyOverlayProfile(_activeProfile);
        UpdateOverlayRows(_latestSensorData);
    }

}
