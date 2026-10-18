using System.Windows;

namespace RivaMonitor;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += (s, args) =>
        {
            MessageBox.Show($"Erro inesperado:\n\n{args.Exception.Message}\n\n{args.Exception.StackTrace}",
                "Riva Monitor - Erro", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };
    }
}
