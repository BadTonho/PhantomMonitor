using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using PhantomMonitor.Services;
using PhantomMonitor.App.ViewModels;
using PhantomMonitor.App.Views;

namespace PhantomMonitor.App;

public sealed partial class App : Application
{
    public override void Initialize()
    {
        if (OperatingSystem.IsLinux())
            ErrorLog.ConfigurePath(Path.Combine(UserDataPaths.LogDirectory, "errors.log"));

        RegisterExceptionHandlers();
        AvaloniaXamlLoader.Load(this);
    }

    private static void RegisterExceptionHandlers()
    {
        Dispatcher.UIThread.UnhandledException += (_, args) =>
            ErrorLog.Write("Avalonia.Dispatcher.UnhandledException", args.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex)
                ErrorLog.Write("AppDomain.UnhandledException", ex);
        };
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            ErrorLog.Write("TaskScheduler.UnobservedTaskException", args.Exception);
            args.SetObserved();
        };
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow
            {
                DataContext = new MainWindowViewModel()
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
