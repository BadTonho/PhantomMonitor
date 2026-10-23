using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Collections.ObjectModel;
using System.Threading;
using Avalonia.Threading;
using PhantomMonitor.App.Services;
using PhantomMonitor.Models;
using PhantomMonitor.Services;
using PhantomMonitor.Services.Linux;
using PhantomMonitor.Services.Telemetry;

namespace PhantomMonitor.App.ViewModels;

// Overlay: alternância de janela, reconciliação de rows do overlay e do inventário,
// padrões de séries e aparências.
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
        var selection = new OverlayConfigSelection(
            OverlayShowCpu,
            OverlayShowGpu,
            OverlayShowRam,
            OverlayShowDisk,
            OverlayShowNetwork,
            OverlayShowFans,
            OverlayShowMotherboard,
            OverlayShowFps,
            _config.Overlay.ShowFrametimeGraph);

        // Reconciliação de rows persistentes: rows são reutilizadas e só a string que
        // mudou dispara PropertyChanged. Sem isso, OverlayRows era Clear+re-add
        // por snapshot — o ItemsControl re-media/re-renderiza tudo (até 10x/s).
        // Whitelist em vigor (perfil > global) e rows extras (graphs P8.5/templates P6.6).
        ReconcileOverlayRows(OverlayMetricsBuilder.Plan(data, selection, EffectiveOverlayShownSeries()));
        EnsureOverlayExtrasCurrent();
        UpdateOverlayExtraRows(data);
        ReconcileOverlayBlocks();
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

    private void LoadOverlaySeriesPatterns()
    {
        _overlaySeriesPatterns.Clear();
        foreach (var pattern in _config.Overlay.ShownSeries ?? ["Gpu*", "FanSpeed*", "Disk*"])
            _overlaySeriesPatterns.Add(new OverlaySeriesPatternViewModel(pattern));
    }

    public void AddDetectedOverlaySeries()
    {
        if (!SensorKey.TryParse(SelectedOverlaySeries, out var key) || key.IsPrimary)
        {
            SettingsMessage = "Selecione uma série por instância detectada.";
            return;
        }
        AddOverlaySeriesPattern(key.ToString());
    }

    public void AddManualOverlaySeriesPattern()
    {
        var pattern = OverlaySeriesPatternInput.Trim();
        if (!IsValidOverlaySeriesPattern(pattern))
        {
            SettingsMessage = "Informe uma chave de sensor válida ou um padrão com * no início/fim (ex.: Gpu*).";
            return;
        }
        AddOverlaySeriesPattern(pattern);
        OverlaySeriesPatternInput = "";
    }

    private void AddOverlaySeriesPattern(string pattern)
    {
        if (_overlaySeriesPatterns.Any(item => string.Equals(item.Pattern, pattern, StringComparison.OrdinalIgnoreCase)))
        {
            SettingsMessage = "Esse padrão já está na lista.";
            return;
        }
        _overlaySeriesPatterns.Add(new OverlaySeriesPatternViewModel(pattern));
        SettingsMessage = "Padrão adicionado. Salve para aplicar ao overlay.";
    }

    public void RemoveOverlaySeriesPattern(OverlaySeriesPatternViewModel item)
    {
        _overlaySeriesPatterns.Remove(item);
    }

    public void RestoreDefaultOverlaySeriesPatterns()
    {
        _overlaySeriesPatterns.Clear();
        foreach (var pattern in new[] { "Gpu*", "FanSpeed*", "Disk*" })
            _overlaySeriesPatterns.Add(new OverlaySeriesPatternViewModel(pattern));
        SettingsMessage = "Padrões do overlay restaurados. Salve para aplicar.";
    }

    private static bool IsValidOverlaySeriesPattern(string pattern)
    {
        if (pattern == "*") return true;
        if (pattern.Count(character => character == '*') == 0)
            return SensorKey.TryParse(pattern, out var key) && !key.IsPrimary && Enum.IsDefined(key.Type);
        if (pattern.Count(character => character == '*') != 1) return false;
        if (pattern.EndsWith('*')) return pattern.Length > 1;
        return pattern.StartsWith('*') && pattern.Length > 1;
    }

    private void RefreshOverlaySeriesOptions()
    {
        var options = _latestSensorData?.Series.Keys
            .Where(key => !key.IsPrimary)
            .Select(key => key.ToString())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(key => key, StringComparer.OrdinalIgnoreCase)
            .ToArray() ?? [];
        if (!OverlaySeriesOptions.SequenceEqual(options, StringComparer.OrdinalIgnoreCase))
            OverlaySeriesOptions = options;
        if (SelectedOverlaySeries is not null && !options.Contains(SelectedOverlaySeries, StringComparer.OrdinalIgnoreCase))
            SelectedOverlaySeries = null;
    }

}
