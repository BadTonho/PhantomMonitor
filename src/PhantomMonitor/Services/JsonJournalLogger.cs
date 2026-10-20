using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using PhantomMonitor.Models;

namespace PhantomMonitor.Services;

/// <summary>
/// Journal JSON contínuo (JSONL, paridade com o "sensor logging em JSON" de suítes
/// de referência): **uma linha por evento**, anexada ao arquivo — gravação de custo
/// constante (nada é reescrito a cada ciclo, como no snapshot sensors_latest.json)
/// e replay de benchmark viável sem parse de arquivo inteiro.
///
/// Formato (1 objeto JSON por linha, UTF-8, sem indentação):
/// <code>
/// {"kind":"meta","ts":"...","labels":{"GpuCore":"GPU Core (Usage)"}}
/// {"kind":"sample","ts":"...","series":{"GpuCore":12.3,"GpuCore#1":8.1}}
/// {"kind":"note","ts":"...","text":"reset (bandeja)"}
/// </code>
/// A linha "meta" sai na primeira amostra com os rótulos do ciclo (série → sensor
/// de origem); amostras seguintes carregam só chave→valor. Notas ({"kind":"note"})
/// são gravadas nos mesmos gatilhos do "#marker" do CSV — início da sessão e resets
/// de estatísticas — delimitando segmentos de benchmark no próprio journal.
/// Escrita append-only com flush imediato; falhas de I/O caem no errors.log e
/// nunca derrubam o monitoramento (mesma política do CSV).
/// </summary>
public sealed class JsonJournalLogger : IDisposable
{
    private readonly string _filePath;
    private readonly object _lock = new();
    private StreamWriter? _writer;
    private bool _headerWritten;
    private bool _disposed;

    public JsonJournalLogger(string filePath)
    {
        _filePath = filePath;
    }

    /// <summary>Amostra atual (chamado a cada snapshot publicado).</summary>
    public void Write(SensorData data)
    {
        lock (_lock)
        {
            if (_disposed) return;

            try
            {
                var writer = OpenWriter();
                if (!_headerWritten)
                {
                    WriteHeader(writer, data);
                    _headerWritten = true;
                }

                var line = BuildLine(jw =>
                {
                    jw.WriteStartObject();
                    jw.WriteString("kind", "sample");
                    jw.WriteString("ts", data.Timestamp.ToString("O", CultureInfo.InvariantCulture));
                    jw.WriteStartObject("series");
                    foreach (var key in data.GetSeriesKeys())
                    {
                        jw.WriteNumber(key.ToString(), data.GetValue(key));
                    }
                    jw.WriteEndObject();
                    jw.WriteEndObject();
                });

                writer.Write(line);
                writer.WriteLine();
                writer.Flush();
            }
            catch (Exception ex)
            {
                ErrorLog.Write("JsonJournal.Write", ex);
            }
        }
    }

    /// <summary>
    /// Linha de nota: {"kind":"note","ts":...,"text":...}. Chamada do app nos
    /// marcadores de sessão (primeira escrita do logger) e a cada reset de
    /// estatísticas — rótulo truncado defensivamente.
    /// </summary>
    public void WriteNote(string? text)
    {
        // Uma linha por nota; \r/\n viram espaço (ajuda consumidores de replay).
        var safe = (text ?? "").Replace("\r", " ").Replace("\n", " ").Trim();
        if (safe.Length == 0) return;
        if (safe.Length > 256) safe = safe[..256];

        lock (_lock)
        {
            if (_disposed) return;

            try
            {
                var writer = OpenWriter();
                var line = BuildLine(jw =>
                {
                    jw.WriteStartObject();
                    jw.WriteString("kind", "note");
                    jw.WriteString("ts", DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));
                    jw.WriteString("text", safe);
                    jw.WriteEndObject();
                });

                writer.Write(line);
                writer.WriteLine();
                writer.Flush();
            }
            catch (Exception ex)
            {
                ErrorLog.Write("JsonJournal.WriteNote", ex);
            }
        }
    }

    /// <summary>Linha "meta" com rótulos do primeiro ciclo (série → sensor de origem).</summary>
    private void WriteHeader(StreamWriter writer, SensorData data)
    {
        var line = BuildLine(jw =>
        {
            jw.WriteStartObject();
            jw.WriteString("kind", "meta");
            jw.WriteString("ts", data.Timestamp.ToString("O", CultureInfo.InvariantCulture));
            jw.WriteStartObject("labels");
            foreach (var key in data.GetSeriesKeys())
            {
                var label = data.GetLabel(key);
                if (label.Length > 0) jw.WriteString(key.ToString(), label);
            }
            jw.WriteEndObject();
            jw.WriteEndObject();
        });

        writer.Write(line);
        writer.WriteLine();
        writer.Flush();
    }

    /// <summary>Constrói a linha via Utf8JsonWriter (escapes corretos; 1 alocação por linha).</summary>
    private static string BuildLine(Action<Utf8JsonWriter> fill)
    {
        var buffer = new MemoryStream();
        using (var jw = new Utf8JsonWriter(buffer))
        {
            fill(jw);
            jw.Flush();
        }
        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    private StreamWriter OpenWriter()
    {
        if (_writer != null) return _writer;

        var dir = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        return _writer = new StreamWriter(_filePath, append: true) { AutoFlush = false };
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;
            try { _writer?.Flush(); } catch { /* arquivo pode ter sido removido */ }
            _writer?.Dispose();
            _writer = null;
        }
    }
}
