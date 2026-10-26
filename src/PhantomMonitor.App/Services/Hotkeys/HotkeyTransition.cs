namespace PhantomMonitor.App.Services.Hotkeys;

/// <summary>Tracks the reversible runtime half of saving a shortcut configuration.</summary>
internal sealed class HotkeyTransition(string previous, Func<string, Task<HotkeyResult>> apply)
{
    private bool _staged;

    public async Task<HotkeyResult> StageAsync(string candidate)
    {
        var result = await apply(candidate);
        _staged = result.Success;
        return result;
    }

    public Task<HotkeyResult> RollbackAsync() => _staged
        ? apply(previous)
        : Task.FromResult(HotkeyResult.Active(previous));
}
