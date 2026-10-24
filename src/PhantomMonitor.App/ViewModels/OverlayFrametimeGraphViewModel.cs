namespace PhantomMonitor.App.ViewModels;

public sealed class OverlayFrametimeGraphViewModel(MainWindowViewModel owner, OverlayCanvasBlockViewModel block)
{
    public MainWindowViewModel Owner { get; } = owner;
    public OverlayCanvasBlockViewModel Block { get; } = block;
}
