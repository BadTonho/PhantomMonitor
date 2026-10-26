using System.ComponentModel;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace PhantomMonitor.Services;

/// <summary>
/// Atalhos globais Win32 (<c>RegisterHotKey</c>, paridade do HotkeyService do WPF)
/// sem depender de janela WPF/Avalonia: uma janela <b>message-only</b> vive numa
/// thread dedicada com bomba de mensagens — <c>WM_HOTKEY</c> chega lá e o callback é
/// acionado na própria thread do serviço (o chamador decide o marshaling: para a UI,
/// publique via <c>Dispatcher.UIThread</c>). Diferente de hooks de teclado, não
/// captura input global — só combinações registradas com modificador/F-key.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class GlobalHotkeyService : IDisposable
{
    /// <summary>Identificador Win32 do atalho de overlay ("PH" = 0x5048) — paridade do WPF.</summary>
    public const int DefaultHotkeyId = 0x5048;

    /// <summary>Identificador Win32 do 2º atalho global (pausa do monitoramento, P6.7) — paridade do WPF.</summary>
    public const int PauseHotkeyId = 0x5049;

    private const int WmHotkey = 0x0312;
    private const uint ModNoRepeat = 0x4000;
    private const uint WmClose = 0x0010;
    private const uint WmDestroy = 0x0002;
    private const uint WmWork = 0x8001;

    private static readonly IntPtr ClassName = Marshal.StringToHGlobalUni("PhantomMonitorHotkeys");
    private static readonly object ClassLock = new();
    private static ushort _classAtom;
    private static readonly ConcurrentDictionary<IntPtr, GlobalHotkeyService> Owners = new();
    private readonly ConcurrentQueue<(Func<bool> Action, TaskCompletionSource<bool> Completion)> _work = new();

    private sealed record Registration(Action Callback);

    private readonly Dictionary<int, Registration> _registrations = [];
    private readonly ManualResetEventSlim _windowReady = new(false);
    private Thread? _messageThread;
    private IntPtr _hwnd;
    private int _disposed;

    public GlobalHotkeyService()
    {
        EnsureWindowThread();
    }

    /// <summary>
    /// Registra a combinação descrita por <paramref name="spec"/> (ex.: "Ctrl+Shift+O")
    /// sob o <paramref name="hotkeyId"/>. Re-registrar a mesma id troca a combinação
    /// (como o WPF). Retorna <c>false</c> quando a especificação é inválida/vazia ou
    /// quando a combinação já está em uso por outro aplicativo (ambos vão para o ErrorLog).
    /// </summary>
    public bool Register(int hotkeyId, string? spec, Action callback)
    {
        if (string.IsNullOrWhiteSpace(spec)) return false;
        if (_disposed != 0) return false;

        EnsureWindowThread();
        if (!_windowReady.Wait(TimeSpan.FromSeconds(3)) || _hwnd == IntPtr.Zero)
        {
            ErrorLog.Write("GlobalHotkey", "Thread de atalhos indisponível; atalho não registrado.");
            return false;
        }

        if (!HotkeyParser.TryParse(spec, out var modifiers, out var virtualKey))
        {
            ErrorLog.Write("GlobalHotkey", $"Especificação de atalho inválida: '{spec}'");
            return false;
        }

        return InvokeOnWindow(() => RegisterCore(hotkeyId, spec, modifiers, virtualKey, callback));
    }

    private bool RegisterCore(int hotkeyId, string spec, uint modifiers, uint virtualKey, Action callback)
    {
        lock (_registrations)
        {
            if (_registrations.TryGetValue(hotkeyId, out _))
                UnregisterHotKey(_hwnd, hotkeyId);
            _registrations.Remove(hotkeyId);

            if (!RegisterHotKey(_hwnd, hotkeyId, (uint)(modifiers | (int)ModNoRepeat), virtualKey))
            {
                ErrorLog.Write("GlobalHotkey",
                    $"RegisterHotKey('{spec}') falhou (Win32 {Marshal.GetLastWin32Error()}) — a combinação pode estar em uso por outro aplicativo.");
                return false;
            }

            _registrations[hotkeyId] = new Registration(callback);
        }
        return true;
    }

    public void UnregisterAll()
        => InvokeOnWindow(() => { UnregisterCore(); return true; });

    /// <summary>Remove apenas um atalho, preservando as demais ações registradas.</summary>
    public bool Unregister(int hotkeyId) => InvokeOnWindow(() =>
    {
        lock (_registrations)
        {
            if (!_registrations.ContainsKey(hotkeyId)) return true;
            if (!UnregisterHotKey(_hwnd, hotkeyId)) return false;
            _registrations.Remove(hotkeyId);
            return true;
        }
    });

    private void UnregisterCore()
    {
        lock (_registrations)
        {
            if (_hwnd != IntPtr.Zero)
                foreach (var hotkeyId in _registrations.Keys)
                    UnregisterHotKey(_hwnd, hotkeyId);
            _registrations.Clear();
        }
    }

    private bool InvokeOnWindow(Func<bool> action)
    {
        if (!_windowReady.Wait(TimeSpan.FromSeconds(3)) || _hwnd == IntPtr.Zero) return false;
        if (Thread.CurrentThread == _messageThread) return action();
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _work.Enqueue((action, completion));
        if (!PostMessageW(_hwnd, WmWork, IntPtr.Zero, IntPtr.Zero)) completion.TrySetCanceled();
        try { return completion.Task.WaitAsync(TimeSpan.FromSeconds(3)).GetAwaiter().GetResult(); }
        catch (Exception ex) when (ex is TimeoutException or OperationCanceledException)
        {
            completion.TrySetCanceled();
            ErrorLog.Write("GlobalHotkey", "A thread de atalhos não respondeu ao comando.");
            return false;
        }
    }

    private void DrainWork()
    {
        while (_work.TryDequeue(out var work))
        {
            if (work.Completion.Task.IsCompleted) continue;
            try { work.Completion.TrySetResult(work.Action()); }
            catch (Exception ex) { ErrorLog.Write("GlobalHotkey.Command", ex); work.Completion.TrySetResult(false); }
        }
    }

    /// <summary>Thread única com janela message-only: created uma vez; sobrevive até o Dispose.</summary>
    private void EnsureWindowThread()
    {
        if (_messageThread is { IsAlive: true }) return;

        _windowReady.Reset();
        _messageThread = new Thread(RunMessageLoop)
        {
            IsBackground = true,
            Name = "PhantomMonitor.GlobalHotkeys"
        };
        _messageThread.Start();
    }

    private void RunMessageLoop()
    {
        try
        {
            EnsureWindowClass();
            // Parent HWND_MESSAGE: janela message-only — nunca aparece na tela,
            // não entra no Alt+Tab e só recebe mensagens despachadas.
            _hwnd = CreateWindowExW(0, ClassName, ClassName, 0, 0, 0, 0, 0, new IntPtr(HwndMessage), IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
            if (_hwnd == IntPtr.Zero)
            {
                ErrorLog.Write("GlobalHotkey", $"CreateWindowEx (message-only) falhou (Win32 {Marshal.GetLastWin32Error()}).");
                _windowReady.Set();
                return;
            }

            Owners[_hwnd] = this;
            _windowReady.Set();
            while (GetMessageW(out var msg, IntPtr.Zero, 0, 0) > 0)
            {
                TranslateMessage(ref msg);
                DispatchMessageW(ref msg);
            }
        }
        finally
        {
            Owners.TryRemove(_hwnd, out _);
            _hwnd = IntPtr.Zero;
            _windowReady.Set();
            while (_work.TryDequeue(out var work)) work.Completion.TrySetCanceled();
        }
    }

    private static void EnsureWindowClass()
    {
        lock (ClassLock)
        {
            if (_classAtom != 0) return;

            var wndClass = new WndClassEx
            {
                CbSize = (uint)Marshal.SizeOf<WndClassEx>(),
                // O delegate fica em campo estático: o GC não pode coletar o
                // ponteiro de função enquanto o serviço existir.
                LpfnWndProc = Marshal.GetFunctionPointerForDelegate(WndProcThunk),
                HInstance = GetModuleHandleW(null),
                LpszClassName = ClassName
            };
            _classAtom = RegisterClassExW(ref wndClass);
            if (_classAtom == 0)
            {
                var error = Marshal.GetLastWin32Error();
                // 1410 = ERROR_CLASS_ALREADY_EXISTS (outra instância registrou antes).
                if (error != 1410)
                    ErrorLog.Write("GlobalHotkey", $"RegisterClassEx falhou (Win32 {error}).");
            }
        }
    }

    /// <summary>Delegate do WndProc, ligado em campo estático pela vida do processo.</summary>
    private static readonly WndClassProcThunk WndProcThunk = WndProc;

    private delegate IntPtr WndClassProcThunk(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

    private static IntPtr WndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == WmHotkey)
        {
            if (Owners.TryGetValue(hwnd, out var owner)) owner.Dispatch((int)wParam);
            return IntPtr.Zero;
        }
        if (msg == WmWork)
        {
            if (Owners.TryGetValue(hwnd, out var owner)) owner.DrainWork();
            return IntPtr.Zero;
        }
        if (msg == WmClose)
        {
            if (Owners.TryGetValue(hwnd, out var owner)) owner.UnregisterCore();
            DestroyWindow(hwnd);
            return IntPtr.Zero;
        }
        if (msg == WmDestroy)
        {
            PostQuitMessage(0);
            return IntPtr.Zero;
        }
        return DefWindowProcW(hwnd, msg, wParam, lParam);
    }

    private void Dispatch(int hotkeyId)
    {
        Action? callback;
        lock (_registrations)
        {
            if (_registrations.TryGetValue(hotkeyId, out var registration))
                callback = registration.Callback;
            else
                return;
        }
        try { callback(); }
        catch (Exception ex) { ErrorLog.Write("GlobalHotkey.Callback", ex); }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

        _windowReady.Wait(TimeSpan.FromSeconds(3));
        if (_hwnd != IntPtr.Zero)
            PostMessageW(_hwnd, WmClose, IntPtr.Zero, IntPtr.Zero);
        if (_messageThread != Thread.CurrentThread && _messageThread?.Join(TimeSpan.FromSeconds(3)) == true)
            _windowReady.Dispose();
    }

    // ---------------- Win32 ----------------

    private const int HwndMessage = -3; // HWND_MESSAGE: parent for message-only windows

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeMessage
    {
        public IntPtr Hwnd;
        public uint Message;
        public IntPtr WParam;
        public IntPtr LParam;
        public int Time;
        public int X;
        public int Y;
        public uint Private;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WndClassEx
    {
        public uint CbSize;
        public uint Style;
        public IntPtr LpfnWndProc;
        public int CbClsExtra;
        public int CbWndExtra;
        public IntPtr HInstance;
        public IntPtr HIcon;
        public IntPtr HCursor;
        public IntPtr HbrBackground;
        public IntPtr LpszMenuName;
        public IntPtr LpszClassName;
        public IntPtr HIconSm;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern ushort RegisterClassExW(ref WndClassEx lpWndClass);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateWindowExW(uint dwExStyle, IntPtr lpClassName, IntPtr lpWindowName,
        uint dwStyle, int x, int y, int nWidth, int nHeight, IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

    [DllImport("user32.dll")]
    private static extern IntPtr DefWindowProcW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int GetMessageW(out NativeMessage lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage(ref NativeMessage lpMsg);

    [DllImport("user32.dll")]
    private static extern IntPtr DispatchMessageW(ref NativeMessage lpMsg);

    [DllImport("user32.dll")]
    private static extern void PostQuitMessage(int nExitCode);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PostMessageW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandleW(string? lpModuleName);
}
