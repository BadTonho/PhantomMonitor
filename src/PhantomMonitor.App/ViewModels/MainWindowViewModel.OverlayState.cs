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

// Propriedades derivadas do overlay (efetivas e prévia) e coleções de blocos.
public sealed partial class MainWindowViewModel
{
    public IReadOnlyList<string> OverlayRenderModeOptions { get; } = ["Janela flutuante", "Dentro do jogo"];
    public int OverlayRenderModeIndex
    {
        get => _overlayRenderModeIndex;
        set
        {
            if (!Set(ref _overlayRenderModeIndex, Math.Clamp(value, 0, 1))) return;
            OnSettingsDraftChanged();
            Raise(nameof(IsOverlayInGameModeSelected));
            Raise(nameof(IsOverlayTargetLaunchVisible));
        }
    }
    public string OverlayRenderMode => _config.Overlay.RenderMode;
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
    private double OverlayPreviewFont => Math.Clamp(_activeProfile?.FontSize ?? _overlayFontSizeSlider, 8, 20);
    private bool OverlayPreviewCompact => _overlayCompactModeSetting;
    private bool TryParseOverlayPreviewWidth(out int width)
        => int.TryParse(_overlayWidthText, NumberStyles.Integer, CultureInfo.CurrentCulture, out width)
            && width >= 140 && width <= 700;
    public double OverlayPreviewWidth => _activeProfile?.Width is { } profileWidth && profileWidth > 0
        ? Math.Clamp(profileWidth, 140, 700)
        : TryParseOverlayPreviewWidth(out var width) ? width : _overlayWidthEffective;
    public bool OverlayPreviewWidthInvalid => !TryParseOverlayPreviewWidth(out _);
    public double OverlayPreviewOpacity => Math.Clamp(
        _activeProfile?.Opacity ?? (_overlayOpacityPercent / 100.0), 0.1, 1.0);
    public double OverlayPreviewNameFontSize => Math.Max(7, OverlayPreviewFont - 2);
    public double OverlayPreviewValueFontSize => OverlayPreviewFont;
    public double OverlayPreviewSecondaryFontSize => Math.Max(7, OverlayPreviewFont - 3.5);
    public double OverlayPreviewProfileTagFontSize => Math.Max(7, OverlayPreviewFont - 2.5);
    public double OverlayPreviewContentSpacing => OverlayPreviewCompact ? 4 : 8;
    public Avalonia.Thickness OverlayPreviewRowMargin => OverlayPreviewCompact ? new(0, 0.5) : new(0, 2);
    public Avalonia.Thickness OverlayPreviewOuterMargin => OverlayPreviewCompact ? new(4) : new(8);
    public Avalonia.Thickness OverlayPreviewPadding => OverlayPreviewCompact ? new(6, 4) : new(12);
    public double OverlayPreviewFrametimeGraphHeight => OverlayPreviewCompact ? 18 : 24;
    public double OverlayEditorCanvasWidth { get => _overlayEditorCanvasWidth; private set => Set(ref _overlayEditorCanvasWidth, value); }
    public double OverlayEditorCanvasHeight { get => _overlayEditorCanvasHeight; private set => Set(ref _overlayEditorCanvasHeight, value); }

}
