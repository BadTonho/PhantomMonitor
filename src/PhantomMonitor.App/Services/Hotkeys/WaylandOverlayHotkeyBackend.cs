using PhantomMonitor.App.Linux.DBus;
using PhantomMonitor.Services;
using Tmds.DBus.Protocol;

namespace PhantomMonitor.App.Services.Hotkeys;

/// <summary>Global shortcuts on Wayland are mediated by the user's desktop portal.</summary>
internal sealed class WaylandOverlayHotkeyBackend(Action activated, Action<string> unavailable) : IOverlayHotkeyBackend
{
    private const string Service = "org.freedesktop.portal.Desktop";
    private const string DesktopPath = "/org/freedesktop/portal/desktop";
    private const string ShortcutId = "toggle-overlay";
    private readonly SemaphoreSlim _gate = new(1, 1);
    private DBusConnection? _connection;
    private GlobalShortcuts? _portal;
    private IDisposable? _activationWatch;
    private IDisposable? _closedWatch;
    private ObjectPath? _session;
    private string _active = "";
    private string _display = "";
    private bool _disposed;

    public async Task<HotkeyResult> ApplyAsync(string shortcut)
    {
        await _gate.WaitAsync();
        try
        {
            if (_disposed) return HotkeyResult.Failed("O serviço de atalhos foi encerrado.");
            if (string.Equals(shortcut, _active, StringComparison.OrdinalIgnoreCase)) return HotkeyResult.Active(_display);
            if (shortcut.Length == 0)
            {
                await CloseCurrentAsync();
                return HotkeyResult.Active("");
            }
            if (!HotkeyParser.TryParse(shortcut, out var modifiers, out var key)) return HotkeyResult.Failed("Combinação inválida.");
            await EnsureConnectedAsync();
            var sessionToken = "pm" + Guid.NewGuid().ToString("N");
            var sessionResult = await RequestAsync(token => _portal!.CreateSessionAsync(new()
            {
                ["handle_token"] = token,
                ["session_handle_token"] = sessionToken
            }));
            if (sessionResult.Response != 0 || !sessionResult.Results.TryGetValue("session_handle", out var sessionValue))
                return HotkeyResult.Failed("O portal não criou uma sessão de atalhos globais.");
            var candidate = new ObjectPath(sessionValue.GetString());
            try
            {
                var preferred = ToPortalTrigger(modifiers, key);
                var bindResult = await RequestAsync(token => _portal!.BindShortcutsAsync(candidate,
                    [(ShortcutId, new Dictionary<string, VariantValue> { ["description"] = "Mostrar ou ocultar o overlay", ["preferred_trigger"] = preferred })],
                    "", new() { ["handle_token"] = token }));
                var decoded = ReadBinding(bindResult.Response, bindResult.Results);
                if (!decoded.Success)
                {
                    await CloseSessionAsync(candidate);
                    return decoded;
                }

                var activationWatch = await _portal!.WatchActivatedAsync(value =>
                {
                    if (_session is { } current && current.ToString() == value.SessionHandle.ToString()
                        && value.ShortcutId == ShortcutId) activated();
                }, emitOnCapturedContext: false);
                var sessionProxy = new Session(_connection!, Service, candidate.ToString());
                var closedWatch = await sessionProxy.WatchClosedAsync(_ =>
                {
                    if (_session is { } current && current.ToString() == candidate.ToString())
                    {
                        _session = null;
                        _active = "";
                        _display = "";
                        unavailable("A sessão de atalhos foi encerrada pelo sistema. Salve novamente para tentar reativá-la.");
                    }
                }, emitOnCapturedContext: false);
                await CloseCurrentAsync();
                _session = candidate;
                _activationWatch = activationWatch;
                _closedWatch = closedWatch;
                _active = shortcut;
                _display = decoded.Display;
                return decoded;
            }
            catch
            {
                await CloseSessionAsync(candidate);
                throw;
            }
        }
        catch (Exception ex)
        {
            ErrorLog.Write("WaylandHotkey", ex);
            return HotkeyResult.Failed("O portal de atalhos globais não está disponível ou recusou a solicitação nesta sessão Wayland.");
        }
        finally { _gate.Release(); }
    }

    internal static string ToPortalTrigger(uint modifiers, uint key)
    {
        var parts = new List<string>();
        if ((modifiers & HotkeyParser.ModControl) != 0) parts.Add("CTRL");
        if ((modifiers & HotkeyParser.ModAlt) != 0) parts.Add("ALT");
        if ((modifiers & HotkeyParser.ModShift) != 0) parts.Add("SHIFT");
        if ((modifiers & HotkeyParser.ModWin) != 0) parts.Add("LOGO");
        parts.Add(key is >= 0x70 and <= 0x87 ? $"F{key - 0x70 + 1}" : ((char)key).ToString().ToLowerInvariant());
        return string.Join('+', parts);
    }

    internal static HotkeyResult ReadBinding(uint response, Dictionary<string, VariantValue> results)
    {
        if (response != 0) return SelectBinding(response, []);
        if (!results.TryGetValue("shortcuts", out var shortcuts)) return HotkeyResult.Failed("O portal não confirmou um atalho ativo.");
        try
        {
            var bindings = new List<(string Id, string Description)>();
            for (var i = 0; i < shortcuts.Count; i++)
            {
                var item = shortcuts.GetItem(i);
                var properties = item.GetItem(1).GetDictionary<string, VariantValue>();
                var description = properties.TryGetValue("trigger_description", out var value)
                    ? value.GetString() : "configurado pelo sistema";
                bindings.Add((item.GetItem(0).GetString(), description));
            }
            return SelectBinding(response, bindings);
        }
        catch (Exception) { return HotkeyResult.Failed("O portal retornou uma resposta de atalho inválida."); }
    }

    internal static HotkeyResult SelectBinding(uint response, IReadOnlyList<(string Id, string Description)> bindings)
    {
        if (response != 0) return HotkeyResult.Failed(response == 1
            ? "Configuração do atalho cancelada no portal."
            : "O portal recusou o atalho global.");
        foreach (var binding in bindings)
            if (binding.Id == ShortcutId) return HotkeyResult.Active(binding.Description);
        return HotkeyResult.Failed("Nenhum atalho foi associado pelo sistema.");
    }

    private async Task EnsureConnectedAsync()
    {
        if (_portal is not null) return;
        var address = DBusAddress.Session ?? throw new InvalidOperationException("Sessão D-Bus indisponível.");
        var connection = new DBusConnection(address);
        try
        {
            await connection.ConnectAsync();
            _connection = connection;
            _portal = new GlobalShortcuts(connection, Service, DesktopPath);
        }
        catch { connection.Dispose(); throw; }
    }

    private async Task<(uint Response, Dictionary<string, VariantValue> Results)> RequestAsync(Func<string, Task<ObjectPath>> invoke)
    {
        var token = "pm" + Guid.NewGuid().ToString("N");
        var sender = _connection!.UniqueName!.TrimStart(':').Replace('.', '_');
        var path = $"{DesktopPath}/request/{sender}/{token}";
        var request = new Request(_connection, Service, path);
        var completion = new TaskCompletionSource<(uint, Dictionary<string, VariantValue>)>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var subscription = await request.WatchResponseAsync(value => completion.TrySetResult(value), emitOnCapturedContext: false);
        var actualPath = await invoke(token);
        if (actualPath.ToString() != path) throw new InvalidOperationException("O portal retornou um caminho de solicitação inesperado.");
        return await completion.Task.WaitAsync(TimeSpan.FromMinutes(2));
    }

    private async Task CloseCurrentAsync()
    {
        var current = _session;
        _session = null;
        _activationWatch?.Dispose(); _activationWatch = null;
        _closedWatch?.Dispose(); _closedWatch = null;
        _active = "";
        _display = "";
        if (current is { } path) await CloseSessionAsync(path);
    }

    private async Task CloseSessionAsync(ObjectPath path)
    {
        if (_connection is null) return;
        try { await new Session(_connection, Service, path.ToString()).CloseAsync(); }
        catch (Exception ex) { ErrorLog.Write("WaylandHotkey.Close", ex); }
    }

    public void Dispose()
    {
        _disposed = true;
        _activationWatch?.Dispose();
        _closedWatch?.Dispose();
        // Closing the D-Bus connection releases portal sessions owned by this client.
        _connection?.Dispose();
        _connection = null;
        _portal = null;
    }
}
