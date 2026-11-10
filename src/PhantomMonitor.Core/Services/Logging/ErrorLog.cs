using System.Diagnostics;
using System.IO;
using System.Globalization;
using PhantomMonitor.Models;

namespace PhantomMonitor.Services;

/// <summary>
/// Registro de erros/diagnósticos internos (substitui os antigos catch vazios).
/// Escreve em logs/errors.log ao lado do executável por padrão; cada frontend
/// pode configurar outro caminho. Mantém limite de tamanho e supressão de
/// mensagens repetidas consecutivas.
/// </summary>
public static class ErrorLog
{
    private const long MaxFileBytes = 1_000_000;
    private const int MaxRecentEntries = 500;
    private static readonly object Lock = new();
    private static readonly Queue<ErrorLogEntry> RecentEntries = new();
    private static string? _lastMessage;
    private static string? _path;

    public static event Action<ErrorLogEntry>? EntryWritten;

    public static string LogFilePath
    {
        get
        {
            lock (Lock)
                return _path ?? DefaultPath;
        }
    }

    private static string DefaultPath => Path.Combine(AppContext.BaseDirectory, "logs", "errors.log");

    /// <summary>Configures a per-frontend path before the first log entry is written.</summary>
    public static void ConfigurePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("O caminho do log é obrigatório.", nameof(path));
        lock (Lock)
        {
            if (_lastMessage is not null || _path is not null)
                throw new InvalidOperationException("O caminho do log já foi utilizado.");
            _path = Path.GetFullPath(path);
        }
    }

    public static IReadOnlyList<ErrorLogEntry> ReadRecentEntries(int maxEntries = MaxRecentEntries)
    {
        maxEntries = Math.Clamp(maxEntries, 1, MaxRecentEntries);
        lock (Lock)
        {
            var path = _path ?? DefaultPath;
            var fileEntries = Array.Empty<ErrorLogEntry>();
            try
            {
                if (File.Exists(path))
                {
                    fileEntries = File.ReadLines(path)
                        .Select(ParseLine)
                        .Where(entry => entry is not null)
                        .Select(entry => entry!)
                        .ToArray();
                }
            }
            catch (Exception ex)
            {
                // The in-memory tail still exposes entries if the file was removed or became unreadable.
                System.Diagnostics.Trace.WriteLine("ErrorLog: falha ao ler o log: " + ex.Message);
            }

            return fileEntries.Concat(RecentEntries)
                // B6 (auditoria 2026-11-03): dedup por Timestamp integral — agrupar
                // por segundo colapsava ocorrências distintas no mesmo segundo.
                .GroupBy(entry => (entry.Timestamp, entry.Context, entry.Message))
                .Select(group => group.Last())
                .OrderBy(entry => entry.Timestamp)
                .TakeLast(maxEntries)
                .ToArray();
        }
    }

    public static void EnsureLogFileExists()
    {
        lock (Lock)
        {
            var path = _path ?? DefaultPath;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using var _ = new FileStream(path, FileMode.OpenOrCreate, FileAccess.Write, FileShare.ReadWrite);
        }
    }

    private sealed class ThrottleState
    {
        public DateTime LastWrittenUtc;
        public int Suppressed;
    }

    private static readonly Dictionary<string, ThrottleState> Throttles = new(StringComparer.Ordinal);
    private static readonly TimeSpan ThrottleWindow = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Para falhas que se repetem em laços de leitura (sensores, WMI, PresentMon): grava a primeira
    /// ocorrência de cada contexto e, dentro de 60 s, apenas conta as demais. A gravação seguinte
    /// informa quantas foram suprimidas.
    /// </summary>
    public static void WriteThrottled(string context, Exception ex)
    {
        var (shouldWrite, suppressed) = Admit(context);
        if (!shouldWrite) return;
        Write(context, suppressed == 0
            ? ex.ToString()
            : $"{ex.GetType().Name}: {ex.Message} (+{suppressed} ocorrências suprimidas)");
    }

    public static void WriteThrottled(string context, string message)
    {
        var (shouldWrite, suppressed) = Admit(context);
        if (!shouldWrite) return;
        Write(context, suppressed == 0
            ? message
            : $"{message} (+{suppressed} ocorrências suprimidas)");
    }

    private static (bool ShouldWrite, int Suppressed) Admit(string context)
    {
        var now = DateTime.UtcNow;
        lock (Lock)
        {
            if (!Throttles.TryGetValue(context, out var state))
            {
                Throttles[context] = new ThrottleState { LastWrittenUtc = now };
                return (true, 0);
            }
            if (now - state.LastWrittenUtc < ThrottleWindow)
            {
                state.Suppressed++;
                return (false, 0);
            }
            var suppressed = state.Suppressed;
            state.Suppressed = 0;
            state.LastWrittenUtc = now;
            return (true, suppressed);
        }
    }

    public static void Write(string context, Exception ex) =>
        Write(context, ex.ToString().ReplaceLineEndings(" | "));

    public static void Write(string context, string message)
    {
        var timestamp = DateTime.Now;
        var entry = new ErrorLogEntry(timestamp, context, message.ReplaceLineEndings(" | "));
        var line = $"[{entry.Context}] {entry.Message}";
        Debug.WriteLine(line);

        try
        {
            lock (Lock)
            {
                if (line == _lastMessage) return;
                _lastMessage = line;

                RecentEntries.Enqueue(entry);
                while (RecentEntries.Count > MaxRecentEntries)
                    RecentEntries.Dequeue();

                _path ??= DefaultPath;
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);

                var info = new FileInfo(_path);
                if (info.Exists && info.Length > MaxFileBytes)
                    File.WriteAllText(_path, "");

                File.AppendAllText(_path, $"{timestamp:yyyy-MM-dd HH:mm:ss} {line}{Environment.NewLine}");
            }
        }
        catch (Exception ex)
        {
            // Logging nunca deve derrubar o app, mas a falha não pode sumir sem rastro.
            System.Diagnostics.Trace.WriteLine("ErrorLog: falha ao gravar o log: " + ex.Message);
        }

        try { EntryWritten?.Invoke(entry); }
        catch { /* Um consumidor do viewer não pode interromper o app. */ }
    }

    private static ErrorLogEntry? ParseLine(string line)
    {
        if (line.Length < 22 || !DateTime.TryParseExact(
                line.AsSpan(0, 19), "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeLocal, out var timestamp))
            return null;

        var payload = line[20..];
        if (!payload.StartsWith('[')) return new ErrorLogEntry(timestamp, "App", payload);
        var separator = payload.IndexOf("]: ", StringComparison.Ordinal);
        return separator < 2
            ? new ErrorLogEntry(timestamp, "App", payload)
            : new ErrorLogEntry(timestamp, payload[1..separator], payload[(separator + 3)..]);
    }
}
