using System.Globalization;
using System.Runtime.CompilerServices;
using Avalonia.Media;
using Avalonia.Threading;
using PhantomMonitor.App.Services;
using PhantomMonitor.Services.Update;

namespace PhantomMonitor.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    private enum SettingsSaveFeedbackState { Idle, Succeeded, Failed }

    private DispatcherTimer? _settingsSaveConfirmationTimer;
    private string _settingsSaveMessage = "";
    private bool _settingsSaveConfirmationVisible;
    private bool _settingsSaveConfirmedSinceLastEdit;
    private SettingsSaveFeedbackState _settingsSaveFeedbackState;
    private bool _isInitializingSettingsDrafts = true;
    private bool? _hasPendingSettingsChanges;

    public bool HasPendingSettingsChanges
        => _settingsSaveConfirmedSinceLastEdit
            ? false
            : _hasPendingSettingsChanges ??= CalculateHasPendingSettingsChanges();

    private bool CalculateHasPendingSettingsChanges()
        => IsIntervalEdited
            || IsOverlayFontSizeDraftDifferent()
            || IsOverlayWidthDraftDifferent()
            || !double.IsFinite(OverlayOpacityPercent)
            || Math.Abs(OverlayOpacityPercent - _config.OverlayOpacity * 100) > 0.0001
            || !double.IsFinite(OverlayBackgroundOpacityPercent)
            || Math.Abs(OverlayBackgroundOpacityPercent - _config.Overlay.BackgroundOpacity * 100) > 0.0001
            || IsColorDraftDifferent(OverlayBackgroundColorText, _config.Overlay.BackgroundColor)
            || OverlayBorderEnabled != _config.Overlay.BorderEnabled
            || IsColorDraftDifferent(OverlayBorderColorText, _config.Overlay.BorderColor)
            || !string.Equals(OverlayFontFamilyDraftValue, _config.Overlay.FontFamily ?? "", StringComparison.Ordinal)
            || OverlayCompactModeSetting != _config.Overlay.CompactMode
            || OverlayShowGpuModelSetting != _config.Overlay.ShowGpuModel
            || (ThemeIndex == 1 ? "slate" : "neptune") != _config.Theme
            || ThemePalettesDraftChanged
            || LogToCsv != _config.LogToCsv
            || LogToJson != _config.LogToJson
            || LogToJsonJournal != _config.LogToJsonJournal
            || TelemetryExportEnabled != _config.TelemetryExport.Enabled
            || IsTelemetryPortDraftDifferent()
            || TelemetryExportListenAll != _config.TelemetryExport.ListenAll
            || UpdateAutoCheck != _config.Update.AutoCheck
            || UpdateBetaChannel != _config.Update.CheckBeta
            || IsUpdateRepositoryDraftDifferent()
            || (OverlayRenderModeIndex == 1 ? "inGame" : "floating") != _config.Overlay.RenderMode
            || OverlayLayoutHasChanges()
            || OverlayInGameTargetsChanged(_config.Overlay.InGameTargets.Select(CloneOverlayTarget).ToList())
            || !string.Equals(_overlayHotkeyDraft, _config.ToggleOverlayHotkey, StringComparison.OrdinalIgnoreCase);

    public string SettingsSaveMessage
    {
        get => _settingsSaveMessage;
        private set
        {
            if (!Set(ref _settingsSaveMessage, value)) return;
            Raise(nameof(HasSettingsSaveMessage));
            Raise(nameof(SettingsSavePanelVisible));
        }
    }

    public bool HasSettingsSaveMessage => SettingsSaveMessage.Length > 0;
    public bool SettingsSavePanelVisible
        => HasPendingSettingsChanges || _settingsSaveConfirmationVisible || HasSettingsSaveMessage;
    public bool SettingsSaveButtonVisible
        => HasPendingSettingsChanges || _settingsSaveFeedbackState == SettingsSaveFeedbackState.Succeeded;
    public IBrush SettingsSaveButtonBrush => ResolveBrush(_settingsSaveFeedbackState switch
    {
        SettingsSaveFeedbackState.Succeeded => "AccentGreenBrush",
        SettingsSaveFeedbackState.Failed => "AccentHotBrush",
        _ => "AccentWarningBrush"
    });

    private bool IsOverlayFontSizeDraftDifferent()
    {
        if (!double.TryParse(OverlayFontSizeText, NumberStyles.Float, CultureInfo.CurrentCulture, out var value)
            || !double.IsFinite(value) || value is < 8 or > 20
            || Math.Abs(value * 2 - Math.Round(value * 2)) >= 0.000001)
            return true;
        return Math.Abs(value - _config.Overlay.FontSize) > 0.000001;
    }

    private bool IsOverlayWidthDraftDifferent()
        => !int.TryParse(OverlayWidthText, NumberStyles.Integer, CultureInfo.CurrentCulture, out var value)
            || value is < 140 or > 700
            || value != _config.Overlay.Width;

    private bool IsTelemetryPortDraftDifferent()
        => !int.TryParse(TelemetryExportPortText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            || value is < 1024 or > 65_535
            || value != _config.TelemetryExport.Port;

    private bool IsUpdateRepositoryDraftDifferent()
    {
        var draft = UpdateRepositoryText.Trim();
        return draft.Length > 0 && UpdateService.NormalizeRepository(draft) is null
            || !string.Equals(draft, (_config.Update.Repository ?? "").Trim(), StringComparison.Ordinal);
    }

    private static bool IsColorDraftDifferent(string draft, string? saved)
    {
        var normalized = (draft ?? "").Trim();
        if (normalized.Length == 0) return !string.IsNullOrEmpty(saved);
        return normalized.Length != 7
            || !OverlayBrushHelper.TryParseColor(normalized, out _)
            || !string.Equals(normalized, saved ?? "", StringComparison.OrdinalIgnoreCase);
    }

    private bool SetSettingsDraft<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (!Set(ref field, value, propertyName)) return false;
        OnSettingsDraftChanged();
        return true;
    }

    private void OnSettingsDraftChanged()
    {
        if (_isInitializingSettingsDrafts) return;
        _settingsSaveConfirmedSinceLastEdit = false;
        StopSettingsSaveConfirmationTimer();
        if (_settingsSaveConfirmationVisible) _settingsSaveConfirmationVisible = false;
        SetSettingsSaveFeedbackState(SettingsSaveFeedbackState.Idle);
        SettingsSaveMessage = "";
        RefreshSettingsPendingState();
    }

    private void RefreshSettingsPendingState()
    {
        var hasPending = !_settingsSaveConfirmedSinceLastEdit && CalculateHasPendingSettingsChanges();
        if (_hasPendingSettingsChanges != hasPending)
        {
            _hasPendingSettingsChanges = hasPending;
            Raise(nameof(HasPendingSettingsChanges));
            Raise(nameof(SettingsSaveButtonVisible));
        }
        Raise(nameof(SettingsSavePanelVisible));
    }

    private void BeginSettingsSaveAttempt()
    {
        StopSettingsSaveConfirmationTimer();
        _settingsSaveConfirmationVisible = false;
        _settingsSaveConfirmedSinceLastEdit = false;
        SetSettingsSaveFeedbackState(SettingsSaveFeedbackState.Idle);
        SettingsSaveMessage = "";
        Raise(nameof(SettingsSavePanelVisible));
    }

    private void ShowSettingsSaveConfirmation(string message)
    {
        _settingsSaveConfirmedSinceLastEdit = true;
        if (_hasPendingSettingsChanges != false)
        {
            _hasPendingSettingsChanges = false;
            Raise(nameof(HasPendingSettingsChanges));
            Raise(nameof(SettingsSaveButtonVisible));
        }
        SettingsSaveMessage = message;
        _settingsSaveConfirmationVisible = true;
        SetSettingsSaveFeedbackState(SettingsSaveFeedbackState.Succeeded);
        Raise(nameof(SettingsSavePanelVisible));
        StartSettingsSaveConfirmationTimer();
    }

    private void ShowSettingsSaveFailure(string message)
    {
        _settingsSaveConfirmedSinceLastEdit = false;
        _settingsSaveConfirmationVisible = false;
        SettingsSaveMessage = string.IsNullOrWhiteSpace(message)
            ? "Não foi possível salvar as configurações."
            : message;
        SetSettingsSaveFeedbackState(SettingsSaveFeedbackState.Failed);
        RefreshSettingsPendingState();
        StartSettingsSaveConfirmationTimer();
    }

    private void StartSettingsSaveConfirmationTimer()
    {
        StopSettingsSaveConfirmationTimer();
        _settingsSaveConfirmationTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _settingsSaveConfirmationTimer.Tick += OnSettingsSaveConfirmationTimerTick;
        _settingsSaveConfirmationTimer.Start();
    }

    private void OnSettingsSaveConfirmationTimerTick(object? sender, EventArgs e)
    {
        StopSettingsSaveConfirmationTimer();
        SetSettingsSaveFeedbackState(SettingsSaveFeedbackState.Idle);
        if (_settingsSaveConfirmedSinceLastEdit)
        {
            _settingsSaveConfirmationVisible = false;
            SettingsSaveMessage = "";
        }
        Raise(nameof(SettingsSavePanelVisible));
    }

    private void SetSettingsSaveFeedbackState(SettingsSaveFeedbackState state)
    {
        if (_settingsSaveFeedbackState == state) return;
        _settingsSaveFeedbackState = state;
        Raise(nameof(SettingsSaveButtonBrush));
        Raise(nameof(SettingsSaveButtonVisible));
    }

    private void StopSettingsSaveConfirmationTimer()
    {
        _settingsSaveConfirmationTimer?.Stop();
        _settingsSaveConfirmationTimer = null;
    }

    private void DisposeSettingsSaveConfirmationTimer()
    {
        StopSettingsSaveConfirmationTimer();
    }

    private void FinishSettingsDraftInitialization()
    {
        _isInitializingSettingsDrafts = false;
        RefreshSettingsPendingState();
    }
}
