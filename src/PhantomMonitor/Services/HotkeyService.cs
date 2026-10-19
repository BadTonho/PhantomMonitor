using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace PhantomMonitor.Services;

/// <summary>
/// Atalho de teclado global via <c>RegisterHotKey</c> do Win32: funciona mesmo com um jogo
/// em foco, sem injeção de DLL nem hooks de teclado. Requer o HWND de uma janela WPF
/// (mensagens <c>WM_HOTKEY</c> são entregues ao WndProc dessa janela).
/// </summary>
public sealed class HotkeyService : IDisposable
{
    private const int WmHotkey = 0x0312;
    private const uint ModNoRepeat = 0x4000;

    /// <summary>Identificador Win32 do atalho de overlay ("PH" = 0x5048) — legacy/default.</summary>
    public const int DefaultHotkeyId = 0x5048;

    /// <summary>Identificador Win32 do 2º atalho global (pausar monitoramento, P6.7).</summary>
    public const int PauseHotkeyId = 0x5049;

    private readonly int _hotkeyId;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private HwndSource? _source;
    private IntPtr _hwnd;
    private bool _registered;
    private Action? _callback;

    /// <param name="hotkeyId">
    /// Identificador Win32 único dentro do HWND: uma mesma janela pode registrar
    /// vários atalhos (overlay + pausa) sem conflito de mensagem.
    /// </param>
    public HotkeyService(int hotkeyId = DefaultHotkeyId)
    {
        _hotkeyId = hotkeyId;
    }

    /// <summary>
    /// Registra o atalho descrito por <paramref name="spec"/> (ex.: "Ctrl+Shift+O").
    /// Retorna <c>false</c> quando a especificação é inválida/vazia ou quando a combinação
    /// já está em uso por outro aplicativo.
    /// </summary>
    public bool Register(Window window, string? spec, Action callback)
    {
        Unregister();

        if (string.IsNullOrWhiteSpace(spec)) return false;

        if (!HotkeyParser.TryParse(spec, out uint modifiers, out uint vk))
        {
            ErrorLog.Write("Hotkey", $"Especificação de atalho inválida: '{spec}'");
            return false;
        }

        _hwnd = new WindowInteropHelper(window).Handle;
        if (_hwnd == IntPtr.Zero)
        {
            ErrorLog.Write("Hotkey", "Janela ainda não possui HWND para registrar o atalho.");
            return false;
        }

        _source = HwndSource.FromHwnd(_hwnd);
        if (_source == null)
        {
            ErrorLog.Write("Hotkey", "Não foi possível obter o HwndSource da janela principal.");
            return false;
        }

        if (!RegisterHotKey(_hwnd, _hotkeyId, modifiers | ModNoRepeat, vk))
        {
            ErrorLog.Write("Hotkey",
                $"Falha ao registrar o atalho global '{spec}' (Win32 {Marshal.GetLastWin32Error()}) — combinação já em uso?");
            return false;
        }

        _source.AddHook(WndProc);
        _callback = callback;
        _registered = true;
        return true;
    }

    public void Unregister()
    {
        if (_source != null)
        {
            _source.RemoveHook(WndProc);
            _source = null;
        }

        if (_registered && _hwnd != IntPtr.Zero)
        {
            UnregisterHotKey(_hwnd, _hotkeyId);
        }

        _registered = false;
        _callback = null;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmHotkey && wParam.ToInt32() == _hotkeyId)
        {
            _callback?.Invoke();
            handled = true;
        }
        return IntPtr.Zero;
    }

    public void Dispose() => Unregister();
}
