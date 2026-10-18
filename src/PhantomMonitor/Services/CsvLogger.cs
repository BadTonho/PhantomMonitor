using System.Globalization;
using System.IO;
using PhantomMonitor.Models;

namespace PhantomMonitor.Services;

public sealed class CsvLogger : IDisposable
{
    private readonly StreamWriter _writer;
    private readonly SensorType[] _types;
    private readonly object _lock = new();
    private bool _disposed;

    public CsvLogger(string filePath, IEnumerable<SensorType> types)
    {
        // Tipos resolvidos uma única vez (antes havia Enum.Parse por coluna a cada escrita).
        _types = types.ToArray();

        var dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        _writer = new StreamWriter(filePath, false) { AutoFlush = false };
        _writer.WriteLine(string.Join(",", ["Timestamp", .. _types.Select(t => t.ToString())]));
        _writer.Flush();
    }

    public void Write(SensorData data)
    {
        lock (_lock)
        {
            if (_disposed) return;

            var values = new string[_types.Length + 1];
            values[0] = data.Timestamp.ToString("O", CultureInfo.InvariantCulture);
            for (int i = 0; i < _types.Length; i++)
            {
                values[i + 1] = data.GetValue(_types[i]).ToString("F2", CultureInfo.InvariantCulture);
            }
            _writer.WriteLine(string.Join(",", values));
            _writer.Flush();
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;
            _writer.Flush();
            _writer.Dispose();
        }
    }
}
