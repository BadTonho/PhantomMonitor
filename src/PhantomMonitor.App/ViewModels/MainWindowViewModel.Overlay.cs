using Avalonia.Threading;
using System.Collections.ObjectModel;
using PhantomMonitor.App.Services;
using PhantomMonitor.Models;
using PhantomMonitor.Services;
using PhantomMonitor.Services.Linux;
using PhantomMonitor.Services.Telemetry;

namespace PhantomMonitor.App.ViewModels;

// Overlay: alternância de visibilidade, métricas e aparência.
public sealed partial class MainWindowViewModel
{
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
        => ReconcileInventoryRows(group.Rows, plan);

    private void ReconcileInventoryRows(ObservableCollection<InventoryRowViewModel> rows, IReadOnlyList<InventoryRowPlan> plan)
    {
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
            row.Apply(entry.Source, entry.Value, entry.Unit, entry.Available, entry.Min, entry.Max, entry.Avg);
            var children = entry.Children ?? [];
            ReconcileInventoryRows(row.Children, children);
            position++;
        }
        // Sobras: o plano encolheu (série deixou de ser publicada); a casca
        // do grupo permanece.
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
    }

}
