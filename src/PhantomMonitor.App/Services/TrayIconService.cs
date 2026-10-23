using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Media.Immutable;
using PhantomMonitor.App.ViewModels;
using PhantomMonitor.Services;

namespace PhantomMonitor.App.Services;

/// <summary>
/// Ícone na bandeja via <c>TrayIcon</c> do Avalonia (Shell_NotifyIcon no Windows;
/// StatusNotifier/DBus no Linux — que exige um ambiente com suporte; sem ele o app
/// continua funcional sem bandeja). Menu nativo com os itens do WPF (Mostrar,
/// Overlay no Jogo, Iniciar com o sistema, Resetar estatísticas, Pausar
/// monitoramento, seção de perfis do OSD e Sair). O ícone é desenhado em runtime
/// com o mesmo visual do WPF — quadro escuro com borda azul e gráfico ciano — e os
/// checks/perfis são reconstruídos via <see cref="InvalidateMenu"/> quando o estado
/// muda (checks avaliados pelos getters registrados, como no WPF).
/// </summary>
public sealed class TrayIconService : IDisposable
{
    private const int IconSize = 32;

    /// <summary>Clique esquerdo/duplo no ícone: trazer a janela principal para frente.</summary>
    public event Action? ActivateRequested;

    /// <summary>Menu: alternar o overlay OSD.</summary>
    public event Action? OverlayToggleRequested;

    /// <summary>Menu: alternar a inicialização com o sistema.</summary>
    public event Action? StartupToggleRequested;

    /// <summary>Menu: zero as estatísticas por série da sessão (min/max/avg).</summary>
    public event Action? ResetStatsRequested;

    /// <summary>Menu: pausar/retomar a coleta de sensores (P6.7, sem persistir).</summary>
    public event Action? PauseToggleRequested;

    /// <summary>Menu: encerrar o aplicativo (fecha a janela principal).</summary>
    public event Action? ExitRequested;

    /// <summary>Estado atual do overlay (para o check do menu).</summary>
    public Func<bool>? IsOverlayVisible { get; set; }

    /// <summary>Estado atual da inicialização com o sistema (para o check do menu).</summary>
    public Func<bool>? IsStartupEnabled { get; set; }

    /// <summary>Estado atual da pausa do monitoramento (para o check do menu, P6.7).</summary>
    public Func<bool>? IsPaused { get; set; }

    private readonly NativeMenu _menu = new();
    private readonly TrayIcon _trayIcon;
    private Func<IReadOnlyList<MainWindowViewModel.TrayProfileMenuEntry>>? _dynamicItemsProvider;
    private Action<int>? _dynamicItemClicked;

    public TrayIconService()
    {
        _trayIcon = new TrayIcon
        {
            Icon = CreateAppIcon(IconSize),
            ToolTipText = "Phantom Monitor",
            IsVisible = true,
            Menu = _menu
        };
        // Clique esquerdo no ícone: ativa janela (paridade do NIN_SELECT do WPF).
        _trayIcon.Clicked += (_, _) => ActivateRequested?.Invoke();
        TrayIcon.SetIcons(Application.Current!, [_trayIcon]);
        InvalidateMenu();
    }

    /// <summary>Registra a seção dinâmica do menu (perfis do OSD, avaliada a cada rebuild) e o retorno do clique.</summary>
    public void SetDynamicItems(Func<IReadOnlyList<MainWindowViewModel.TrayProfileMenuEntry>>? provider, Action<int>? onClicked)
    {
        _dynamicItemsProvider = provider;
        _dynamicItemClicked = onClicked;
    }

    /// <summary>Reconstrói o menu: os checks refletem os getters no momento da chamada.</summary>
    public void InvalidateMenu()
    {
        _menu.Items.Clear();

        AddItem("Mostrar Phantom Monitor", check: null, () => ActivateRequested?.Invoke());
        AddItem("Overlay no Jogo", IsOverlayVisible?.Invoke() == true, () => OverlayToggleRequested?.Invoke());
        AddItem("Iniciar com o sistema", IsStartupEnabled?.Invoke() == true, () => StartupToggleRequested?.Invoke());
        AddItem("Resetar estatísticas", check: null, () => ResetStatsRequested?.Invoke());
        AddItem("Pausar monitoramento", IsPaused?.Invoke() == true, () => PauseToggleRequested?.Invoke());

        // Seção dinâmica (perfis por aplicativo): pin / voltar ao automático.
        var dynamic = _dynamicItemsProvider?.Invoke();
        if (dynamic is { Count: > 0 })
        {
            _menu.Items.Add(new NativeMenuItemSeparator());
            foreach (var entry in dynamic)
                AddItem(entry.Title, entry.Checked, () => _dynamicItemClicked?.Invoke(entry.Id));
        }

        _menu.Items.Add(new NativeMenuItemSeparator());
        AddItem("Sair", check: null, () => ExitRequested?.Invoke());
    }

    private void AddItem(string header, bool? check, Action onClick)
    {
        var item = new NativeMenuItem { Header = header };
        if (check.HasValue)
        {
            item.ToggleType = MenuItemToggleType.CheckBox;
            item.IsChecked = check.Value;
        }
        item.Click += (_, _) => onClick();
        _menu.Items.Add(item);
    }

    public void Dispose()
    {
        _trayIcon.IsVisible = false;
        TrayIcon.SetIcons(Application.Current!, []);
    }

    /// <summary>Desenha o ícone do app (quadro escuro com gráfico ciano) em runtime, como o WPF.</summary>
    private static WindowIcon? CreateAppIcon(int size)
    {
        try
        {
            var bitmap = new RenderTargetBitmap(new PixelSize(size, size), new Vector(96, 96));
            using (var context = bitmap.CreateDrawingContext())
            {
                context.DrawRectangle(
                    new ImmutableSolidColorBrush(Color.FromRgb(0x0B, 0x12, 0x20)),
                    new ImmutablePen(new ImmutableSolidColorBrush(Color.FromRgb(0x1E, 0x90, 0xFF)), 2),
                    new RoundedRect(new Rect(1, 1, size - 2, size - 2), new CornerRadius(6)));

                var line = new ImmutablePen(
                    new ImmutableSolidColorBrush(Color.FromRgb(0x00, 0xE5, 0xFF)),
                    2.4,
                    lineCap: PenLineCap.Round,
                    lineJoin: PenLineJoin.Round);
                context.DrawLine(line, new Point(size * 0.15, size * 0.62), new Point(size * 0.34, size * 0.40));
                context.DrawLine(line, new Point(size * 0.34, size * 0.40), new Point(size * 0.48, size * 0.58));
                context.DrawLine(line, new Point(size * 0.48, size * 0.58), new Point(size * 0.68, size * 0.26));
                context.DrawLine(line, new Point(size * 0.68, size * 0.26), new Point(size * 0.85, size * 0.46));
            }
            return new WindowIcon(bitmap);
        }
        catch (Exception ex)
        {
            ErrorLog.Write("TrayIcon.CreateIcon", ex);
            return null;
        }
    }
}
