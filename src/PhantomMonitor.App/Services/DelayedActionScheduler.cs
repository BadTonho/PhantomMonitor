using Avalonia.Threading;

namespace PhantomMonitor.App.Services;

/// <summary>
/// Agenda uma única ação atrasada na UI thread (toasts, limpeza de status, confirmações).
/// Cada <see cref="Schedule"/> substitui o agendamento anterior; suporta cancelamento e Dispose.
/// </summary>
internal sealed class DelayedActionScheduler : IDisposable
{
    private readonly TimeSpan _delay;
    private DispatcherTimer? _timer;
    private Action? _action;
    private bool _disposed;

    public DelayedActionScheduler(TimeSpan delay) => _delay = delay;

    public bool IsPending => _timer is { IsEnabled: true };

    /// <summary>Agenda <paramref name="action"/>; reinicia a contagem se já havia uma pendente.</summary>
    public void Schedule(Action action)
    {
        if (_disposed) return;
        Cancel();
        _action = action;
        var timer = new DispatcherTimer { Interval = _delay };
        timer.Tick += OnTick;
        _timer = timer;
        timer.Start();
    }

    /// <summary>Cancela a ação pendente, se houver.</summary>
    public void Cancel()
    {
        if (_timer is { } timer)
        {
            timer.Stop();
            timer.Tick -= OnTick;
        }
        _timer = null;
        _action = null;
    }

    private void OnTick(object? sender, EventArgs e)
    {
        var action = _action;
        Cancel();
        action?.Invoke();
    }

    public void Dispose()
    {
        _disposed = true;
        Cancel();
    }
}
