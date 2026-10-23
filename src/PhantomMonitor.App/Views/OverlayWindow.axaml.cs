using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Threading;
using PhantomMonitor.App.ViewModels;

namespace PhantomMonitor.App.Views;

/// <summary>Coordena as janelas independentes dos blocos, evitando uma janela
/// transparente de tela inteira capturar cliques fora das métricas.</summary>
public sealed partial class OverlayWindow : IDisposable
{
    private readonly MainWindowViewModel _viewModel;
    private readonly Dictionary<string, OverlayBlockWindow> _windows = new(StringComparer.OrdinalIgnoreCase);
    private bool _requestedVisible;
    private bool _closing;
    private bool _disposed;

    public OverlayWindow(MainWindowViewModel viewModel)
    {
        _viewModel = viewModel;
        foreach (var block in viewModel.OverlayBlocks)
        {
            block.PropertyChanged += OnBlockPropertyChanged;
            _windows[block.Key] = CreateBlockWindow(block);
        }
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    public event Action? UserClosed;
    public bool IsVisible => _windows.Values.Any(window => window.IsVisible);

    public double Opacity
    {
        set
        {
            foreach (var window in _windows.Values)
                window.Opacity = value;
        }
    }

    public void Show()
    {
        if (_disposed) return;
        _requestedVisible = true;
        SyncBlockVisibility();
        if (OperatingSystem.IsWindows()) StartWindowsTimers();
        PositionWindows();
    }

    public void Hide()
    {
        _requestedVisible = false;
        StopWindowsTimers();
        foreach (var window in _windows.Values)
            if (window.IsVisible) window.Hide();
    }

    public void Refresh()
    {
        SyncBlockVisibility();
        PositionWindows();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _closing = true;
        StopWindowsTimers();
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        foreach (var block in _viewModel.OverlayBlocks)
            block.PropertyChanged -= OnBlockPropertyChanged;
        foreach (var window in _windows.Values)
            window.Close();
        _windows.Clear();
    }

    public void Close() => Dispose();

    private OverlayBlockWindow CreateBlockWindow(OverlayCanvasBlockViewModel block)
    {
        var window = new OverlayBlockWindow
        {
            DataContext = block,
            Opacity = _viewModel.OverlayOpacity
        };
        window.Opened += (_, _) =>
        {
            if (OperatingSystem.IsWindows()) MakeClickThrough(window);
        };
        window.Closed += (_, _) =>
        {
            if (_closing || _disposed) return;
            _closing = true;
            foreach (var other in _windows.Values)
                if (!ReferenceEquals(other, window)) other.Close();
            _windows.Clear();
            _requestedVisible = false;
            Dispose();
            UserClosed?.Invoke();
        };
        return window;
    }

    private void OnBlockPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(OverlayCanvasBlockViewModel.HasContent)
            or nameof(OverlayCanvasBlockViewModel.IsRenderable)
            or nameof(OverlayCanvasBlockViewModel.X)
            or nameof(OverlayCanvasBlockViewModel.Y)
            or nameof(OverlayCanvasBlockViewModel.CanvasX)
            or nameof(OverlayCanvasBlockViewModel.CanvasY))
            Refresh();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainWindowViewModel.OverlayOpacity)
            or nameof(MainWindowViewModel.OverlayWidth)
            or nameof(MainWindowViewModel.OverlayCanvasWidth)
            or nameof(MainWindowViewModel.OverlayCanvasHeight)
            or nameof(MainWindowViewModel.FollowOffsetX)
            or nameof(MainWindowViewModel.FollowOffsetY))
        {
            Opacity = _viewModel.OverlayOpacity;
            Refresh();
        }
        else if (e.PropertyName is nameof(MainWindowViewModel.OverlayPanelVisible))
        {
            Refresh();
        }
    }

    private void SyncBlockVisibility()
    {
        foreach (var (key, window) in _windows)
        {
            var block = (OverlayCanvasBlockViewModel)window.DataContext!;
            if (_requestedVisible && block.IsRenderable)
            {
                if (!window.IsVisible) window.Show();
            }
            else if (window.IsVisible)
            {
                window.Hide();
            }
        }
    }

}
