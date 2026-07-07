using System.IO;
using System.Text.Json;
using RivaMonitor.Models;

namespace RivaMonitor.Services;

public sealed class JsonLogger : IDisposable
{
    private readonly string _filePath;
    private readonly object _lock = new();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public JsonLogger(string filePath)
    {
        _filePath = filePath;
    }

    public void Write(SensorData data)
    {
        lock (_lock)
        {
            var json = JsonSerializer.Serialize(data, JsonOptions);
            File.WriteAllText(_filePath, json + Environment.NewLine);
        }
    }

    public void Dispose() { }
}
