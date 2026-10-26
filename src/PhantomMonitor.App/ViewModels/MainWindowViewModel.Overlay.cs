using Avalonia.Threading;
using PhantomMonitor.App.Services;
using PhantomMonitor.Models;
using PhantomMonitor.Services;
using PhantomMonitor.Services.Linux;
using PhantomMonitor.Services.Telemetry;

namespace PhantomMonitor.App.ViewModels;

// Overlay: alternância de visibilidade, métricas e aparência.
public sealed partial class MainWindowViewModel
{
    private IReadOnlyList<HardwareIdentity> _overlayHardwareDevices = [];
    public IReadOnlyList<HardwareIdentity> OverlayHardwareDevices => _overlayHardwareDevices;

    private void UpdateOverlayHardwareDevices(SensorData data)
    {
        if (_overlayHardwareDevices.SequenceEqual(data.HardwareDevices)) return;
        _overlayHardwareDevices = data.HardwareDevices.ToArray();
        Raise(nameof(OverlayHardwareDevices));
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
        PublishInGameOverlayFrame();
        SettingsMessage = _config.ShowOverlay
            ? _config.Overlay.RenderMode == "inGame"
                ? "Overlay ativado no modo dentro do jogo. Consulte Diagnóstico para ver o estado do renderer nativo."
                : "Overlay ativado."
            : "Overlay ocultado.";
    }

    private void UpdateOverlayRows(SensorData? data)
    {
        var categories = new OverlayConfigSelection(true, true, true, true, true, true, true, true, false);
        var categoryPlans = OverlayMetricsBuilder.Plan(data, categories);
        var instancePlans = OverlayMetricsBuilder.Plan(data,
            new OverlayConfigSelection(false, false, false, false, false, false, false, false, false), ["*"], includePrimarySeries: true);

        // Reconciliação de rows persistentes: rows são reutilizadas e só a string que
        // mudou dispara PropertyChanged. Sem isso, OverlayRows era Clear+re-add
        // por snapshot — o ItemsControl re-media/re-renderiza tudo (até 10x/s).
        // Whitelist em vigor (perfil > global) e rows extras (graphs P8.5/templates P6.6).
        ReconcileOverlayRows(categoryPlans.Concat(instancePlans)
            .GroupBy(plan => plan.Identity, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First()).ToArray());
        EnsureOverlayExtrasCurrent();
        UpdateOverlayExtraRows(data);
        UpdateOverlayAppliedExtraRows(data);
        ReconcileOverlayBlocks(categoryPlans, instancePlans);
        PublishInGameOverlayFrame();
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
                    row = new OverlayMetricViewModel(entry.Identity, entry.Name, entry.OverrideIdentity, entry.SeriesKey, entry.NumericValue);
                    rows.Insert(position, row);
                }
            }

            row.Name = entry.Name;
            row.Value = entry.Value;
            row.ShowFrametimeGraph = entry.ShowFrametimeGraph;
            row.UpdateSensorFormatting(entry.OverrideIdentity, entry.SeriesKey, entry.NumericValue,
                entry.ShowNameOverride, entry.IsIndividualSeries, entry.DeviceLabel);
            position++;
        }

        // Sobras: plan encolheu (série sumiu, opção desligada).
        while (rows.Count > position)
            rows.RemoveAt(rows.Count - 1);
    }

    private void RefreshOverlaySeriesOptions()
    {
        var options = _latestSensorData?.Series.Keys
            .Select(key => key.ToString())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(key => key, StringComparer.OrdinalIgnoreCase)
            .ToArray() ?? [];
        if (!OverlaySeriesOptions.SequenceEqual(options, StringComparer.OrdinalIgnoreCase))
            OverlaySeriesOptions = options;
    }

    private void RaiseOverlayAppearanceProperties()
    {
        Raise(nameof(OverlayFontSize));
        Raise(nameof(OverlayMetricNameFontFamily));
        Raise(nameof(OverlayMetricValueFontFamily));
        Raise(nameof(OverlayCompactMode));
        Raise(nameof(OverlayMetricNameFontSize));
        Raise(nameof(OverlayMetricValueFontSize));
        Raise(nameof(OverlayHeaderFontSize));
        Raise(nameof(OverlaySecondaryFontSize));
        Raise(nameof(OverlayProfileTagFontSize));
        Raise(nameof(OverlayOuterMargin));
        Raise(nameof(OverlayPadding));
        Raise(nameof(OverlayContentSpacing));
        Raise(nameof(OverlayHeaderSpacing));
        Raise(nameof(OverlayRowMargin));
        Raise(nameof(OverlayHideButtonMinSize));
        Raise(nameof(OverlayHideButtonPadding));
        Raise(nameof(OverlayBlockPadding));
        Raise(nameof(OverlayFrametimeGraphHeight));
        foreach (var block in _overlayAppliedBlocks)
            block.RefreshPresentation();
    }

}
