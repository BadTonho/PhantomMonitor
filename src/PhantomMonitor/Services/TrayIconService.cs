using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PhantomMonitor.Services;

/// <summary>
/// Ícone na bandeja do sistema via <c>Shell_NotifyIcon</c> (Win32 puro, sem WinForms):
/// clique esquerdo ativa a janela principal e clique direito abre o menu de contexto
/// (Mostrar, Overlay no Jogo, Iniciar com o Windows, Sair). O ícone é desenhado em
/// runtime com WPF (RenderTargetBitmap) e convertido para HICON — sem assets externos.
/// </summary>
public sealed class TrayIconService : IDisposable
{
    private const int CallbackMessage = 0x8000 + 1; // WM_APP + 1
    private const int WmContextMenu = 0x007B;
    private const int WmRButtonUp = 0x0205;
    private const int WmLButtonUp = 0x0202;
    private const int WmLButtonDblClk = 0x0203;
    private const int NinSelect = 0x0400;

    private const uint NifMessage = 0x01;
    private const uint NifIcon = 0x02;
    private const uint NifTip = 0x04;
    private const uint NifShowTip = 0x80;
    private const uint NimAdd = 0x00;
    private const uint NimDelete = 0x02;
    private const uint NimSetVersion = 0x04;
    private const uint NotifyIconVersion4 = 4;

    private const uint MfString = 0x00;
    private const uint MfChecked = 0x08;
    private const uint MfSeparator = 0x800;
    private const uint TpmRightButton = 0x02;
    private const uint TpmReturnCmd = 0x100;
    private const uint TpmNoNotify = 0x80;

    private const uint MenuShow = 1;
    private const uint MenuOverlay = 2;
    private const uint MenuStartup = 3;
    private const uint MenuExit = 4;

    private const int DefaultIconId = 32512; // IDI_APPLICATION

    private readonly Window _window;
    private readonly int _iconId = 1;
    private HwndSource? _source;
    private IntPtr _hwnd;
    private IntPtr _icon;
    private bool _ownsIcon;
    private bool _created;

    /// <summary>Clique esquerdo/duplo no ícone: trazer a janela principal para frente.</summary>
    public event Action? ActivateRequested;

    /// <summary>Menu: alternar o overlay OSD.</summary>
    public event Action? OverlayToggleRequested;

    /// <summary>Menu: alternar a inicialização com o Windows.</summary>
    public event Action? StartupToggleRequested;

    /// <summary>Menu: encerrar o aplicativo.</summary>
    public event Action? ExitRequested;

    /// <summary>Estado atual do overlay (para o check do menu).</summary>
    public Func<bool>? IsOverlayVisible { get; set; }

    /// <summary>Estado atual da inicialização com o Windows (para o check do menu).</summary>
    public Func<bool>? IsStartupEnabled { get; set; }

    public TrayIconService(Window window, string tooltip)
    {
        _window = window;
        _hwnd = new WindowInteropHelper(window).Handle;
        if (_hwnd == IntPtr.Zero)
        {
            ErrorLog.Write("TrayIcon", "Janela principal ainda não possui HWND.");
            return;
        }

        _source = HwndSource.FromHwnd(_hwnd);
        if (_source == null)
        {
            ErrorLog.Write("TrayIcon", "Não foi possível obter o HwndSource da janela principal.");
            return;
        }

        _icon = CreateAppIcon(32);
        if (_icon == IntPtr.Zero)
        {
            _icon = LoadIcon(IntPtr.Zero, (IntPtr)DefaultIconId);
            _ownsIcon = false; // ícone compartilhado do sistema: não destruir
        }
        else
        {
            _ownsIcon = true;
        }

        var data = BuildData();
        data.uFlags = NifMessage | NifIcon | NifTip | NifShowTip;
        data.uCallbackMessage = CallbackMessage;
        data.szTip = tooltip;

        if (!Shell_NotifyIcon(NimAdd, ref data))
        {
            ErrorLog.Write("TrayIcon", $"Shell_NotifyIcon(NIM_ADD) falhou (Win32 {Marshal.GetLastWin32Error()}).");
            if (_ownsIcon && _icon != IntPtr.Zero) DestroyIcon(_icon);
            _icon = IntPtr.Zero;
            return;
        }

        // Versão 4: eventos modernos (NIN_SELECT/WM_CONTEXTMENU) e respeito a DPI no posicionamento.
        data.uVersion = NotifyIconVersion4;
        Shell_NotifyIcon(NimSetVersion, ref data);

        _source.AddHook(WndProc);
        _created = true;
    }

    private NOTIFYICONDATA BuildData() => new()
    {
        cbSize = Marshal.SizeOf<NOTIFYICONDATA>(),
        hWnd = _hwnd,
        uID = _iconId,
        hIcon = _icon,
        szTip = "",
        szInfo = "",
        szInfoTitle = ""
    };

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != CallbackMessage) return IntPtr.Zero;

        // Com a versão 4 da tray, o clique direito chega como WM_CONTEXTMENU e o
        // lParam carrega COORDENADAS do mouse (X empacotado na loword) — não um código
        // de mensagem. Por isso a comparação usa o valor completo de lParam: comparar
        // a loword mascarada contra 0x007B só casava se o ponteiro estivesse a X = 123 px,
        // deixando o menu de contexto inacessível na prática.
        long evt = lParam.ToInt64();

        if (evt is NinSelect or WmLButtonUp or WmLButtonDblClk)
        {
            ActivateRequested?.Invoke();
            handled = true;
        }
        else if (evt is WmContextMenu or WmRButtonUp)
        {
            ShowContextMenu();
            handled = true;
        }

        return IntPtr.Zero;
    }

    private void ShowContextMenu()
    {
        var menu = CreatePopupMenu();
        if (menu == IntPtr.Zero) return;

        bool overlayChecked = IsOverlayVisible?.Invoke() == true;
        bool startupChecked = IsStartupEnabled?.Invoke() == true;

        AppendMenu(menu, MfString, MenuShow, "Mostrar Phantom Monitor");
        AppendMenu(menu, MfString | (overlayChecked ? MfChecked : 0), MenuOverlay, "Overlay no Jogo");
        AppendMenu(menu, MfString | (startupChecked ? MfChecked : 0), MenuStartup, "Iniciar com o Windows");

        // Seção dinâmica (perfis por aplicativo): itens e estados avaliados a cada
        // abertura do menu pelos proveedores registrados na janela principal.
        var dynamic = _dynamicItemsProvider?.Invoke();
        if (dynamic is { Count: > 0 })
        {
            AppendMenu(menu, MfSeparator, 0, null);
            foreach (var item in dynamic)
            {
                AppendMenu(menu, MfString | (item.Checked ? MfChecked : 0), (uint)item.Id, item.Title);
            }
        }

        AppendMenu(menu, MfSeparator, 0, null);
        AppendMenu(menu, MfString, MenuExit, "Sair");

        if (GetCursorPos(out var pt))
        {
            SetForegroundWindow(_hwnd); // garante que o menu feche ao clicar fora
            uint cmd = (uint)TrackPopupMenu(menu,
                TpmRightButton | TpmReturnCmd | TpmNoNotify,
                pt.X, pt.Y, 0, _hwnd, IntPtr.Zero);
            PostMessage(_hwnd, 0x0000 /* WM_NULL */, IntPtr.Zero, IntPtr.Zero);

            switch (cmd)
            {
                case MenuShow: ActivateRequested?.Invoke(); break;
                case MenuOverlay: OverlayToggleRequested?.Invoke(); break;
                case MenuStartup: StartupToggleRequested?.Invoke(); break;
                case MenuExit: ExitRequested?.Invoke(); break;
                default:
                    if (cmd >= MenuDynamicBase) DynamicItemClicked?.Invoke((int)cmd);
                    break;
            }
        }

        DestroyMenu(menu);
    }

    /// <summary>Item injetado na bandeja por um provedor externo (ex.: perfis do OSD).</summary>
    public sealed record MenuEntry(int Id, string Title, bool Checked);

    /// <summary>Id base dos itens dinâmicos (>= que os fixos MenuShow..MenuExit).</summary>
    public const uint MenuDynamicBase = 0x500;

    private Func<IReadOnlyList<MenuEntry>>? _dynamicItemsProvider;

    /// <summary>Registra a seção dinâmica do menu (avaliada a cada abertura) e o retorno do clique.</summary>
    public void SetDynamicItems(Func<IReadOnlyList<MenuEntry>>? provider, Action<int>? onClicked)
    {
        _dynamicItemsProvider = provider;
        DynamicItemClicked = onClicked;
    }

    private event Action<int>? DynamicItemClicked;

    /// <summary>Desenha o ícone do app (quadro escuro com gráfico ciano) e converte para HICON.</summary>
    private static IntPtr CreateAppIcon(int size)
    {
        try
        {
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                var background = new SolidColorBrush(Color.FromRgb(0x0B, 0x12, 0x20));
                var borderPen = new Pen(new SolidColorBrush(Color.FromRgb(0x1E, 0x90, 0xFF)), 2);
                dc.DrawRoundedRectangle(background, borderPen, new Rect(1, 1, size - 2, size - 2), 6, 6);

                var linePen = new Pen(new SolidColorBrush(Color.FromRgb(0x00, 0xE5, 0xFF)), 2.4)
                {
                    LineJoin = PenLineJoin.Round,
                    StartLineCap = PenLineCap.Round,
                    EndLineCap = PenLineCap.Round
                };

                var geo = new StreamGeometry();
                using (var g = geo.Open())
                {
                    g.BeginFigure(new Point(size * 0.15, size * 0.62), false, false);
                    g.LineTo(new Point(size * 0.34, size * 0.40), true, false);
                    g.LineTo(new Point(size * 0.48, size * 0.58), true, false);
                    g.LineTo(new Point(size * 0.68, size * 0.26), true, false);
                    g.LineTo(new Point(size * 0.85, size * 0.46), true, false);
                }
                geo.Freeze();
                dc.DrawGeometry(null, linePen, geo);
            }

            var rtb = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(visual);

            int stride = size * 4;
            var pixels = new byte[size * stride];
            rtb.CopyPixels(pixels, stride, 0);

            var bmi = new BITMAPINFO
            {
                bmiHeader = new BITMAPINFOHEADER
                {
                    biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>(),
                    biWidth = size,
                    biHeight = -size, // top-down
                    biPlanes = 1,
                    biBitCount = 32,
                    biCompression = 0 // BI_RGB
                }
            };

            var hBitmap = CreateDIBSection(IntPtr.Zero, ref bmi, 0, out var bits, IntPtr.Zero, 0);
            if (hBitmap == IntPtr.Zero || bits == IntPtr.Zero) return IntPtr.Zero;

            Marshal.Copy(pixels, 0, bits, pixels.Length);

            var hMask = CreateBitmap(size, size, 1, 1, null); // máscara 1bpp (o canal alfa do RGB manda)
            var info = new ICONINFO { fIcon = true, hbmMask = hMask, hbmColor = hBitmap };
            var hIcon = CreateIconIndirect(ref info);

            DeleteObject(hBitmap);
            if (hMask != IntPtr.Zero) DeleteObject(hMask);
            return hIcon;
        }
        catch (Exception ex)
        {
            ErrorLog.Write("TrayIcon.CreateIcon", ex);
            return IntPtr.Zero;
        }
    }

    public void Dispose()
    {
        if (_created)
        {
            var data = BuildData();
            data.uFlags = 0;
            Shell_NotifyIcon(NimDelete, ref data);
            _created = false;
        }

        if (_source != null)
        {
            _source.RemoveHook(WndProc);
            _source = null;
        }

        if (_ownsIcon && _icon != IntPtr.Zero) DestroyIcon(_icon);
        _icon = IntPtr.Zero;
    }

    // ---------------- Win32 ----------------

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATA
    {
        public int cbSize;
        public IntPtr hWnd;
        public int uID;
        public uint uFlags;
        public int uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
        public uint dwState;
        public uint dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
        public uint uVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
        public uint dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ICONINFO
    {
        [MarshalAs(UnmanagedType.Bool)] public bool fIcon;
        public int xHotspot;
        public int yHotspot;
        public IntPtr hbmMask;
        public IntPtr hbmColor;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RGBQUAD
    {
        public byte b;
        public byte g;
        public byte r;
        public byte reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public uint biSize;
        public int biWidth;
        public int biHeight;
        public ushort biPlanes;
        public ushort biBitCount;
        public uint biCompression;
        public uint biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public uint biClrUsed;
        public uint biClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFO
    {
        public BITMAPINFOHEADER bmiHeader;
        public RGBQUAD bmiColors;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool Shell_NotifyIcon(uint dwMessage, ref NOTIFYICONDATA lpData);

    [DllImport("user32.dll")]
    private static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool AppendMenu(IntPtr hMenu, uint uFlags, uint uIDNewItem, string? lpNewItem);

    [DllImport("user32.dll")]
    private static extern bool DestroyMenu(IntPtr hMenu);

    [DllImport("user32.dll")]
    private static extern int TrackPopupMenu(IntPtr hMenu, uint uFlags, int x, int y, int nReserved, IntPtr hWnd, IntPtr prcRect);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT p);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern IntPtr LoadIcon(IntPtr hInstance, IntPtr lpIconName);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);

    [DllImport("user32.dll")]
    private static extern IntPtr CreateIconIndirect(ref ICONINFO piconinfo);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPINFO pbmi, uint usage, out IntPtr ppvBits, IntPtr hSection, uint offset);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateBitmap(int width, int height, uint planes, uint bitCount, byte[]? bits);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);
}
