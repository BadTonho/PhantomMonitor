using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Avalonia.Controls;
using Avalonia.Threading;
using PhantomMonitor.App.ViewModels;
using PhantomMonitor.Services;

namespace PhantomMonitor.App.Views;

// Serviços de janela do overlay no Windows (porta do OverlayWindow do WPF — F1.3):
// 1) clique-through real via WS_EX_TRANSPARENT (+LAYERED/NOACTIVATE/TOOLWINDOW),
//    como o painel do WPF ("IsHitTestVisible=false" equivalente deDllImport);
// 2) reforço de topmost a cada 500 ms (SetWindowPos HWND_TOPMOST);
// 3) posição pelo app em foco: timer de 30 ms que também roda o matcher de
//    perfis (UpdateProfileForForeground). No Linux nada disso roda — a janela
//    continua arrastável, clicável e Topmost fixo.
//   Consequência paritária: no Windows o overlay não é arrastável nem tem o ×
//   clicável (igual ao WPF); o controle fica no dashboard ("Ocultar overlay")
//   e os offsets do config/perfis.
public sealed partial class OverlayWindow : Window
{
    private DispatcherTimer? _followTimer;
    private DispatcherTimer? _topmostTimer;

    private MainWindowViewModel? OverlayViewModel => DataContext as MainWindowViewModel;

    [SupportedOSPlatform("windows")]
    private void InitializeWindowsServices()
    {
        MakeClickThrough();
        _topmostTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _topmostTimer.Tick += (_, _) => MaintainTopmost();
        _topmostTimer.Start();

        _followTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(30) };
        _followTimer.Tick += (_, _) => UpdatePosition();
        _followTimer.Start();
    }

    private void ShutdownWindowsServices()
    {
        _followTimer?.Stop();
        _topmostTimer?.Stop();
        _followTimer = null;
        _topmostTimer = null;
    }

    [SupportedOSPlatform("windows")]
    private void UpdatePosition()
    {
        if (!IsVisible) return;

        var vm = OverlayViewModel;
        if (vm is null) return;

        var foreground = Win32Interop.GetForegroundWindow();
        if (foreground == IntPtr.Zero) return;

        Win32Interop.GetWindowThreadProcessId(foreground, out var pid);

        // Não persiga a si mesmo nem o explorer (área de trabalho com nada em foco útil).
        if (pid == (uint)Process.GetCurrentProcess().Id) return;
        if (pid == GetExplorerPid()) return;

        // Perfis por aplicativo: cascata AppMatch / pin — aplica overrides ao VM
        // (offsets, largura, acento, fonte, whitelist). Trabalho real só no switch.
        vm.UpdateProfileForForeground(pid);

        if (!GetWindowRect(foreground, out var rect)) return;
        if (rect.Left <= -32000) return; // janela minimizada (truque do Windows)

        // GetWindowRect devolve PIXELS FÍSICOS; a posição da Window é DIP.
        var scale = RenderScaling;
        Position = new Avalonia.PixelPoint(
            (int)(rect.Left / scale + vm.FollowOffsetX),
            (int)(rect.Top / scale + vm.FollowOffsetY));
    }

    // ---------- PID do explorer com cache de 10 s (WPF verbatim) ----------

    private static uint _explorerPid;
    private static DateTime _explorerPidCheckedAt = DateTime.MinValue;

    [SupportedOSPlatform("windows")]
    private static uint GetExplorerPid()
    {
        if (_explorerPid != 0 && DateTime.UtcNow - _explorerPidCheckedAt < TimeSpan.FromSeconds(10))
            return _explorerPid;

        _explorerPidCheckedAt = DateTime.UtcNow;

        if (_explorerPid != 0)
        {
            // O explorer pode reiniciar e o PID em cache deixa de existir → re-resolve.
            try
            {
                using var process = Process.GetProcessById((int)_explorerPid);
                if (!process.HasExited) return _explorerPid;
            }
            catch
            {
                // PID inválido/saído: cai no re-resolve abaixo.
            }
            _explorerPid = 0;
        }

        var processes = Process.GetProcessesByName("explorer");
        if (processes.Length > 0)
        {
            using var first = processes[0];
            _explorerPid = (uint)first.Id;
        }
        foreach (var process in processes) process.Dispose();
        return _explorerPid;
    }

    // ---------- Clique-through + topmost ----------

    [SupportedOSPlatform("windows")]
    private void MakeClickThrough()
    {
        if (TryGetPlatformHandle()?.Handle is not { } handle || handle == IntPtr.Zero)
        {
            // Handle ainda não existe (janela não mostrada): o reforço do topmost
            // reaplica o estilo no primeiro tick de 500 ms.
            return;
        }

        SetExTransparent(handle);
    }

    [SupportedOSPlatform("windows")]
    private void MaintainTopmost()
    {
        if (!IsVisible) return;

        if (TryGetPlatformHandle()?.Handle is not { } handle || handle == IntPtr.Zero) return;

        if (OperatingSystem.IsWindows())
            SetWindowPos(handle, HwndTopmost, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate);

        // Primeiro handle válido visto: aplica o clique-through.
        if (!_clickThroughApplied)
        {
            _clickThroughApplied = true;
            SetExTransparent(handle);
        }
    }

    private bool _clickThroughApplied;

    [SupportedOSPlatform("windows")]
    private void SetExTransparent(IntPtr handle)
    {
        var exStyle = GetWindowLongPtr(handle, GwlExstyle).ToInt64();
        SetWindowLongPtr(handle, GwlExstyle,
            new IntPtr(exStyle | WsExLayered | WsExTransparent | WsExTopmost | WsExNoActivate | WsExToolWindow));
    }

    private const int GwlExstyle = -20;
    private const long WsExTransparent = 0x00000020;
    private const long WsExLayered = 0x00080000;
    private const long WsExTopmost = 0x00000008;
    private const long WsExNoActivate = 0x08000000;
    private const long WsExToolWindow = 0x00000080;
    private static readonly IntPtr HwndTopmost = new(-1);
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoActivate = 0x0010;

    // GetWindowLong/SetWindowLong 32 bits não são recomendados em processos x64 —
    // usa as variantes *_PtrW com fallback para WindowLongW em 32 bits (WPF verbatim).
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)]
    private static extern int GetWindowLong32(IntPtr hwnd, int index);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtr64(IntPtr hwnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
    private static extern int SetWindowLong32(IntPtr hwnd, int index, int newStyle);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr64(IntPtr hwnd, int index, IntPtr newStyle);

    [SupportedOSPlatform("windows")]
    private static IntPtr GetWindowLongPtr(IntPtr hwnd, int index) =>
        IntPtr.Size == 4 ? new IntPtr(GetWindowLong32(hwnd, index)) : GetWindowLongPtr64(hwnd, index);

    [SupportedOSPlatform("windows")]
    private static IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr newStyle) =>
        IntPtr.Size == 4
            ? new IntPtr(SetWindowLong32(hwnd, index, (int)newStyle))
            : SetWindowLongPtr64(hwnd, index, newStyle);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

    [StructLayout(LayoutKind.Sequential)]
    internal struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
}
