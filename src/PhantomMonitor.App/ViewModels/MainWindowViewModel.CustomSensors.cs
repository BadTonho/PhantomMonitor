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

// Sensores customizados: edição, ordenação e salvamento das fórmulas (Custom#i).
public sealed partial class MainWindowViewModel
{
    public void AddCustomSensor()
    {
        if (CustomSensorEditors.Count >= CustomSensorEngine.MaxSensors)
        {
            CustomSensorsMessage = $"O limite de {CustomSensorEngine.MaxSensors} sensores customizados foi atingido.";
            return;
        }

        var name = "Novo sensor";
        var suffix = 2;
        while (CustomSensorEditors.Any(editor => string.Equals(editor.Name.Trim(), name, StringComparison.OrdinalIgnoreCase)))
            name = $"Novo sensor {suffix++}";

        CustomSensorEditors.Add(new CustomSensorEditorViewModel(name, "", "", CustomSensorEditors.Count));
        UpdateCustomSensorPositions();
        CustomSensorsMessage = "Sensor adicionado. Preencha a fórmula e salve para aplicar.";
    }

    public void RemoveCustomSensor(CustomSensorEditorViewModel editor)
    {
        if (!CustomSensorEditors.Remove(editor)) return;
        UpdateCustomSensorPositions();
        CustomSensorsMessage = "Sensor removido. Salve para aplicar a alteração.";
    }

    public void MoveCustomSensor(CustomSensorEditorViewModel editor, int offset)
    {
        var oldIndex = CustomSensorEditors.IndexOf(editor);
        var newIndex = oldIndex + offset;
        if (oldIndex < 0 || newIndex < 0 || newIndex >= CustomSensorEditors.Count) return;

        CustomSensorEditors.Move(oldIndex, newIndex);
        UpdateCustomSensorPositions();
        CustomSensorsMessage = "Ordem alterada. Salvar pode mudar os índices Custom#i usados em alertas.";
    }

    public void SaveCustomSensors()
    {
        if (CustomSensorEditors.Count > CustomSensorEngine.MaxSensors)
        {
            CustomSensorsMessage = $"O limite é de {CustomSensorEngine.MaxSensors} sensores customizados.";
            return;
        }

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var candidate = new List<CustomSensorConfig>(CustomSensorEditors.Count);
        for (var index = 0; index < CustomSensorEditors.Count; index++)
        {
            var editor = CustomSensorEditors[index];
            var name = editor.Name.Trim();
            var formula = editor.Formula.Trim();
            var unit = editor.Unit.Trim();

            if (string.IsNullOrWhiteSpace(name))
            {
                CustomSensorsMessage = $"Informe um nome para {editor.SeriesKey}.";
                return;
            }
            if (!names.Add(name))
            {
                CustomSensorsMessage = $"O nome '{name}' está duplicado. Use nomes únicos.";
                return;
            }
            if (!CustomSensorEngine.IsFormulaValid(formula))
            {
                CustomSensorsMessage = $"A fórmula de {editor.SeriesKey} ({name}) é inválida.";
                return;
            }

            candidate.Add(new CustomSensorConfig { Name = name, Formula = formula, Unit = unit });
        }

        if (CustomSensorConfigsEqual(_config.CustomSensors, candidate))
        {
            CustomSensorsMessage = "Nenhuma alteração para salvar.";
            return;
        }

        var previous = _config.CustomSensors;
        var wasRunning = IsRunning;
        var wasPaused = IsPaused;
        _config.CustomSensors = candidate;
        if (!SaveConfiguration())
        {
            _config.CustomSensors = previous;
            CustomSensorsMessage = "Não foi possível salvar o config.json. As fórmulas em execução não foram alteradas.";
            return;
        }

        ReplaceSensorService();
        LoadCustomSensorEditors();
        CustomSensorsMessage = wasRunning
            ? wasPaused
                ? "Sensores salvos e aplicados. Coleta retomada pausada; as estatísticas da sessão foram zeradas."
                : "Sensores salvos e aplicados; as estatísticas da sessão foram zeradas."
            : "Sensores salvos e aplicados à próxima coleta.";
    }

    private void LoadCustomSensorEditors()
    {
        CustomSensorEditors.Clear();
        for (var index = 0; index < _config.CustomSensors.Count; index++)
        {
            var sensor = _config.CustomSensors[index];
            CustomSensorEditors.Add(new CustomSensorEditorViewModel(sensor.Name, sensor.Formula, sensor.Unit, index));
        }
        UpdateCustomSensorPositions();
    }

    private void UpdateCustomSensorPositions()
    {
        for (var index = 0; index < CustomSensorEditors.Count; index++)
            CustomSensorEditors[index].SetPosition(index, CustomSensorEditors.Count);
    }

    private static bool CustomSensorConfigsEqual(IReadOnlyList<CustomSensorConfig> left, IReadOnlyList<CustomSensorConfig> right) =>
        left.Count == right.Count && left.Zip(right).All(pair =>
            string.Equals(pair.First.Name, pair.Second.Name, StringComparison.Ordinal)
            && string.Equals(pair.First.Formula, pair.Second.Formula, StringComparison.Ordinal)
            && string.Equals(pair.First.Unit, pair.Second.Unit, StringComparison.Ordinal));

}
