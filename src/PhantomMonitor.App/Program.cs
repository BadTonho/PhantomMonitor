using Avalonia;
using PhantomMonitor.Services;

namespace PhantomMonitor.App;

internal static class Program
{
    private static IDisposable? _singleInstanceLock;

    [STAThread]
    public static int Main(string[] args)
    {
        // Instância única (F1.5, paridade do WPF): mutex nomeado no Windows (com
        // MessageBox informativo) e lock de arquivo no Linux (silencioso). Segunda
        // cópia encerra antes de qualquer arranque — protege a sessão ETW do
        // PresentMon e o sensors_latest.json compartilhados.
        _singleInstanceLock = SingleInstanceGuard.TryAcquire();
        if (_singleInstanceLock is null)
            return 0;

        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        finally
        {
            _singleInstanceLock.Dispose();
            _singleInstanceLock = null;
        }
        return 0;
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}
