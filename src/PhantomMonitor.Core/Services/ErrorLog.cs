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
            catch
            {
                // The in-memory tail still exposes entries if the file was removed or became unreadable.
            }

            return fileEntries.Concat(RecentEntries)
                .GroupBy(entry => (entry.Timestamp.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture), entry.Context, entry.Message))
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
        catch
        {
            // Logging nunca deve derrubar o app.
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
