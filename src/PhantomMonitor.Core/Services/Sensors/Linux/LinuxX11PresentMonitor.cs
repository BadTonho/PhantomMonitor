using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace PhantomMonitor.Services.Linux;

/// <summary>Observes completed X11/XWayland presentations without injecting into the game.</summary>
internal sealed class LinuxX11PresentMonitor : IDisposable
{
    private const byte GenericEvent = 35;
    private const ushort CompleteNotify = 1;
    private const byte PixmapComplete = 0;
    private const byte SkipMode = 2;
    private const uint CompleteNotifyMask = 2;
    private const uint WindowPropertyType = 33; // XCB_ATOM_WINDOW
    private const uint CardinalPropertyType = 6; // XCB_ATOM_CARDINAL
    private const string ClientListStackingAtomName = "_NET_CLIENT_LIST_STACKING";

    private readonly object _gate = new();
    private Thread? _thread;
    private Action<uint, string, float, float>? _frameReceived;
    private volatile bool _stopping;
    private volatile string _status = "X11 Present aguardando janelas X11/XWayland.";

    public string Status => _status;

    public void Start(Action<uint, string, float, float> frameReceived)
    {
        lock (_gate)
        {
            if (_thread is { IsAlive: true }) return;
            _frameReceived = frameReceived;
            _stopping = false;
            _thread = new Thread(Run)
            {
                IsBackground = true,
                Name = "PhantomMonitor.LinuxX11Present"
            };
            _thread.Start();
        }
    }

    public void Stop()
    {
        Thread? thread;
        lock (_gate)
        {
            _stopping = true;
            thread = _thread;
        }
        if (thread is not null && thread != Thread.CurrentThread)
            thread.Join(TimeSpan.FromSeconds(2));
    }

    private void Run()
    {
        IntPtr connection = IntPtr.Zero;
        try
        {
            connection = Native.xcb_connect(null, out var screenNumber);
            if (connection == IntPtr.Zero || Native.xcb_connection_has_error(connection) != 0)
            {
                _status = "X11 Present indisponível: não foi possível conectar ao X11/XWayland.";
                return;
            }

            var setup = Native.xcb_get_setup(connection);
            var screens = Native.xcb_setup_roots_iterator(setup);
            for (var index = 0; index < screenNumber && screens.Rem > 0; index++)
                Native.xcb_screen_next(ref screens);
            if (screens.Data == IntPtr.Zero)
            {
                _status = "X11 Present indisponível: a sessão não publicou uma tela X11.";
                return;
            }

            var root = unchecked((uint)Marshal.ReadInt32(screens.Data));
            var activeWindowAtom = InternAtom(connection, "_NET_ACTIVE_WINDOW", onlyIfExists: true);
            var clientListAtom = InternAtom(connection, ClientListStackingAtomName, onlyIfExists: true);
            var windowAtom = InternAtom(connection, "WINDOW", onlyIfExists: true);
            var processIdAtom = InternAtom(connection, "_NET_WM_PID", onlyIfExists: true);
            if (activeWindowAtom == 0 || windowAtom == 0 || processIdAtom == 0)
            {
                _status = "X11 Present indisponível: o gerenciador de janelas não publicou as propriedades EWMH.";
                return;
            }

            var version = Native.xcb_present_query_version(connection, 1, 2);
            var versionReply = Native.xcb_present_query_version_reply(connection, version, out var versionError);
            if (versionError != IntPtr.Zero) Native.free(versionError);
            if (versionReply == IntPtr.Zero)
            {
                _status = "X11 Present indisponível: o servidor gráfico não oferece a extensão Present.";
                return;
            }
            Native.free(versionReply);

            var windows = new Dictionary<uint, WindowFrames>();
            var windowsByEvent = new Dictionary<uint, WindowFrames>();
            uint activeWindow = 0;
            var nextWindowCheck = Stopwatch.GetTimestamp();
            _status = "X11 Present ativo; procurando janelas de jogos em X11/XWayland.";

            while (!_stopping && Native.xcb_connection_has_error(connection) == 0)
            {
                while (!_stopping)
                {
                    var nativeEvent = Native.xcb_poll_for_event(connection);
                    if (nativeEvent == IntPtr.Zero) break;
                    try
                    {
                        var responseType = (byte)(Marshal.ReadByte(nativeEvent) & 0x7f);
                        if (responseType != GenericEvent
                            || Marshal.ReadInt16(nativeEvent, 8) != CompleteNotify)
                            continue;

                        var eventId = unchecked((uint)Marshal.ReadInt32(nativeEvent, 12));
                        var windowId = unchecked((uint)Marshal.ReadInt32(nativeEvent, 16));
                        if (!windowsByEvent.TryGetValue(eventId, out var window) || window.Window != windowId)
                            continue;

                        var kind = Marshal.ReadByte(nativeEvent, 10);
                        var mode = Marshal.ReadByte(nativeEvent, 11);
                        var ust = unchecked((ulong)Marshal.ReadInt64(nativeEvent, 24));
                        if (kind != PixmapComplete || mode == SkipMode || ust == 0)
                            continue;

                        window.LastFrameTimestamp = Stopwatch.GetTimestamp();
                        window.RecentFrames.Enqueue(window.LastFrameTimestamp);
                        while (window.RecentFrames.Count > 0
                            && window.LastFrameTimestamp - window.RecentFrames.Peek() > Stopwatch.Frequency * 2)
                            window.RecentFrames.Dequeue();

                        if (window.LastUst != 0 && ust > window.LastUst)
                        {
                            var frametimeMs = (ust - window.LastUst) / 1000f;
                            if (frametimeMs is >= 0.01f and <= 2000f)
                            {
                                window.FrametimeMs = frametimeMs;
                                window.Fps = 1000f / frametimeMs;
                            }
                        }
                        window.LastUst = ust;
                    }
                    finally
                    {
                        Native.free(nativeEvent);
                    }
                }

                var now = Stopwatch.GetTimestamp();
                if (now >= nextWindowCheck)
                {
                    nextWindowCheck = now + Stopwatch.Frequency / 4;
                    activeWindow = ReadWindowProperty(connection, root, activeWindowAtom, WindowPropertyType);
                    var clientWindows = clientListAtom == 0
                        ? activeWindow == 0 ? [] : new[] { activeWindow }
                        : ReadWindowPropertyList(connection, root, clientListAtom, windowAtom);

                    var liveWindows = new HashSet<uint>();
                    foreach (var windowId in clientWindows)
                    {
                        if (windowId == 0 || liveWindows.Contains(windowId)) continue;
                        var pid = ReadWindowProperty(connection, windowId, processIdAtom, CardinalPropertyType);
                        // Alguns clientes (glxgears, SDL/Wine antigos) não publicam _NET_WM_PID:
                        // pede o PID ao próprio servidor pela extensão XRes.
                        if (pid == 0) pid = QueryClientPid(connection, windowId);
                        if (pid == 0 || pid == Environment.ProcessId) continue;
                        liveWindows.Add(windowId);

                        if (windows.ContainsKey(windowId)) continue;
                        var eventId = Native.xcb_generate_id(connection);
                        if (eventId == 0) continue;
                        var cookie = Native.xcb_present_select_input_checked(
                            connection, eventId, windowId, CompleteNotifyMask);
                        var error = Native.xcb_request_check(connection, cookie);
                        if (error != IntPtr.Zero)
                        {
                            var errorCode = Marshal.ReadByte(error, 1);
                            Native.free(error);
                            continue;
                        }

                        var observed = new WindowFrames(windowId, eventId, pid, ReadProcessName(pid));
                        windows.Add(windowId, observed);
                        windowsByEvent.Add(eventId, observed);
                    }

                    foreach (var removedWindow in windows.Keys.Where(id => !liveWindows.Contains(id)).ToArray())
                    {
                        var removed = windows[removedWindow];
                        Native.xcb_present_select_input(connection, removed.EventId, removed.Window, 0);
                        windows.Remove(removedWindow);
                        windowsByEvent.Remove(removed.EventId);
                    }
                    Native.xcb_flush(connection);

                    var target = SelectTarget(windows.Values, activeWindow);
                    if (target is null)
                    {
                        var waitingWindow = windows.Values.FirstOrDefault(window => window.Window == activeWindow)
                            ?? windows.Values.FirstOrDefault();
                        _status = waitingWindow is not null
                            ? $"X11 Present conectado a {windows.Count} janela(s); aguardando quadros de {waitingWindow.Name} (PID {waitingWindow.Pid}). Janelas Wayland nativas não publicam estes eventos."
                            : clientListAtom == 0
                                ? "X11 Present aguardando uma janela ativa com PID publicado."
                                : "X11 Present não encontrou janelas X11/XWayland de outros processos; janelas Wayland nativas não publicam estes eventos.";
                    }
                    else if (target.LastFrameTimestamp == 0)
                    {
                        _status = $"X11 Present observando {windows.Count} janela(s); aguardando quadros de {target.Name} (PID {target.Pid}).";
                    }
                    else
                    {
                        _status = $"X11 Present observando {target.Name} (PID {target.Pid}); {target.RecentFrames.Count} quadros nos últimos 2 s.";
                    }

                    if (target is not null && target.Fps > 0
                        && target.LastUst != target.LastEmittedUst)
                    {
                        target.LastEmittedUst = target.LastUst;
                        _frameReceived?.Invoke(target.Pid, target.Name, target.FrametimeMs, target.Fps);
                    }
                }

                if (!_stopping) Thread.Sleep(4);
            }

            if (!_stopping)
                _status = "X11 Present desconectado da sessão gráfica.";
        }
        catch (DllNotFoundException)
        {
            _status = "X11 Present indisponível: instale a biblioteca libxcb-present do sistema.";
        }
        catch (EntryPointNotFoundException)
        {
            _status = "X11 Present indisponível: a biblioteca libxcb-present não oferece a API necessária.";
        }
        catch (Exception ex)
        {
            _status = $"X11 Present indisponível: {ex.Message}";
        }
        finally
        {
            if (connection != IntPtr.Zero) Native.xcb_disconnect(connection);
        }
    }

    private static WindowFrames? SelectTarget(IEnumerable<WindowFrames> windows, uint activeWindow)
    {
        var candidates = windows
            .Where(window => window.LastFrameTimestamp != 0
                && Stopwatch.GetTimestamp() - window.LastFrameTimestamp <= Stopwatch.Frequency * 2)
            .ToArray();
        var active = candidates.FirstOrDefault(window => window.Window == activeWindow);
        if (active is not null) return active;
        return candidates
            .OrderByDescending(window => window.RecentFrames.Count)
            .ThenByDescending(window => window.LastFrameTimestamp)
            .FirstOrDefault();
    }

    private static uint InternAtom(IntPtr connection, string name, bool onlyIfExists)
    {
        var bytes = Encoding.UTF8.GetBytes(name);
        var cookie = Native.xcb_intern_atom(connection, onlyIfExists ? (byte)1 : (byte)0,
            checked((ushort)bytes.Length), bytes);
        var reply = Native.xcb_intern_atom_reply(connection, cookie, out var error);
        if (error != IntPtr.Zero) Native.free(error);
        if (reply == IntPtr.Zero) return 0;
        try { return unchecked((uint)Marshal.ReadInt32(reply, 8)); }
        finally { Native.free(reply); }
    }

    private static uint ReadWindowProperty(IntPtr connection, uint window, uint property, uint type)
    {
        var values = ReadPropertyValues(connection, window, property, type, 1);
        return values.Length == 0 ? 0 : values[0];
    }

    private static uint[] ReadWindowPropertyList(IntPtr connection, uint window, uint property, uint type) =>
        ReadPropertyValues(connection, window, property, type, 1024);

    private static uint[] ReadPropertyValues(IntPtr connection, uint window, uint property, uint type, uint maxLength)
    {
        if (window == 0 || property == 0 || type == 0) return [];
        var cookie = Native.xcb_get_property(connection, 0, window, property, type, 0, maxLength);
        var reply = Native.xcb_get_property_reply(connection, cookie, out var error);
        if (error != IntPtr.Zero) Native.free(error);
        if (reply == IntPtr.Zero) return [];
        try
        {
            var format = Marshal.ReadByte(reply, 1);
            var length = Native.xcb_get_property_value_length(reply);
            if (format != 32 || length < sizeof(uint)) return [];
            var count = Math.Min(length / sizeof(uint), checked((int)maxLength));
            var values = new uint[count];
            var value = Native.xcb_get_property_value(reply);
            for (var index = 0; index < count; index++)
                values[index] = unchecked((uint)Marshal.ReadInt32(value, index * sizeof(uint)));
            return values;
        }
        finally { Native.free(reply); }
    }

    private const uint ResClientIdMaskLocalClientPid = 2; // XCB_RES_CLIENT_ID_MASK_LOCAL_CLIENT_PID

    /// <summary>PID do cliente X dono da janela via XRes (ClientIds); 0 se indisponível.</summary>
    private static uint QueryClientPid(IntPtr connection, uint window)
    {
        try
        {
            var spec = new ResClientIdSpec { Client = window, Mask = ResClientIdMaskLocalClientPid };
            var cookie = Native.xcb_res_query_client_ids(connection, 1, ref spec);
            var reply = Native.xcb_res_query_client_ids_reply(connection, cookie, out var error);
            if (error != IntPtr.Zero) Native.free(error);
            if (reply == IntPtr.Zero) return 0;
            try
            {
                // Layout da resposta: cabeçalho de 32 bytes (num_ids em +8) seguido, por id, de
                // spec{client,mask}(8) + length(4, em bytes) + valores uint32.
                var numIds = Marshal.ReadInt32(reply, 8);
                var offset = 32;
                for (var index = 0; index < numIds && index < 4; index++)
                {
                    var mask = unchecked((uint)Marshal.ReadInt32(reply, offset + 4));
                    var length = Marshal.ReadInt32(reply, offset + 8);
                    if (length < 0 || length > 64) return 0;
                    if (mask == ResClientIdMaskLocalClientPid && length >= sizeof(uint))
                        return unchecked((uint)Marshal.ReadInt32(reply, offset + 12));
                    offset += 12 + length;
                }
                return 0;
            }
            finally { Native.free(reply); }
        }
        catch (DllNotFoundException) { return 0; }
        catch (EntryPointNotFoundException) { return 0; }
    }

    [StructLayout(LayoutKind.Sequential)] private struct ResClientIdSpec { public uint Client; public uint Mask; }

    private static string ReadProcessName(uint pid)
    {
        if (pid == 0) return "X11";
        try
        {
            var name = File.ReadAllText($"/proc/{pid}/comm").Trim();
            return name.Length == 0 ? $"PID {pid}" : name;
        }
        catch (IOException) { return $"PID {pid}"; }
        catch (UnauthorizedAccessException) { return $"PID {pid}"; }
    }

    public void Dispose() => Stop();

    private sealed class WindowFrames(uint window, uint eventId, uint pid, string name)
    {
        public uint Window { get; } = window;
        public uint EventId { get; } = eventId;
        public uint Pid { get; } = pid;
        public string Name { get; } = name;
        public ulong LastUst { get; set; }
        public ulong LastEmittedUst { get; set; }
        public long LastFrameTimestamp { get; set; }
        public float FrametimeMs { get; set; }
        public float Fps { get; set; }
        public Queue<long> RecentFrames { get; } = new();
    }

    private static class Native
    {
        private const string Xcb = "libxcb.so.1";
        private const string XcbPresent = "libxcb-present.so.0";
        private const string XcbRes = "libxcb-res.so.0";

        [StructLayout(LayoutKind.Sequential)] internal struct Iterator
        {
            public IntPtr Data;
            public int Rem;
            public int Index;
        }

        [StructLayout(LayoutKind.Sequential)] internal struct Cookie { public uint Sequence; }

        [DllImport(Xcb, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr xcb_connect(string? display, out int screen);
        [DllImport(Xcb, CallingConvention = CallingConvention.Cdecl)] internal static extern int xcb_connection_has_error(IntPtr connection);
        [DllImport(Xcb, CallingConvention = CallingConvention.Cdecl)] internal static extern void xcb_disconnect(IntPtr connection);
        [DllImport(Xcb, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr xcb_get_setup(IntPtr connection);
        [DllImport(Xcb, CallingConvention = CallingConvention.Cdecl)] internal static extern Iterator xcb_setup_roots_iterator(IntPtr setup);
        [DllImport(Xcb, CallingConvention = CallingConvention.Cdecl)] internal static extern void xcb_screen_next(ref Iterator iterator);
        [DllImport(Xcb, CallingConvention = CallingConvention.Cdecl)] internal static extern uint xcb_generate_id(IntPtr connection);
        [DllImport(Xcb, CallingConvention = CallingConvention.Cdecl)] internal static extern Cookie xcb_intern_atom(IntPtr connection, byte onlyIfExists, ushort nameLength, byte[] name);
        [DllImport(Xcb, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr xcb_intern_atom_reply(IntPtr connection, Cookie cookie, out IntPtr error);
        [DllImport(Xcb, CallingConvention = CallingConvention.Cdecl)] internal static extern Cookie xcb_get_property(IntPtr connection, byte delete, uint window, uint property, uint type, uint longOffset, uint longLength);
        [DllImport(Xcb, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr xcb_get_property_reply(IntPtr connection, Cookie cookie, out IntPtr error);
        [DllImport(Xcb, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr xcb_get_property_value(IntPtr reply);
        [DllImport(Xcb, CallingConvention = CallingConvention.Cdecl)] internal static extern int xcb_get_property_value_length(IntPtr reply);
        [DllImport(Xcb, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr xcb_poll_for_event(IntPtr connection);
        [DllImport(Xcb, CallingConvention = CallingConvention.Cdecl)] internal static extern int xcb_flush(IntPtr connection);
        [DllImport(XcbPresent, CallingConvention = CallingConvention.Cdecl)] internal static extern Cookie xcb_present_query_version(IntPtr connection, uint major, uint minor);
        [DllImport(XcbPresent, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr xcb_present_query_version_reply(IntPtr connection, Cookie cookie, out IntPtr error);
        [DllImport(XcbPresent, CallingConvention = CallingConvention.Cdecl)] internal static extern Cookie xcb_present_select_input(IntPtr connection, uint eventId, uint window, uint eventMask);
        [DllImport(XcbPresent, CallingConvention = CallingConvention.Cdecl)] internal static extern Cookie xcb_present_select_input_checked(IntPtr connection, uint eventId, uint window, uint eventMask);
        [DllImport(XcbRes, CallingConvention = CallingConvention.Cdecl)] internal static extern Cookie xcb_res_query_client_ids(IntPtr connection, uint numSpecs, ref ResClientIdSpec specs);
        [DllImport(XcbRes, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr xcb_res_query_client_ids_reply(IntPtr connection, Cookie cookie, out IntPtr error);
        [DllImport(Xcb, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr xcb_request_check(IntPtr connection, Cookie cookie);
        [DllImport("libc.so.6", CallingConvention = CallingConvention.Cdecl)] internal static extern void free(IntPtr pointer);
    }
}
