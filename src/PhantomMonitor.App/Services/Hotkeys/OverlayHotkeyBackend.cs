using PhantomMonitor.Services;
using System.Runtime.Versioning;

namespace PhantomMonitor.App.Services.Hotkeys;

internal readonly record struct HotkeyResult(bool Success, string Message, string Display)
{
    public static HotkeyResult Failed(string message) => new(false, message, "");
    public static HotkeyResult Active(string display) => new(true, "", display);
}

internal interface IOverlayHotkeyBackend : IDisposable
{
    Task<HotkeyResult> ApplyAsync(string shortcut);
}

[SupportedOSPlatform("windows")]
internal sealed class WindowsOverlayHotkeyBackend(GlobalHotkeyService service, Action activated) : IOverlayHotkeyBackend
{
    // Two IDs allow a new shortcut to be reserved before releasing the old one.
    private const int FirstId = GlobalHotkeyService.DefaultHotkeyId;
    private const int SecondId = 0x504A;
    private int _activeId;
    private string _active = "";

    public Task<HotkeyResult> ApplyAsync(string shortcut)
    {
        if (string.Equals(_active, shortcut, StringComparison.OrdinalIgnoreCase))
            return Task.FromResult(HotkeyResult.Active(shortcut));
        if (shortcut.Length == 0)
        {
            if (_activeId != 0 && !service.Unregister(_activeId))
                return Task.FromResult(HotkeyResult.Failed("Não foi possível remover o atalho anterior."));
            _activeId = 0;
            _active = "";
            return Task.FromResult(HotkeyResult.Active(""));
        }

        var next = _activeId == FirstId ? SecondId : FirstId;
        if (!service.Register(next, shortcut, activated))
            return Task.FromResult(HotkeyResult.Failed("Não foi possível registrar o atalho. Ele pode estar em uso por outro aplicativo."));
        if (_activeId != 0 && !service.Unregister(_activeId))
        {
            service.Unregister(next);
            return Task.FromResult(HotkeyResult.Failed("Não foi possível remover o atalho anterior."));
        }
        _activeId = next;
        _active = shortcut;
        return Task.FromResult(HotkeyResult.Active(shortcut));
    }

    public void Dispose()
    {
        if (_activeId != 0) service.Unregister(_activeId);
        _activeId = 0;
    }
}
