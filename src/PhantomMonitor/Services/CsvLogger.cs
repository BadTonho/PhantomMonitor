using System.Globalization;
using System.IO;
using PhantomMonitor.Models;

namespace PhantomMonitor.Services;

/// <summary>
/// CSV de telemetria com colunas dinâmicas: o cabeçalho é escrito na primeira escrita,
/// a partir das séries realmente presentes no primeiro snapshot (séries canônicas +
/// séries por instância como "GpuCore#1"), em vez de uma lista fixa de colunas.
/// Colunas existentes continuam no lugar mesmo se some hardware (ventoinha extra,
/// USB desligada...) — a célula fica em branco/0, sem desalinhar planilhas.
/// </summary>
public sealed class CsvLogger : IDisposable
{
    private readonly string _filePath;
    private readonly object _lock = new();
    private StreamWriter? _writer;
    private List<SensorKey> _columns = [];
    private bool _disposed;

    public CsvLogger(string filePath)
    {
        _filePath = filePath;
    }

    public void Write(SensorData data)
    {
        lock (_lock)
        {
            if (_disposed) return;

            // Write roda a cada snapshot na UI thread: falha de I/O (disco cheio, antivírus,
            // arquivo bloqueado) não deve propagar exceção para o app.
            try
            {
                _writer ??= OpenWriter(data);
                WriteRow(data);
            }
            catch (Exception ex)
            {
                ErrorLog.Write("CsvLogger.Write", ex);
            }
        }
    }

    private StreamWriter OpenWriter(SensorData data)
    {
        var dir = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        _columns = data.GetSeriesKeys();
        var writer = new StreamWriter(_filePath, false) { AutoFlush = false };

        // Cabeçalho fixo na abertura: coluna 0 é o timestamp, o resto são as séries do snapshot.
        writer.WriteLine(string.Join(",", ["Timestamp", .. _columns.Select(c => c.ToString())]));
        writer.Flush();
        return writer;
    }

    private void WriteRow(SensorData data)
    {
        var cells = new string[_columns.Count + 1];
        cells[0] = data.Timestamp.ToString("O", CultureInfo.InvariantCulture);
        for (int i = 0; i < _columns.Count; i++)
        {
            cells[i + 1] = data.GetValue(_columns[i]).ToString("F2", CultureInfo.InvariantCulture);
        }
        _writer!.WriteLine(string.Join(",", cells));
        _writer.Flush();
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;
            try { _writer?.Flush(); } catch { /* arquivo pode ter sido removido */ }
            _writer?.Dispose();
        }
    }
}
