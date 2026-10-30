using System.Globalization;
using PhantomMonitor.App.Services.Hotkeys;
using PhantomMonitor.Services;
using PhantomMonitor.Services.Update;

namespace PhantomMonitor.App.ViewModels;

// Orquestra validação, persistência e aplicação das configurações da interface.
public sealed partial class MainWindowViewModel
{
    public async Task SaveSettingsAsync()
    {
        if (IsSavingSettings) return;
        IsSavingSettings = true;
        try { await SaveSettingsCoreAsync(); }
        finally { IsSavingSettings = false; }
    }

    private async Task SaveSettingsCoreAsync()
    {
        if (!double.TryParse(OverlayFontSizeText, NumberStyles.Float, CultureInfo.CurrentCulture, out var overlayFontSize)
            || !double.IsFinite(overlayFontSize)
            || overlayFontSize is < 8 or > 20
            || Math.Abs(overlayFontSize * 2 - Math.Round(overlayFontSize * 2)) >= 0.000001)
        {
            SettingsSaveMessage = "O tamanho da fonte deve ficar entre 8 e 20 pt, em passos de 0,5 pt.";
            return;
        }

        if (!int.TryParse(OverlayWidthText, NumberStyles.Integer, CultureInfo.CurrentCulture, out var overlayWidth)
            || overlayWidth is < 140 or > 700)
        {
            SettingsSaveMessage = "A largura do overlay deve ficar entre 140 e 700 pixels.";
            return;
        }

        if (!double.IsFinite(OverlayOpacityPercent) || OverlayOpacityPercent is < 10 or > 100)
        {
            SettingsSaveMessage = "A opacidade do overlay deve ficar entre 10% e 100%.";
            return;
        }

        if (!double.IsFinite(OverlayBackgroundOpacityPercent) || OverlayBackgroundOpacityPercent is < 0 or > 100)
        {
            SettingsSaveMessage = "A opacidade do fundo deve ficar entre 0% e 100%.";
            return;
        }

        var backgroundColor = OverlayBackgroundColorText.Trim();
        if (backgroundColor.Length > 0
            && (backgroundColor.Length != 7 || !OverlayBrushHelper.TryParseColor(backgroundColor, out _)))
        {
            SettingsSaveMessage = "A cor do fundo deve usar o formato hexadecimal #RRGGBB.";
            return;
        }

        var borderColor = OverlayBorderColorText.Trim();
        if (borderColor.Length > 0
            && (borderColor.Length != 7 || !OverlayBrushHelper.TryParseColor(borderColor, out _)))
        {
            SettingsSaveMessage = "A cor da borda deve usar o formato hexadecimal #RRGGBB.";
            return;
        }

        var fontFamily = OverlayFontFamilyDraftValue;
        if (fontFamily.Length > 128 || fontFamily.Any(char.IsControl))
        {
            SettingsSaveMessage = "O nome da fonte deve ter até 128 caracteres.";
            return;
        }

        int? newInterval = null;
        if (IsIntervalEdited)
        {
            if (!int.TryParse(UpdateIntervalText, NumberStyles.Integer, CultureInfo.CurrentCulture, out var parsed)
                || parsed is < 100 or > 60_000)
            {
                SettingsSaveMessage = "Informe um intervalo entre 100 e 60.000 ms.";
                return;
            }
            newInterval = parsed;
        }

        if (!int.TryParse(TelemetryExportPortText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var telemetryPort)
            || telemetryPort is < 1024 or > 65_535)
        {
            SettingsSaveMessage = "A porta do exportador HTTP deve ficar entre 1024 e 65.535.";
            return;
        }

        // Repository vazio usa o repositório oficial (decisão tomada no serviço).
        var updateRepository = UpdateRepositoryText.Trim();
        if (updateRepository.Length > 0 && UpdateService.NormalizeRepository(updateRepository) is null)
        {
            SettingsSaveMessage = "O repositório deve estar como owner/repo ou em uma URL do GitHub.";
            return;
        }

        var oldInterval = _config.UpdateIntervalMs;
        var oldOverlayHotkey = _config.ToggleOverlayHotkey;
        var oldActiveOverlayHotkey = _activeOverlayHotkey;
        var hotkeyChanged = !string.Equals(_overlayHotkeyDraft, oldOverlayHotkey, StringComparison.OrdinalIgnoreCase);
        var hotkeyTransition = new HotkeyTransition(oldActiveOverlayHotkey, ApplyOverlayHotkeyAsync);
        var oldTheme = _config.Theme;
        var oldThemePalettes = CloneThemePalettes(_config.ThemePalettes);
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
        var oldOverlayBackgroundColor = _config.Overlay.BackgroundColor;
        var oldOverlayBackgroundOpacity = _config.Overlay.BackgroundOpacity;
        var oldOverlayBorderEnabled = _config.Overlay.BorderEnabled;
        var oldOverlayBorderColor = _config.Overlay.BorderColor;
        var oldOverlayFontFamily = _config.Overlay.FontFamily;
        var oldOverlayFontSize = _config.Overlay.FontSize;
        var oldOverlayCompactMode = _config.Overlay.CompactMode;
        var oldOverlayShowGpuModel = _config.Overlay.ShowGpuModel;
        var oldOverlayRenderMode = _config.Overlay.RenderMode;
        var oldOverlayTargets = _config.Overlay.InGameTargets.Select(CloneOverlayTarget).ToList();
        var oldOverlayBlocks = (_config.Overlay.Blocks ?? []).Select(CloneOverlayBlock).ToList();
        var oldOverlayHiddenMetrics = (_config.Overlay.HiddenMetrics ?? []).ToList();
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
        var themePalettesChanged = ThemePalettesDraftChanged;
        var themeAppearanceChanged = themeChanged || themePalettesChanged;
        var overlayVisualChanged = overlayWidth != oldOverlayWidth
            || Math.Abs(OverlayOpacityPercent / 100.0 - oldOverlayOpacity) > 0.000001
            || Math.Abs(OverlayBackgroundOpacityPercent / 100.0 - oldOverlayBackgroundOpacity) > 0.000001
            || !string.Equals(backgroundColor, oldOverlayBackgroundColor ?? "", StringComparison.OrdinalIgnoreCase)
            || OverlayBorderEnabled != oldOverlayBorderEnabled
            || !string.Equals(borderColor, oldOverlayBorderColor ?? "", StringComparison.OrdinalIgnoreCase);
        var overlayAppearanceChanged = Math.Abs(overlayFontSize - oldOverlayFontSize) > 0.000001
            || !string.Equals(fontFamily, oldOverlayFontFamily ?? "", StringComparison.Ordinal)
            || OverlayCompactModeSetting != oldOverlayCompactMode
            || OverlayShowGpuModelSetting != oldOverlayShowGpuModel;
        var overlayLayoutChanged = OverlayLayoutHasChanges();
        var overlayRenderModeChanged = (OverlayRenderModeIndex == 1 ? "inGame" : "floating") != oldOverlayRenderMode;
        var overlayTargetsChanged = OverlayInGameTargetsChanged(oldOverlayTargets);
        var overlayChanged = overlayLayoutChanged
            || overlayVisualChanged
            || overlayAppearanceChanged
            || overlayRenderModeChanged
            || overlayTargetsChanged;

        // A failed startup registration must not prevent unrelated settings from being saved.
        // Saving with no other changes retries the configured shortcut explicitly.
        var otherSettingsChanged = intervalChanged || loggingChanged || telemetryChanged || overlayChanged
            || updateChanged || themeAppearanceChanged;
        var hotkeyNeedsApply = !string.Equals(_overlayHotkeyDraft, oldActiveOverlayHotkey, StringComparison.OrdinalIgnoreCase)
            && (hotkeyChanged || !otherSettingsChanged);
        if (hotkeyChanged || hotkeyNeedsApply)
        {
            if (_overlayHotkeyDraft.Length > 0 && !HotkeyParser.TryParse(_overlayHotkeyDraft, out _, out _))
            {
                OverlayHotkeyStatus = "A combinação do overlay é inválida.";
                SettingsSaveMessage = OverlayHotkeyStatus;
                return;
            }
            if (HotkeyCapture.SameCombination(_overlayHotkeyDraft, _config.PauseMonitorHotkey))
            {
                OverlayHotkeyStatus = "Esta combinação já está configurada para pausar o monitoramento.";
                SettingsSaveMessage = OverlayHotkeyStatus;
                return;
            }
        }

        if (hotkeyNeedsApply)
        {
            OverlayHotkeyStatus = "Configurando atalho global…";
            var result = await hotkeyTransition.StageAsync(_overlayHotkeyDraft);
            if (!result.Success)
            {
                OverlayHotkeyStatus = result.Message;
                SettingsSaveMessage = result.Message;
                return;
            }
            OverlayHotkeyStatus = _overlayHotkeyDraft.Length == 0 ? "Atalho desativado." : $"Atalho ativo: {result.Display}.";
        }

        if (!intervalChanged && !loggingChanged && !telemetryChanged && !overlayChanged && !updateChanged
            && !themeAppearanceChanged && !hotkeyChanged)
        {
            ConfigureTelemetryExporter();
            SettingsSaveMessage = hotkeyNeedsApply ? "Atalho aplicado." : "Nenhuma alteração para salvar.";
            RefreshSettingsPendingState();
            ShowSettingsSaveConfirmation(SettingsSaveMessage);
            return;
        }

        if (newInterval.HasValue) _config.UpdateIntervalMs = newInterval.Value;
        _config.ToggleOverlayHotkey = _overlayHotkeyDraft;
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
        _config.ThemePalettes = CloneThemePalettesDraft();
        _config.Overlay.Width = overlayWidth;
        _config.OverlayOpacity = OverlayOpacityPercent / 100.0;
        _config.Overlay.BackgroundColor = backgroundColor.Length == 0 ? null : backgroundColor.ToUpperInvariant();
        _config.Overlay.BackgroundOpacity = OverlayBackgroundOpacityPercent / 100.0;
        _config.Overlay.BorderEnabled = OverlayBorderEnabled;
        _config.Overlay.BorderColor = borderColor.Length == 0 ? null : borderColor.ToUpperInvariant();
        _config.Overlay.FontFamily = fontFamily;
        _config.Overlay.FontSize = overlayFontSize;
        _config.Overlay.CompactMode = OverlayCompactModeSetting;
        _config.Overlay.ShowGpuModel = OverlayShowGpuModelSetting;
        _config.Overlay.RenderMode = OverlayRenderModeIndex == 1 ? "inGame" : "floating";
        _config.Overlay.InGameTargets = OverlayInGameTargets.Select(CloneOverlayTarget).ToList();
        CopyOverlayLayoutToConfig();

        if (!SaveConfiguration())
        {
            _config.UpdateIntervalMs = oldInterval;
            _config.ToggleOverlayHotkey = oldOverlayHotkey;
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
            _config.ThemePalettes = oldThemePalettes;
            _config.Overlay.Width = oldOverlayWidth;
            _config.OverlayOpacity = oldOverlayOpacity;
            _config.Overlay.BackgroundColor = oldOverlayBackgroundColor;
            _config.Overlay.BackgroundOpacity = oldOverlayBackgroundOpacity;
            _config.Overlay.BorderEnabled = oldOverlayBorderEnabled;
            _config.Overlay.BorderColor = oldOverlayBorderColor;
            _config.Overlay.FontFamily = oldOverlayFontFamily ?? "";
            _config.Overlay.FontSize = oldOverlayFontSize;
            _config.Overlay.CompactMode = oldOverlayCompactMode;
            _config.Overlay.ShowGpuModel = oldOverlayShowGpuModel;
            _config.Overlay.RenderMode = oldOverlayRenderMode;
            _config.Overlay.InGameTargets = oldOverlayTargets;
            _config.Overlay.Blocks = oldOverlayBlocks;
            _config.Overlay.HiddenMetrics = oldOverlayHiddenMetrics;
            _config.Overlay.Graphs = oldOverlayGraphs;
            _config.Overlay.Templates = oldOverlayTemplates;
            if (hotkeyNeedsApply)
            {
                var restored = await hotkeyTransition.RollbackAsync();
                OverlayHotkeyStatus = restored.Success
                    ? "Não foi possível salvar. O atalho anterior foi restaurado."
                    : $"Não foi possível salvar nem restaurar o atalho anterior. Atalho ativo: {(_activeOverlayHotkeyDisplay.Length == 0 ? "nenhum" : _activeOverlayHotkeyDisplay)}.";
                if (!restored.Success)
                {
                    SettingsSaveMessage = OverlayHotkeyStatus;
                    RefreshSettingsPendingState();
                    return;
                }
            }
            SettingsSaveMessage = "Não foi possível salvar o config.json. As opções em execução não foram alteradas.";
            RefreshSettingsPendingState();
            return;
        }

        _config.Overlay.BlocksMigrated = false;
        if (overlayRenderModeChanged)
        {
            Raise(nameof(OverlayRenderMode));
            Raise(nameof(IsInGameOverlayDiagnosticsVisible));
        }
        if (overlayLayoutChanged)
            ApplyOverlayLayoutDraft();

        // Fase 3 — troca de dicionário em runtime (isolada ou junto das demais
        // alterações). As views reagem pelos DynamicResource e a VM repinta os
        // pincéis resolvidos por token no evento ThemeChanged.
        if (themeAppearanceChanged)
        {
            _config.ThemePalettes.TryGetValue(_config.Theme, out var activePalette);
            App.ApplyTheme(_config.Theme, activePalette);
        }

        // Tema mudou sozinho: nada mais a reconfigurar; mensagem dedicada.
        if (themeAppearanceChanged && !intervalChanged && !loggingChanged && !telemetryChanged
            && !updateChanged && !overlayChanged && !hotkeyChanged)
        {
            SettingsSaveMessage = themePalettesChanged && !themeChanged
                ? "Paleta salva e aplicada sem reiniciar."
                : "Tema e paleta salvos e aplicados sem reiniciar.";
            RefreshSettingsPendingState();
            ShowSettingsSaveConfirmation(SettingsSaveMessage);
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
        ConfigureInGameOverlayHeartbeat();
        OverlayWidthText = _config.Overlay.Width.ToString(CultureInfo.CurrentCulture);
        OverlayOpacityPercent = _config.OverlayOpacity * 100;
        OverlayBackgroundOpacityPercent = _config.Overlay.BackgroundOpacity * 100;
        OverlayBackgroundColorText = _config.Overlay.BackgroundColor ?? "";
        OverlayBorderEnabled = _config.Overlay.BorderEnabled;
        OverlayBorderColorText = _config.Overlay.BorderColor ?? "";
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
        SettingsSaveMessage = intervalChanged
            ? collectionWasRunning
                ? collectionWasPaused
                    ? "Coleta reiniciada pausada; as estatísticas da sessão foram zeradas."
                    : "Coleta reiniciada; as estatísticas da sessão foram zeradas."
                : "Intervalo salvo. Será usado quando a coleta for iniciada."
            : overlayChanged && !loggingChanged && !telemetryChanged
                ? _config.Overlay.RenderMode == "inGame"
                    ? "Modo dentro do jogo salvo. Consulte Diagnóstico para ver a camada ou hook nativo."
                    : "Opções do overlay salvas e aplicadas."
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

        if (updateChanged && !intervalChanged && !loggingChanged && !telemetryChanged
            && !overlayChanged && !themeAppearanceChanged && !hotkeyChanged)
        {
            SettingsSaveMessage = "Preferências de atualização salvas.";
        }
        else if (hotkeyChanged && !intervalChanged && !loggingChanged && !telemetryChanged
            && !overlayChanged && !themeAppearanceChanged && !updateChanged)
            SettingsSaveMessage = "Atalho do overlay salvo e aplicado.";

        RefreshSettingsPendingState();
        ShowSettingsSaveConfirmation(SettingsSaveMessage);
    }

}
