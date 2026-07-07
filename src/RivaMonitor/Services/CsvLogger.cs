using System.Globalization;
using System.IO;
using RivaMonitor.Models;

namespace RivaMonitor.Services;

public sealed class CsvLogger : IDisposable
{
    private readonly StreamWriter _writer;
    private readonly string[] _headers;
    private readonly string _filePath;
    private readonly object _lock = new();

    public CsvLogger(string filePath, IEnumerable<SensorType> types)
    {
        _filePath = filePath;
        _headers = ["Timestamp", .. types.Select(t => t.ToString())];
        _writer = new StreamWriter(filePath, false);
        _writer.WriteLine(string.Join(",", _headers));
        _writer.Flush();
    }

    public void Write(SensorData data)
    {
        lock (_lock)
        {
            var values = new List<string> { data.Timestamp.ToString("O") };
            for (int i = 1; i < _headers.Length; i++)
            {
                var type = Enum.Parse<SensorType>(_headers[i]);
                values.Add(data.GetValue(type).ToString("F2", CultureInfo.InvariantCulture));
            }
            _writer.WriteLine(string.Join(",", values));
            _writer.Flush();
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _writer.Flush();
            _writer.Dispose();
        }
    }
}
