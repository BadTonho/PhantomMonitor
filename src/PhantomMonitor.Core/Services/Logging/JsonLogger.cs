using System.IO;
using System.Text.Json;
using PhantomMonitor.Models;

namespace PhantomMonitor.Services;

public sealed class JsonLogger : IDisposable
{
    private readonly string _filePath;
    private readonly object _lock = new();
    private bool _writeFailureLogged;

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
            try
            {
                var dir = Path.GetDirectoryName(_filePath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

                // Escreve num temporário e substitui: leitores externos nunca veem JSON truncado.
                var json = JsonSerializer.Serialize(data, JsonOptions);
                var tmp = _filePath + ".tmp";
                File.WriteAllText(tmp, json + Environment.NewLine);
                File.Move(tmp, _filePath, overwrite: true);
                _writeFailureLogged = false;
            }
            catch (Exception ex)
            {
                // Com intervalo de 100 ms, uma falha persistente (ex.: arquivo travado por outro processo)
                // geraria milhares de linhas; registra só a primeira da sequência.
                if (!_writeFailureLogged)
                {
                    _writeFailureLogged = true;
                    ErrorLog.Write("JsonLogger.Write", ex);
                }
            }
        }
    }

    public void Dispose() { }
}
