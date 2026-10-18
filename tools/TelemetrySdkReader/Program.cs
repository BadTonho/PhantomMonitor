// Leitor de exemplo da SDK de telemetria do Phantom Monitor (docs/telemetry-sdk.md).
// Abre a memória compartilhada `phantom_monitor_telemetry_v1` publicada pelo monitor
// e imprime as séries em tempo real usando o protocolo de consistência (seqlock).
//
// Uso: dotnet run --project tools/TelemetrySdkReader [-- intervaloMs]
using System.IO.MemoryMappedFiles;
using System.Text;

const string MapName = "phantom_monitor_telemetry_v1";
const int Magic = 0x53544D50;           // "PMTS" little-endian
const int ProtocolVersion = 1;
const int HeaderSize = 64;
const int Capacity = 256;
const int KeyLength = 40;
const int LabelLength = 32;
const int EntrySize = KeyLength + 8 + LabelLength; // 80 bytes por série

var intervalMs = args.Length > 0 && int.TryParse(args[0], out var i) && i > 50 ? i : 500;

MemoryMappedFile? mmf = null;
MemoryMappedViewAccessor? accessor = null;

Console.WriteLine($"Phantom Monitor — leitor de telemetria ({MapName}, {intervalMs} ms). Ctrl+C para sair.");

while (true)
{
    // O monitor cria o mapa quando começa a publicar; aguarde se não existir ainda.
    try
    {
        if (accessor == null)
        {
            mmf = MemoryMappedFile.OpenExisting(MapName, MemoryMappedFileRights.Read);
            accessor = mmf.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);
        }
    }
    catch (FileNotFoundException)
    {
        Console.WriteLine("...aguardando o Phantom Monitor (mapa ainda não criado).");
        Thread.Sleep(1000);
        continue;
    }
    catch (UnauthorizedAccessException)
    {
        Console.WriteLine("...o monitor está rodando em outra sessão/nível de permissão; rode o leitor no mesmo usuário.");
        Thread.Sleep(1000);
        continue;
    }

    PrintOnce(accessor!);
    Thread.Sleep(intervalMs);
}

static void PrintOnce(System.IO.MemoryMappedFiles.MemoryMappedViewAccessor accessor)
{
    var (valid, count, timestampMs) = ReadHeader(accessor);
    if (!valid)
    {
        Console.WriteLine("...escrita em andamento — tentando de novo...");
        return;
    }

    Console.WriteLine();
    Console.WriteLine($"Snapshot: {DateTimeOffset.FromUnixTimeMilliseconds(timestampMs).LocalDateTime:HH:mm:ss} | séries: {count}");
    foreach (var entry in ReadSeries(accessor, count))
    {
        Console.WriteLine($"  {entry.Key,-24} {entry.Value,10:F2}  {entry.Label}");
    }
}

// Seqlock: seq lido antes e depois precisa ser idêntico e PAR; se mudou, a leitura
// foi interrompida pela próxima publicação e é descartada (o loop externo repete).
static (bool valid, int count, long timestamp) ReadHeader(System.IO.MemoryMappedFiles.MemoryMappedViewAccessor a)
{
    int seq1 = a.ReadInt32(8);
    if (seq1 % 2 != 0) return (false, 0, 0);

    int magic = a.ReadInt32(0);
    if (magic != Magic) return (false, 0, 0);

    int version = a.ReadInt32(4);
    if (version != ProtocolVersion) return (false, 0, 0);

    int count = a.ReadInt32(12);
    long ts = a.ReadInt64(16);

    int seq2 = a.ReadInt32(8);
    return seq2 == seq1 ? (true, Math.Min(count, Capacity), ts) : (false, 0, 0);
}

static List<(string Key, double Value, string Label)> ReadSeries(System.IO.MemoryMappedFiles.MemoryMappedViewAccessor a, int count)
{
    var result = new List<(string, double, string)>(count);
    for (int i = 0; i < count; i++)
    {
        var offset = HeaderSize + i * EntrySize;
        var key = ReadFixed(a, offset, KeyLength);
        var value = a.ReadDouble(offset + KeyLength);
        var label = ReadFixed(a, offset + KeyLength + 8, LabelLength);
        if (!double.IsNaN(value))
            result.Add((key, value, label));
    }
    return result;
}

static string ReadFixed(System.IO.MemoryMappedFiles.MemoryMappedViewAccessor a, int offset, int length)
{
    var bytes = new byte[length];
    a.ReadArray(offset, bytes, 0, length);
    var nul = Array.IndexOf(bytes, (byte)0);
    return Encoding.ASCII.GetString(nul < 0 ? bytes : bytes[..nul]);
}
