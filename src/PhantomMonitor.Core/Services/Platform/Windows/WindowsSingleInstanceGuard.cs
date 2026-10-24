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
    private static readonly TimeSpan RestartWaitTimeout = TimeSpan.FromSeconds(30);

    public static Mutex? TryAcquire(bool waitForExisting = false)
    {
        var mutex = new Mutex(true, MutexName, out var createdNew);
        if (createdNew) return mutex;

        if (waitForExisting)
        {
            try
            {
                if (mutex.WaitOne(RestartWaitTimeout)) return mutex;
            }
            catch (AbandonedMutexException)
            {
                // O processo antigo encerrou enquanto ainda era dono do mutex.
                // WaitOne concede a posse ao processo atual mesmo neste caso.
                return mutex;
            }

            mutex.Dispose();
            MessageBoxW(IntPtr.Zero, "A instância atual não encerrou a tempo para reiniciar como administrador.",
                "Phantom Monitor", MbIconInformation);
            return null;
        }

        mutex.Dispose();
        MessageBoxW(IntPtr.Zero, "Phantom Monitor já está em execução.", "Phantom Monitor", MbIconInformation);
        return null;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint options);
}
