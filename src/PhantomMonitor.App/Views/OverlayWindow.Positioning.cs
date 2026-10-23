namespace PhantomMonitor.App.Views;

public sealed partial class OverlayWindow
{
    private void PositionWindows()
    {
        if (_disposed || !_requestedVisible) return;

        if (OperatingSystem.IsWindows())
        {
            PositionWindowsOnWindows();
            return;
        }

        PositionWindowsOnPrimaryScreen();
    }
}
