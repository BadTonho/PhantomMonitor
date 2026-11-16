using Avalonia.Controls;
using Avalonia.Interactivity;
using PhantomMonitor.App.ViewModels;

namespace PhantomMonitor.App.Views;

public partial class OverviewPage : UserControl
{
    private MainWindowViewModel ViewModel => (MainWindowViewModel)DataContext!;
    private bool _overviewCompact;

    public OverviewPage()
    {
        InitializeComponent();
        OverviewSecondaryCards.SizeChanged += (_, _) => ArrangeOverviewCards();
    }

    private void ArrangeOverviewCards()
    {
        var grid = OverviewSecondaryCards;
        var compact = grid.Bounds.Width < 760;
        if (compact == _overviewCompact) return;
        _overviewCompact = compact;
        grid.ColumnDefinitions = ColumnDefinitions.Parse(compact ? "*,*" : "*,*,*,*");
        grid.RowDefinitions = RowDefinitions.Parse(compact ? "Auto,Auto" : "Auto");
        for (var i = 0; i < grid.Children.Count; i++)
        {
            Grid.SetColumn(grid.Children[i], compact ? i % 2 : i);
            Grid.SetRow(grid.Children[i], compact ? i / 2 : 0);
        }
    }

    private void OnToggleOverlayClick(object? sender, RoutedEventArgs e) => ViewModel.ToggleOverlay();
    private void OnStartClick(object? sender, RoutedEventArgs e) => ViewModel.Start();
    private void OnPauseClick(object? sender, RoutedEventArgs e) => ViewModel.TogglePause();
    private void OnStopClick(object? sender, RoutedEventArgs e) => ViewModel.Stop();
    private void OnRestartAsAdminClick(object? sender, RoutedEventArgs e) => ViewModel.RestartAsAdmin();
    private void OnDismissAttentionClick(object? sender, RoutedEventArgs e) => ViewModel.DismissAttentionMessage();
}
