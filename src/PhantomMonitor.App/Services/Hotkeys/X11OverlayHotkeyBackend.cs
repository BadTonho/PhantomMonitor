using System.Runtime.InteropServices;
using PhantomMonitor.Services;

namespace PhantomMonitor.App.Services.Hotkeys;

/// <summary>Uses a dedicated XCB connection, so checked grab errors never affect Avalonia's X11 connection.</summary>
internal sealed class X11OverlayHotkeyBackend(Action activated, Action<string> unavailable) : IOverlayHotkeyBackend
{
    private readonly object _gate = new();
    private IntPtr _connection;
    private uint _root;
    private Thread? _thread;
    private bool _disposed;
    private byte _keycode;
    private ushort[] _masks = [];
    private string _active = "";
    private readonly X11KeyRepeatFilter _repeat = new();

    public Task<HotkeyResult> ApplyAsync(string shortcut)
    {
        lock (_gate)
        {
            if (_disposed) return Task.FromResult(HotkeyResult.Failed("O serviço de atalhos foi encerrado."));
            if (string.Equals(shortcut, _active, StringComparison.OrdinalIgnoreCase) || HotkeyCapture.SameCombination(shortcut, _active))
                return Task.FromResult(HotkeyResult.Active(shortcut));
            if (shortcut.Length == 0)
            {
                Ungrab(_keycode, _masks);
                _keycode = 0;
                _masks = [];
                _active = "";
                _repeat.Reset();
                return Task.FromResult(HotkeyResult.Active(""));
            }
            if (!HotkeyParser.TryParse(shortcut, out var modifiers, out var key))
                return Task.FromResult(HotkeyResult.Failed("Combinação inválida."));
            if (!EnsureConnected())
                return Task.FromResult(HotkeyResult.Failed("Não foi possível acessar a sessão X11 para registrar o atalho."));

            var mapping = ReadKeyboardMapping();
            if (mapping is null) return Task.FromResult(HotkeyResult.Failed("Não foi possível ler o mapa do teclado X11."));
            var keysym = key is >= 0x70 and <= 0x87 ? 0xFFBEu + key - 0x70 : key;
            var code = mapping.Find(keysym);
            if (code == 0) return Task.FromResult(HotkeyResult.Failed("Esta tecla não está disponível no mapa de teclado X11."));
            var alt = mapping.ModifierFor(0xFFE9, 0xFFEA);
            var super = mapping.ModifierFor(0xFFEB, 0xFFEC);
            if (((modifiers & HotkeyParser.ModAlt) != 0 && alt == 0)
                || ((modifiers & HotkeyParser.ModWin) != 0 && super == 0))
                return Task.FromResult(HotkeyResult.Failed("O modificador escolhido não está disponível no teclado X11."));
            ushort baseMask = 0;
            if ((modifiers & HotkeyParser.ModShift) != 0) baseMask |= 1;
            if ((modifiers & HotkeyParser.ModControl) != 0) baseMask |= 4;
            if ((modifiers & HotkeyParser.ModAlt) != 0) baseMask |= alt;
            if ((modifiers & HotkeyParser.ModWin) != 0) baseMask |= super;
            var locks = new[] { (ushort)2, mapping.ModifierFor(0xFF7F), mapping.ModifierFor(0xFF14) };
            var masks = BuildMasks(baseMask, locks);
            var grabbed = new List<ushort>();
            foreach (var mask in masks)
            {
                if (code == _keycode && _masks.Contains(mask)) continue;
                var cookie = Native.xcb_grab_key_checked(_connection, 0, _root, mask, code, 1, 1);
                var error = Native.xcb_request_check(_connection, cookie);
                if (error != IntPtr.Zero)
                {
                    Native.free(error);
                    Ungrab(code, grabbed);
                    return Task.FromResult(HotkeyResult.Failed("Não foi possível registrar o atalho no X11. A combinação pode estar em uso."));
                }
                grabbed.Add(mask);
            }
            Ungrab(_keycode, code == _keycode ? _masks.Except(masks) : _masks);
            _keycode = code;
            _masks = masks;
            _active = shortcut;
            _repeat.Reset();
            Native.xcb_flush(_connection);
            return Task.FromResult(HotkeyResult.Active(shortcut));
        }
    }

    internal static ushort[] BuildMasks(ushort baseMask, IEnumerable<ushort> lockMasks)
    {
        var combinations = new HashSet<ushort> { baseMask };
        foreach (var mask in lockMasks.Where(mask => mask != 0))
            foreach (var existing in combinations.ToArray()) combinations.Add((ushort)(existing | mask));
        return combinations.Order().ToArray();
    }

    private bool EnsureConnected()
    {
        if (_connection != IntPtr.Zero) return Native.xcb_connection_has_error(_connection) == 0;
        _connection = Native.xcb_connect(null, out var screenNumber);
        if (_connection == IntPtr.Zero || Native.xcb_connection_has_error(_connection) != 0)
        {
            if (_connection != IntPtr.Zero) Native.xcb_disconnect(_connection);
            _connection = IntPtr.Zero;
            return false;
        }
        var screens = Native.xcb_setup_roots_iterator(Native.xcb_get_setup(_connection));
        for (var i = 0; i < screenNumber && screens.Rem > 0; i++) Native.xcb_screen_next(ref screens);
        if (screens.Data == IntPtr.Zero) { Native.xcb_disconnect(_connection); _connection = IntPtr.Zero; return false; }
        _root = unchecked((uint)Marshal.ReadInt32(screens.Data));
        _thread = new Thread(PollEvents) { IsBackground = true, Name = "PhantomMonitor.X11Hotkey" };
        _thread.Start();
        return true;
    }

    private KeyboardMap? ReadKeyboardMapping()
    {
        var setup = Native.xcb_get_setup(_connection);
        var min = Marshal.ReadByte(setup, 34);
        var max = Marshal.ReadByte(setup, 35);
        var count = max - min + 1;
        if (count is < 1 or > 255) return null;
        var reply = Native.xcb_get_keyboard_mapping_reply(_connection, Native.xcb_get_keyboard_mapping(_connection, min, (byte)count), IntPtr.Zero);
        if (reply == IntPtr.Zero) return null;
        try
        {
            var perKey = Marshal.ReadByte(reply, 1);
            var length = Native.xcb_get_keyboard_mapping_keysyms_length(reply);
            var data = Native.xcb_get_keyboard_mapping_keysyms(reply);
            var symbols = new uint[length];
            for (var i = 0; i < length; i++) symbols[i] = unchecked((uint)Marshal.ReadInt32(data, i * 4));
            var modifierReply = Native.xcb_get_modifier_mapping_reply(_connection, Native.xcb_get_modifier_mapping(_connection), IntPtr.Zero);
            if (modifierReply == IntPtr.Zero) return null;
            try
            {
                var perModifier = Marshal.ReadByte(modifierReply, 1);
                var keycodes = new byte[perModifier * 8];
                if (keycodes.Length > 0) Marshal.Copy(Native.xcb_get_modifier_mapping_keycodes(modifierReply), keycodes, 0, keycodes.Length);
                return new KeyboardMap(min, perKey, symbols, perModifier, keycodes);
            }
            finally { Native.free(modifierReply); }
        }
        finally { Native.free(reply); }
    }

    private sealed record KeyboardMap(byte Min, byte PerKey, uint[] Symbols, byte PerModifier, byte[] Modifiers)
    {
        public byte Find(params uint[] symbols)
        {
            if (PerKey == 0) return 0;
            for (var i = 0; i < Symbols.Length; i++)
                if (symbols.Contains(Symbols[i])) return (byte)(Min + i / PerKey);
            return 0;
        }

        public ushort ModifierFor(params uint[] symbols)
        {
            ushort result = 0;
            for (var i = 0; i < Modifiers.Length; i++)
            {
                var code = Modifiers[i];
                if (code < Min || PerKey == 0) continue;
                var offset = (code - Min) * PerKey;
                for (var j = 0; j < PerKey && offset + j < Symbols.Length; j++)
                    if (symbols.Contains(Symbols[offset + j])) result |= (ushort)(1 << (i / PerModifier));
            }
            return result;
        }
    }

    private void Ungrab(byte code, IEnumerable<ushort> masks)
    {
        if (_connection == IntPtr.Zero || code == 0) return;
        foreach (var mask in masks) Native.xcb_ungrab_key(_connection, code, _root, mask);
        Native.xcb_flush(_connection);
    }

    private void PollEvents()
    {
        while (true)
        {
            var fire = false;
            var hadEvent = false;
            var disconnected = false;
            lock (_gate)
            {
                if (_disposed) return;
                if (Native.xcb_connection_has_error(_connection) != 0)
                {
                    Native.xcb_disconnect(_connection);
                    _connection = IntPtr.Zero;
                    _keycode = 0;
                    _masks = [];
                    _active = "";
                    _repeat.Reset();
                    disconnected = true;
                }
                if (!disconnected)
                {
                    var evt = Native.xcb_poll_for_event(_connection);
                    if (evt != IntPtr.Zero)
                    {
                        hadEvent = true;
                        try
                        {
                            var type = Marshal.ReadByte(evt) & 0x7f;
                            if (Marshal.ReadByte(evt, 1) == _keycode && _keycode != 0)
                            {
                                var timestamp = unchecked((uint)Marshal.ReadInt32(evt, 4));
                                if (type == 2 && _masks.Contains((ushort)(Marshal.ReadInt16(evt, 28) & 0xff)))
                                    fire = _repeat.Press(timestamp);
                                else if (type == 3) _repeat.Release(timestamp, Environment.TickCount64);
                            }
                        }
                        finally { Native.free(evt); }
                    }
                    else _repeat.FlushRelease(Environment.TickCount64);
                }
            }
            if (disconnected)
            {
                unavailable("A conexão X11 foi encerrada. Salve novamente para tentar reativar o atalho.");
                return;
            }
            if (fire) activated();
            if (!hadEvent) Thread.Sleep(10);
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            Ungrab(_keycode, _masks);
        }
        if (_thread != Thread.CurrentThread) _thread?.Join(TimeSpan.FromSeconds(1));
        lock (_gate) { if (_connection != IntPtr.Zero) Native.xcb_disconnect(_connection); _connection = IntPtr.Zero; }
    }

    private static class Native
    {
        private const string Xcb = "libxcb.so.1";
        [StructLayout(LayoutKind.Sequential)] internal struct Cookie { public uint Sequence; }
        [StructLayout(LayoutKind.Sequential)] internal struct Iterator { public IntPtr Data; public int Rem; public int Index; }
        [DllImport(Xcb)] internal static extern IntPtr xcb_connect(string? display, out int screen);
        [DllImport(Xcb)] internal static extern int xcb_connection_has_error(IntPtr connection);
        [DllImport(Xcb)] internal static extern void xcb_disconnect(IntPtr connection);
        [DllImport(Xcb)] internal static extern IntPtr xcb_get_setup(IntPtr connection);
        [DllImport(Xcb)] internal static extern Iterator xcb_setup_roots_iterator(IntPtr setup);
        [DllImport(Xcb)] internal static extern void xcb_screen_next(ref Iterator iterator);
        [DllImport(Xcb)] internal static extern Cookie xcb_get_keyboard_mapping(IntPtr connection, byte first, byte count);
        [DllImport(Xcb)] internal static extern IntPtr xcb_get_keyboard_mapping_reply(IntPtr connection, Cookie cookie, IntPtr error);
        [DllImport(Xcb)] internal static extern IntPtr xcb_get_keyboard_mapping_keysyms(IntPtr reply);
        [DllImport(Xcb)] internal static extern int xcb_get_keyboard_mapping_keysyms_length(IntPtr reply);
        [DllImport(Xcb)] internal static extern Cookie xcb_get_modifier_mapping(IntPtr connection);
        [DllImport(Xcb)] internal static extern IntPtr xcb_get_modifier_mapping_reply(IntPtr connection, Cookie cookie, IntPtr error);
        [DllImport(Xcb)] internal static extern IntPtr xcb_get_modifier_mapping_keycodes(IntPtr reply);
        [DllImport(Xcb)] internal static extern Cookie xcb_grab_key_checked(IntPtr connection, byte ownerEvents, uint window, ushort modifiers, byte key, byte pointerMode, byte keyboardMode);
        [DllImport(Xcb)] internal static extern Cookie xcb_ungrab_key(IntPtr connection, byte key, uint window, ushort modifiers);
        [DllImport(Xcb)] internal static extern IntPtr xcb_request_check(IntPtr connection, Cookie cookie);
        [DllImport(Xcb)] internal static extern IntPtr xcb_poll_for_event(IntPtr connection);
        [DllImport(Xcb)] internal static extern int xcb_flush(IntPtr connection);
        [DllImport("libc.so.6")] internal static extern void free(IntPtr pointer);
    }
}

// X11 auto-repeat may send a release/press pair with the same timestamp.
internal sealed class X11KeyRepeatFilter
{
    private bool _down;
    private bool _releasePending;
    private uint _releaseTime;
    private long _releaseObservedAt;

    public bool Press(uint timestamp)
    {
        if (_releasePending && timestamp != _releaseTime) _down = false;
        _releasePending = false;
        if (_down) return false;
        _down = true;
        return true;
    }

    public void Release(uint timestamp, long now)
    {
        _releasePending = true;
        _releaseTime = timestamp;
        _releaseObservedAt = now;
    }

    public void FlushRelease(long now)
    {
        if (_releasePending && now - _releaseObservedAt >= 60) Reset();
    }

    public void Reset() { _down = false; _releasePending = false; }
}
