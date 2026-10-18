using System;
using System.Threading.Tasks;
using System.Windows;
using PhantomMonitor.Services;

namespace PhantomMonitor;

public partial class App : Application
{
    private static string? _lastShownError;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Exceções na UI thread: logadas com supressão de repetição antes do modal.
        // Como os snapshots rodam a cada ~1s na própria UI thread, um erro recorrente
        // (ex.: disco cheio travando o CSV) geraria uma enxurrada de modais encadeados —
        // erros idênticos ao anterior ficam apenas no errors.log.
        DispatcherUnhandledException += (s, args) =>
        {
            args.Handled = true;
            ErrorLog.Write("Dispatcher.UnhandledException", args.Exception);

            string details = args.Exception.ToString();
            if (details == _lastShownError) return;
            _lastShownError = details;

            MessageBox.Show($"Erro inesperado:\n\n{args.Exception.Message}\n\n{args.Exception.StackTrace}",
                "Phantom Monitor - Erro", MessageBoxButton.OK, MessageBoxImage.Error);
        };

        // Exceções fora da UI thread (timers de fundo, tasks do WMI/LibreHardware/PresentMon)
        // ficavam invisíveis sem isso — só sobreviviam em catches locais.
        AppDomain.CurrentDomain.UnhandledException += (s, args) =>
        {
            if (args.ExceptionObject is Exception ex)
                ErrorLog.Write("AppDomain.UnhandledException", ex);
        };

        TaskScheduler.UnobservedTaskException += (s, args) =>
        {
            ErrorLog.Write("TaskScheduler.UnobservedTaskException", args.Exception);
            args.SetObserved();
        };
    }
}
