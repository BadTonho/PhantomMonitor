using System.IO;
using System.Text;

namespace PhantomMonitor.Services.Linux;

/// <summary>
/// Lock exclusivo do arquivo de sessão no diretório de logs: a segunda instância
/// falha em <c>FileShare.None</c> e encerra silenciosamente (log no ErrorLog; sem
/// UI em ambiente desktop mínimo). O lock é liberado pelo SO quando o processo sai.
/// </summary>
public static class LinuxSingleInstanceGuard
{
    private const string LockFileName = "session.lock";

    public static FileStream? TryAcquire()
    {
        try
        {
            Directory.CreateDirectory(UserDataPaths.LogDirectory);
            var handle = new FileStream(Path.Combine(UserDataPaths.LogDirectory, LockFileName),
                FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            var content = Encoding.UTF8.GetBytes($"{Environment.ProcessId}\n");
            handle.SetLength(0);
            handle.Write(content, 0, content.Length);
            handle.Flush();
            return handle;
        }
        catch (Exception ex)
        {
            ErrorLog.Write("SingleInstance", $"Outra instância já detém '{LockFileName}' (ou o lock falhou): {ex.Message}");
            return null;
        }
    }
}
