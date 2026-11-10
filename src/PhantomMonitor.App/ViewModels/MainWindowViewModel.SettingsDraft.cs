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

// Propriedades de rascunho das Configurações (intervalo, overlay, tema, logs, telemetria).
public sealed partial class MainWindowViewModel
{
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
            OnSettingsDraftChanged();
        }
    }
    public bool IsIntervalEdited => _intervalEdited;
    public bool IsLegacyIntervalOutOfRange => !IsIntervalEdited && _config.UpdateIntervalMs > 60_000;
    public string OverlayWidthText
    {
        get => _overlayWidthText;
        set
        {
            if (!Set(ref _overlayWidthText, value)) return;
            OnSettingsDraftChanged();
            RaiseOverlayPreviewProperties();
        }
    }
    public string OverlayFontSizeText
    {
        get => _overlayFontSizeText;
        set
        {
            if (!Set(ref _overlayFontSizeText, value)) return;
            OnSettingsDraftChanged();
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
            OnSettingsDraftChanged();
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
            if (!Set(ref _overlayCompactModeSetting, value)) return;
            OnSettingsDraftChanged();
            RaiseOverlayPreviewProperties();
        }
    }
    public double OverlayOpacityPercent
    {
        get => _overlayOpacityPercent;
        set
        {
            if (Set(ref _overlayOpacityPercent, value) is var changed && changed)
            {
                OnSettingsDraftChanged();
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
    public int ThemeIndex
    {
        get => _themeIndex;
        set
        {
            if (!Set(ref _themeIndex, value)) return;
            OnSettingsDraftChanged();
            Raise(nameof(SelectedThemeKey));
        }
    }
    private int _themeIndex;

    public bool LogToCsv { get => _logToCsv; set => SetSettingsDraft(ref _logToCsv, value); }
    public bool LogToJson { get => _logToJson; set => SetSettingsDraft(ref _logToJson, value); }
    public bool LogToJsonJournal { get => _logToJsonJournal; set => SetSettingsDraft(ref _logToJsonJournal, value); }
    public bool TelemetryExportEnabled { get => _telemetryExportEnabled; set => SetSettingsDraft(ref _telemetryExportEnabled, value); }
    public string TelemetryExportPortText
    {
        get => _telemetryExportPortText;
        set
        {
            if (!Set(ref _telemetryExportPortText, value)) return;
            OnSettingsDraftChanged();
            Raise(nameof(TelemetryExportEndpointText));
        }
    }
    public bool TelemetryExportListenAll
    {
        get => _telemetryExportListenAll;
        set
        {
            if (!Set(ref _telemetryExportListenAll, value)) return;
            OnSettingsDraftChanged();
            Raise(nameof(TelemetryExportEndpointText));
        }
    }
    public string TelemetryExportStatusText { get => _telemetryExportStatusText; private set => Set(ref _telemetryExportStatusText, value); }
    public string TelemetryExportEndpointText => TelemetryExportListenAll
        ? $"http://<host>:{TelemetryExportPortText}/values · http://<host>:{TelemetryExportPortText}/health\n"
          + $"Requer token: envie o cabeçalho Authorization: Bearer {TelemetryExportTokenText}"
        : $"http://127.0.0.1:{TelemetryExportPortText}/values · http://127.0.0.1:{TelemetryExportPortText}/health";
    public string TelemetryExportTokenText => string.IsNullOrEmpty(_config.TelemetryExport.AccessToken)
        ? "(será gerado ao aplicar)"
        : _config.TelemetryExport.AccessToken;
    public string LogDirectoryText => UserDataPaths.LogDirectory;
    public string SettingsMessage { get => _settingsMessage; private set => Set(ref _settingsMessage, value); }
}
