using System.Globalization;
using System.IO;
using PhantomMonitor.Models;

namespace PhantomMonitor.Services;

/// <summary>
/// CSV de telemetria com colunas dinâmicas: o cabeçalho é escrito na primeira escrita,
/// a partir das séries presentes. Novas séries abrem um segmento .partNNN.csv
/// com a união das colunas; séries ausentes deixam células vazias.
/// </summary>
public sealed class CsvLogger : IDisposable
{
    private readonly string _filePath;
    private readonly object _lock = new();
    private StreamWriter? _writer;
    private List<SensorKey> _columns = [];
    private bool _disposed;
    private int _segment;

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
                var additions = data.GetSeriesKeys().Except(_columns).ToArray();
                if (_writer is not null && additions.Length > 0)
                {
                    _writer.Dispose();
                    _writer = null;
                }
                _columns.AddRange(additions);
                _writer ??= OpenWriter();
                WriteRow(data);
            }
            catch (Exception ex)
            {
                ErrorLog.Write("CsvLogger.Write", ex);
            }
        }
    }

    private StreamWriter OpenWriter()
    {
        var dir = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        var path = _segment == 0 ? _filePath : Path.Combine(dir ?? "",
            $"{Path.GetFileNameWithoutExtension(_filePath)}.part{_segment + 1:D3}{Path.GetExtension(_filePath)}");
        var writer = new StreamWriter(path, false) { AutoFlush = false };

        // Cabeçalho fixo na abertura: coluna 0 é o timestamp, o resto são as séries do snapshot.
        writer.WriteLine(string.Join(",", ["Timestamp", .. _columns.Select(c => c.ToString())]));
        writer.Flush();
        _segment++;
        return writer;
    }

    private void WriteRow(SensorData data)
    {
        var cells = new string[_columns.Count + 1];
        cells[0] = data.Timestamp.ToString("O", CultureInfo.InvariantCulture);
        for (int i = 0; i < _columns.Count; i++)
        {
            var key = _columns[i];
            var present = key.IsPrimary ? data.Values.ContainsKey(key.Type) : data.Series.ContainsKey(key);
            cells[i + 1] = present ? data.GetValue(key).ToString("F2", CultureInfo.InvariantCulture) : "";
        }
        _writer!.WriteLine(string.Join(",", cells));
        _writer.Flush();
    }

    /// <summary>
    /// Linha de marcador ("#marker") para delimitar segmentos de sessão/benchmark
    /// (reset de estatísticas, aquecimento, fim de teste). Clientes que só leem as
    /// colunas numéricas ignoram a linha — planilhas mantêm as colunas alinhadas.
    /// Só tem efeito com o CSV já aberto (após a primeira linha de valores).
    /// </summary>
    public void WriteMarker(string label)
    {
        lock (_lock)
        {
            if (_disposed || _writer == null) return;

            try
            {
                _writer.WriteLine(string.Join(",", "#marker",
                    DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                    (label ?? "").Replace(',', ' ')));
                _writer.Flush();
            }
            catch (Exception ex)
            {
                ErrorLog.Write("CsvLogger.WriteMarker", ex);
            }
        }
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
