using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace PhantomMonitor.Services;

/// <summary>
/// Mutex nomeado por sessão (WPF: <c>Local\PhantomMonitor.SingleInstance</c>).
/// Segunda instância recebe um MessageBox informativo e encerra — mesma mensagem
/// do WPF. O retorno é a própria mutex: segura-a viva pelo processo.
/// </summary>
[SupportedOSPlatform("windows")]
public static class WindowsSingleInstanceGuard
{
    private const string MutexName = @"Local\PhantomMonitor.SingleInstance";
    private const uint MbIconInformation = 0x40;

    public static Mutex? TryAcquire()
    {
        var mutex = new Mutex(true, MutexName, out var createdNew);
        if (createdNew) return mutex;
        mutex.Dispose();
        MessageBoxW(IntPtr.Zero, "Phantom Monitor já está em execução.", "Phantom Monitor", MbIconInformation);
        return null;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint options);
}
