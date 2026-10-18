using System.IO;
using System.IO.MemoryMappedFiles;
using System.Text;
using PhantomMonitor.Models;

namespace PhantomMonitor.Services.Telemetry;

/// <summary>
/// SDK de telemetria estilo RTSS/HWiNFO: publica o snapshot atual numa região de
/// memória compartilhada (<c>phantom_monitor_telemetry_v1</c>) em cada ciclo do
/// monitoramento, sem parse de JSON/CSV por parte do consumidor.
///
/// Protocolo (seqlock sem spin do escritor):
/// - O escritor incrementa `seq` para um valor ÍMPAR, reescreve o cabeçalho + tabela
///   e então incrementa para o próximo PAR. Um leitor válido lê `seq` antes e depois
///   da tabela: se ambos forem o MESMO valor par, a leitura é consistente; caso
///   contrário, repete (o leitor nunca vê escrita pela metade com a checagem dupla).
/// - Entradas fora de `seriesCount` são marcadas com NaN quando o total encolhe,
///   para leitores legados que carreguem a tabela inteira.
/// Layout completo e exemplos de leitura: docs/telemetry-sdk.md
/// </summary>
public sealed class TelemetrySdkWriter : IDisposable
{
    /// <summary>Versão 1 do layout (quebre só com nome de mapa novo: v2...).</summary>
    public const int ProtocolVersion = 1;
    public const string MapName = "phantom_monitor_telemetry_v1";
    public const int Magic = 0x53544D50; // "PMTS" little-endian

    public const int HeaderSize = 64;
    public const int Capacity = 256;
    public const int KeyLength = 40;
    public const int LabelLength = 32;
    public const int EntrySize = KeyLength + sizeof(double) + LabelLength; // 80

    private const int SeqOffset = 8;
    private const int CountOffset = 12;
    private const int TimestampOffset = 16;
    private const int EntriesOffset = HeaderSize;

    private MemoryMappedFile? _mmf;
    private MemoryMappedViewAccessor? _accessor;
    private int _seq;
    private int _lastCount;
    private bool _disposed;

    public void Publish(SensorData data)
    {
        if (_disposed) return;

        try
        {
            _accessor ??= OpenMapping();
            var accessor = _accessor;

            var keys = data.GetSeriesKeys();
            var count = Math.Min(keys.Count, Capacity);

            // 1) Seq ímpar: "atingindo o meio" da escrita para leitores.
            _seq = unchecked(_seq + 1);
            accessor.Write(SeqOffset, _seq);

            accessor.Write(CountOffset, count);
            accessor.Write(TimestampOffset, new DateTimeOffset(data.Timestamp).ToUnixTimeMilliseconds());

            for (int i = 0; i < count; i++)
            {
                WriteEntry(accessor, i, keys[i], data.GetValue(keys[i]), data.GetLabel(keys[i]));
            }

            // 2) Entradas que sobraram do ciclo anterior ficam com NaN (série sumiu).
            for (int i = count; i < _lastCount; i++)
            {
                accessor.Write(EntriesOffset + i * EntrySize + KeyLength, double.NaN);
            }
            _lastCount = count;

            // 3) Seq par: escrita completa.
            _seq = unchecked(_seq + 1);
            accessor.Write(SeqOffset, _seq);
        }
        catch (Exception ex)
        {
            ErrorLog.Write("TelemetrySdk.Publish", ex);
        }
    }

    private MemoryMappedViewAccessor OpenMapping()
    {
        var size = EntriesOffset + Capacity * EntrySize; // 64 + 256*80 = 20544 bytes
        _mmf = MemoryMappedFile.CreateOrOpen(MapName, size, MemoryMappedFileAccess.ReadWrite);
        var accessor = _mmf.CreateViewAccessor(0, size, MemoryMappedFileAccess.ReadWrite);

        // Cabeçalho inicial (magic/version/seq=0) lido uma vez; leitores podem chegaram antes.
        accessor.Write(0, Magic);
        accessor.Write(4, ProtocolVersion);
        accessor.Write(SeqOffset, 0);
        accessor.Write(CountOffset, 0);
        return accessor;
    }

    private static void WriteEntry(MemoryMappedViewAccessor accessor, int index, SensorKey key, double value, string label)
    {
        var offset = EntriesOffset + index * EntrySize;

        WriteFixedString(accessor, offset, key.ToString(), KeyLength);
        accessor.Write(offset + KeyLength, value);
        WriteFixedString(accessor, offset + KeyLength + sizeof(double), Truncate(label, LabelLength), LabelLength);
    }

    /// <summary>Grava o texto ASCII fixo, preenchido com NUL até o comprimento (trunca o excesso).</summary>
    private static void WriteFixedString(MemoryMappedViewAccessor accessor, int offset, string text, int length)
    {
        var bytes = new byte[length];
        var chars = Encoding.ASCII.GetBytes(text.Length > length ? text[..length] : text);
        Array.Copy(chars, bytes, Math.Min(chars.Length, length));
        accessor.WriteArray(offset, bytes, 0, length);
    }

    private static string Truncate(string text, int length)
    {
        if (string.IsNullOrEmpty(text)) return "";
        var sb = new StringBuilder();
        foreach (var c in text)
        {
            if (sb.Length + 1 > length) break;
            sb.Append(c <= 127 ? c : '*'); // ASCII-only no mapa; não-ASCII vira curinga
        }
        return sb.ToString();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try
        {
            _accessor?.Dispose();
            _mmf?.Dispose();
        }
        catch
        {
            // Destruição limpa: falha de unmap não é crítica (o SO recolhe o mapa).
        }
        _accessor = null;
        _mmf = null;
    }
}
