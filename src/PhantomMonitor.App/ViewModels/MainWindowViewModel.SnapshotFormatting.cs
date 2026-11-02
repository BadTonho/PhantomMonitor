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

// Formatação de valores de sensores e histórico.
public sealed partial class MainWindowViewModel
{
    private static bool Has(SensorData data, SensorType type) => data.Values.ContainsKey(type);

    private static string Percent(SensorData data, SensorType type) =>
        Has(data, type) ? $"{data.GetValue(type):F0}%" : "—";

    private static string Temperature(SensorData data, SensorType type) =>
        Has(data, type) ? $"{data.GetValue(type):F0} °C" : "—";

    private static string Clock(SensorData data, SensorType type)
    {
        // Primeira chave da categoria (canônicas precedem as por instância em
        // GetSeriesKeys). Loop explícito em vez de FirstOrDefault: o default do
        // struct (enum 0) casava com `key.Type == type` quando nenhuma chave existe.
        var keys = data.GetSeriesKeys();
        for (var i = 0; i < keys.Count; i++)
        {
            if (keys[i].Type != type) continue;
            var key = keys[i];
            if (key.IsPrimary || data.Series.ContainsKey(key))
            {
                var estimate = data.GetLabel(key).Contains("aprox", StringComparison.OrdinalIgnoreCase) ? "≈ " : "";
                return $"{estimate}{data.GetValue(key):F0} MHz";
            }
            return "—";
        }
        return "—";
    }

    private static string MemoryPair(SensorData data, SensorType usedType, SensorType totalType, bool bytesAreGb = false)
    {
        if (!Has(data, usedType) || !Has(data, totalType)) return "—";
        var used = data.GetValue(usedType);
        var total = data.GetValue(totalType);
        if (bytesAreGb) return $"{used:F1} / {total:F1} GB";
        return $"{used / 1024:F1} / {total / 1024:F1} GB";
    }

    private static string Rate(SensorData data, SensorType type)
    {
        if (!Has(data, type)) return "—";
        return OverlayMetricsBuilder.RateText(data.GetValue(type));
    }

    private static void AddHistory(Queue<double> history, double value, bool hasValue)
    {
        if (!hasValue) return;
        history.Enqueue(value);
        while (history.Count > HistoryLength) history.Dequeue();
    }

}
