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

// Ajuste por série: edição, cadência/suavização e salvamento do bloco sensorTuning.
public sealed partial class MainWindowViewModel
{
    public void AddSensorTuning()
    {
        if (SensorTuningEditors.Count >= SensorTuningEngine.MaxTunings)
        {
            SensorTuningMessage = $"O limite de {SensorTuningEngine.MaxTunings} regras foi atingido.";
            return;
        }

        var series = SensorTuningSeriesOptions.FirstOrDefault() ?? "";
        var tuning = new SensorTuningConfig { Series = series };
        SensorTuningEditors.Add(new SensorTuningEditorViewModel(tuning, SensorTuningSeriesOptions, SensorTuningEditors.Count));
        UpdateSensorTuningPositions();
        SensorTuningMessage = "Regra adicionada. Configure a série e salve para aplicar.";
    }

    public void RemoveSensorTuning(SensorTuningEditorViewModel editor)
    {
        if (!SensorTuningEditors.Remove(editor)) return;
        UpdateSensorTuningPositions();
        SensorTuningMessage = "Regra removida. Salve para aplicar a alteração.";
    }

    public void MoveSensorTuning(SensorTuningEditorViewModel editor, int offset)
    {
        var oldIndex = SensorTuningEditors.IndexOf(editor);
        var newIndex = oldIndex + offset;
        if (oldIndex < 0 || newIndex < 0 || newIndex >= SensorTuningEditors.Count) return;

        SensorTuningEditors.Move(oldIndex, newIndex);
        UpdateSensorTuningPositions();
        SensorTuningMessage = "Ordem alterada. A última regra correspondente à série terá prioridade.";
    }

    public void SaveSensorTunings()
    {
        if (SensorTuningEditors.Count > SensorTuningEngine.MaxTunings)
        {
            SensorTuningMessage = $"O limite é de {SensorTuningEngine.MaxTunings} regras.";
            return;
        }

        var candidate = new List<SensorTuningConfig>(SensorTuningEditors.Count);
        for (var index = 0; index < SensorTuningEditors.Count; index++)
        {
            var editor = SensorTuningEditors[index];
            var series = editor.Series.Trim();
            if (string.IsNullOrWhiteSpace(series))
            {
                SensorTuningMessage = $"Informe uma série ou padrão para a regra {index + 1}.";
                return;
            }

            if (!int.TryParse(editor.IntervalText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var intervalMs)
                || intervalMs < 0
                || (intervalMs > 0 && intervalMs is < 100 or > 3_600_000))
            {
                SensorTuningMessage = $"O intervalo da regra {index + 1} deve ser 0 ou entre 100 e 3.600.000 ms.";
                return;
            }

            if (!int.TryParse(editor.SmoothingText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var smoothing)
                || smoothing is < 0 or > SensorTuningEngine.MaxSmoothing)
            {
                SensorTuningMessage = $"A suavização da regra {index + 1} deve ficar entre 0 e {SensorTuningEngine.MaxSmoothing}.";
                return;
            }

            if (intervalMs > 0)
                intervalMs = Math.Max(_config.UpdateIntervalMs, Math.Clamp(intervalMs, 100, 3_600_000));
            candidate.Add(new SensorTuningConfig { Series = series, IntervalMs = intervalMs, Smoothing = smoothing });
        }

        if (SensorTuningConfigsEqual(_config.SensorTunings, candidate))
        {
            SensorTuningMessage = "Nenhuma alteração para salvar.";
            return;
        }

        var previous = _config.SensorTunings;
        var wasRunning = IsRunning;
        var wasPaused = IsPaused;
        _config.SensorTunings = candidate;
        if (!SaveConfiguration())
        {
            _config.SensorTunings = previous;
            SensorTuningMessage = "Não foi possível salvar o config.json. As regras em execução não foram alteradas.";
            return;
        }

        ReplaceSensorService();
        LoadSensorTuningEditors();
        SensorTuningMessage = wasRunning
            ? wasPaused
                ? "Ajustes salvos e aplicados. Coleta continua pausada; as estatísticas da sessão foram zeradas."
                : "Ajustes salvos e aplicados; as estatísticas da sessão foram zeradas."
            : "Ajustes salvos e aplicados à próxima coleta.";
    }

    private void LoadSensorTuningEditors()
    {
        RefreshSensorTuningSeriesOptions();
        SensorTuningEditors.Clear();
        for (var index = 0; index < _config.SensorTunings.Count; index++)
            SensorTuningEditors.Add(new SensorTuningEditorViewModel(_config.SensorTunings[index], SensorTuningSeriesOptions, index));
        UpdateSensorTuningPositions();
    }

    private void RefreshSensorTuningSeriesOptions()
    {
        var series = new HashSet<string>(_config.SensorTunings.Select(tuning => tuning.Series), StringComparer.OrdinalIgnoreCase);
        foreach (var group in InventoryGroups)
            foreach (var row in group.Rows.Where(row => row.IsAvailable))
                series.Add(row.Key);

        var options = series.Where(value => !string.IsNullOrWhiteSpace(value))
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (SensorTuningSeriesOptions.SequenceEqual(options, StringComparer.OrdinalIgnoreCase)) return;

        SensorTuningSeriesOptions = options;
        foreach (var editor in SensorTuningEditors)
            editor.SeriesOptions = options;
    }

    private void UpdateSensorTuningPositions()
    {
        for (var index = 0; index < SensorTuningEditors.Count; index++)
            SensorTuningEditors[index].SetPosition(index, SensorTuningEditors.Count);
    }

    private static bool SensorTuningConfigsEqual(IReadOnlyList<SensorTuningConfig> left, IReadOnlyList<SensorTuningConfig> right) =>
        left.Count == right.Count && left.Zip(right).All(pair =>
            string.Equals(pair.First.Series, pair.Second.Series, StringComparison.Ordinal)
            && pair.First.IntervalMs == pair.Second.IntervalMs
            && pair.First.Smoothing == pair.Second.Smoothing);

}
