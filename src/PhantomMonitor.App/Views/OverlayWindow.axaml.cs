using System.ComponentModel;
using System.Collections.Specialized;
using Avalonia;
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
    private bool _linuxGeometryNeedsPosition;
    private DispatcherTimer? _linuxGeometryTimer;

    public OverlayWindow(MainWindowViewModel viewModel)
    {
        _viewModel = viewModel;
        if (!OperatingSystem.IsWindows())
        {
            _linuxGeometryTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
            _linuxGeometryTimer.Tick += OnLinuxGeometryTimerTick;
        }
        foreach (var block in viewModel.OverlayAppliedBlocks)
        {
            block.PropertyChanged += OnBlockPropertyChanged;
            _windows[block.Key] = CreateBlockWindow(block);
        }
        viewModel.OverlayAppliedBlocks.CollectionChanged += OnBlocksChanged;
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
        ScheduleLinuxGeometryCheck();
    }

    public void Hide()
    {
        _requestedVisible = false;
        StopWindowsTimers();
        _linuxGeometryTimer?.Stop();
        _linuxGeometryNeedsPosition = false;
        foreach (var window in _windows.Values)
            if (window.IsVisible) window.Hide();
    }

    public void Refresh()
    {
        SyncBlockVisibility();
        PositionWindows();
        ScheduleLinuxGeometryCheck();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _closing = true;
        StopWindowsTimers();
        _linuxGeometryTimer?.Stop();
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _viewModel.OverlayAppliedBlocks.CollectionChanged -= OnBlocksChanged;
        foreach (var block in _viewModel.OverlayAppliedBlocks)
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
            else ScheduleLinuxGeometryCheck(repositionAfterMeasure: true);
        };
        window.SizeChanged += (_, _) => ScheduleLinuxGeometryCheck(repositionAfterMeasure: true);
        window.Closed += (_, _) =>
        {
            if (_closing || _disposed || !_windows.TryGetValue(block.Key, out var current)
                || !ReferenceEquals(current, window)) return;
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
            or nameof(OverlayCanvasBlockViewModel.Content)
            or nameof(OverlayCanvasBlockViewModel.X)
            or nameof(OverlayCanvasBlockViewModel.Y)
            or nameof(OverlayCanvasBlockViewModel.CanvasX)
            or nameof(OverlayCanvasBlockViewModel.CanvasY))
            Refresh();
    }

    private void OnBlocksChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            _viewModel.ClearLinuxOverlayGeometry();
            _linuxRequestedPositions.Clear();
            var oldWindows = _windows.Values.ToArray();
            foreach (var window in oldWindows)
            {
                if (window.DataContext is OverlayCanvasBlockViewModel block)
                    block.PropertyChanged -= OnBlockPropertyChanged;
                window.Hide();
            }

            // Clear the map before closing windows so their Closed handlers
            // cannot mistake this rebuild for a user closing the overlay.
            _windows.Clear();
            foreach (var window in oldWindows)
                window.Close();

            foreach (var block in _viewModel.OverlayAppliedBlocks)
            {
                block.PropertyChanged += OnBlockPropertyChanged;
                _windows[block.Key] = CreateBlockWindow(block);
            }

            Refresh();
            return;
        }

        if (e.OldItems is not null)
        {
            foreach (OverlayCanvasBlockViewModel block in e.OldItems)
            {
                _viewModel.RemoveLinuxOverlayGeometry(block.Key);
                _linuxRequestedPositions.Remove(block.Key);
                block.PropertyChanged -= OnBlockPropertyChanged;
                if (!_windows.Remove(block.Key, out var window)) continue;
                window.Hide();
                window.Close();
            }
        }
        if (e.NewItems is not null)
        {
            foreach (OverlayCanvasBlockViewModel block in e.NewItems)
            {
                block.PropertyChanged += OnBlockPropertyChanged;
                _windows[block.Key] = CreateBlockWindow(block);
            }
        }
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

    private void ScheduleLinuxGeometryCheck(bool repositionAfterMeasure = false)
    {
        if (OperatingSystem.IsWindows() || _disposed || _linuxGeometryTimer is null)
            return;

        _linuxGeometryNeedsPosition |= repositionAfterMeasure;
        if (!_linuxGeometryTimer.IsEnabled) _linuxGeometryTimer.Start();
    }

    private void OnLinuxGeometryTimerTick(object? sender, EventArgs e)
    {
        _linuxGeometryTimer?.Stop();
        if (_disposed || !_requestedVisible) return;

        if (_linuxGeometryNeedsPosition)
        {
            _linuxGeometryNeedsPosition = false;
            PositionWindows();
            ScheduleLinuxGeometryCheck();
            return;
        }

        foreach (var (key, window) in _windows)
        {
            if (!window.IsVisible || window.DataContext is not OverlayCanvasBlockViewModel block) continue;

            var requested = GetLinuxRequestedPosition(key) ?? window.Position;
            _viewModel.RecordLinuxOverlayGeometry(key, block.Title, requested, window.Position,
                window.Bounds.Width > 0 ? window.Bounds.Width : window.Width,
                window.Bounds.Height > 0 ? window.Bounds.Height : window.Height,
                window.RenderScaling);
        }
    }

}
