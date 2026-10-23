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
        ApplyConfiguredUiTheme();
    }

    /// <summary>
    /// F1.6 — escolhe o tema builtin no boot: CLI <c>--ui-theme=&lt;neptune|slate&gt;</c>
    /// sobrepõe config.json <c>"uiTheme"</c>. O dicionário é trocado ANTES da criação
    /// das views, então todos os DynamicResource e as pílulas/VM (que resolvem tokens
    /// por nome na construção — "ResolveBrush") já leem o dicionário certo. A troca
    /// em runtime pela UI chega na Fase 3.
    /// </summary>
    private void ApplyConfiguredUiTheme()
    {
        var source = ResolveUiTheme() == "slate"
            ? "avares://PhantomMonitor.App/Themes/Tokens.Slate.axaml"
            : "avares://PhantomMonitor.App/Themes/Tokens.Neptune.axaml";

        Resources.MergedDictionaries.Clear();
        Resources.MergedDictionaries.Add(
            (Avalonia.Controls.ResourceDictionary)AvaloniaXamlLoader.Load(new Uri(source)));
    }

    private static string ResolveUiTheme()
    {
        foreach (var arg in Environment.GetCommandLineArgs())
        {
            if (!arg.StartsWith("--ui-theme=", StringComparison.OrdinalIgnoreCase)) continue;
            var value = arg["--ui-theme=".Length..].Trim().ToLowerInvariant();
            if (value is "neptune" or "slate") return value;
        }

        // Config.Load normaliza valores desconhecidos para "neptune" e, em falha de
        // leitura, devolve o AppConfig padrão — boot sempre com tema válido.
        return AppConfig.Load(ConfigPaths.ConfigFile).UiTheme;
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
