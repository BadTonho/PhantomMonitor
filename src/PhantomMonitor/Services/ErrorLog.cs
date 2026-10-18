using System.Diagnostics;
using System.IO;

namespace PhantomMonitor.Services;

/// <summary>
/// Registro de erros/diagnósticos internos (substitui os antigos catch vazios).
/// Escreve em logs/errors.log ao lado do executável, com limite de tamanho
/// e supressão de mensagens repetidas consecutivas.
/// </summary>
public static class ErrorLog
{
    private const long MaxFileBytes = 1_000_000;
    private static readonly object Lock = new();
    private static string? _lastMessage;
    private static string? _path;

    public static void Write(string context, Exception ex) =>
        Write(context, $"{ex.GetType().Name}: {ex.Message}");

    public static void Write(string context, string message)
    {
        var line = $"[{context}] {message}";
        Debug.WriteLine(line);

        try
        {
            lock (Lock)
            {
                if (line == _lastMessage) return;
                _lastMessage = line;

                _path ??= Path.Combine(AppContext.BaseDirectory, "logs", "errors.log");
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);

                var info = new FileInfo(_path);
                if (info.Exists && info.Length > MaxFileBytes)
                    File.WriteAllText(_path, "");

                File.AppendAllText(_path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {line}{Environment.NewLine}");
            }
        }
        catch
        {
            // Logging nunca deve derrubar o app.
        }
    }
}
