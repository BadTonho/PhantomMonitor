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
        ApplyConfiguredTheme();
    }

    /// <summary>
    /// Fase 3 — troca de tema builtin em runtime. As views reagem na hora pelos
    /// <c>DynamicResource</c> da Fase 0 (inclusive o HistoryChart) e os pincéis
    /// resolvidos pela VM (ResolveBrush, cache por execução) se refrescam por
    /// <see cref="ThemeChanged"/>; demais janelas usam o mesmo Resources do app.
    /// Chamada pela página Configurações ao salvar, já na UI thread.
    /// </summary>
    public static event Action? ThemeChanged;

    public static string CurrentTheme { get; private set; } = "neptune";

    /// <summary>
    /// Troca o dicionário de tokens ativo. No-op se o tema pedido já está ativo.
    /// A troca dispara <see cref="ThemeChanged"/> ao final, sempre no chamador
    /// (UI thread).
    /// </summary>
    public static void ApplyTheme(string theme)
    {
        var target = theme == "slate" ? "slate" : "neptune";
        if (target == CurrentTheme) return;

        var source = target == "slate"
            ? "avares://PhantomMonitor/Themes/Tokens.Slate.axaml"
            : "avares://PhantomMonitor/Themes/Tokens.Neptune.axaml";
        var app = Application.Current;
        if (app is null) return;

        app.Resources.MergedDictionaries.Clear();
        app.Resources.MergedDictionaries.Add(
            (Avalonia.Controls.ResourceDictionary)AvaloniaXamlLoader.Load(new Uri(source)));
        CurrentTheme = target;
        ThemeChanged?.Invoke();
    }

    /// <summary>
    /// F1.6/Fase 3 — escolhe o tema builtin no boot: CLI <c>--theme=&lt;neptune|slate&gt;</c>
    /// (com <c>--ui-theme=</c> aceito como legado) sobrepõe config.json <c>"theme"</c>.
    /// O dicionário é trocado ANTES da criação das views, então todos os
    /// DynamicResource e as pílulas/VM (que resolvem tokens por nome na
    /// construção — "ResolveBrush") já leem o dicionário certo. A página
    /// Configurações usa <see cref="ApplyTheme"/> para a troca em runtime.
    /// </summary>
    private void ApplyConfiguredTheme() => ApplyTheme(ResolveTheme());

    private static string ResolveTheme()
    {
        foreach (var arg in Environment.GetCommandLineArgs())
        {
            var value = ExtractThemeArg(arg, "--theme=") ?? ExtractThemeArg(arg, "--ui-theme=");
            if (value is not null) return value;
        }

        // Config.Load normaliza valores desconhecidos para "neptune" e, em falha de
        // leitura, devolve o AppConfig padrão — boot sempre com tema válido.
        return AppConfig.Load(ConfigPaths.ConfigFile).Theme;
    }

    private static string? ExtractThemeArg(string arg, string prefix)
    {
        if (!arg.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return null;
        var value = arg[prefix.Length..].Trim().ToLowerInvariant();
        return value is "neptune" or "slate" ? value : null;
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
