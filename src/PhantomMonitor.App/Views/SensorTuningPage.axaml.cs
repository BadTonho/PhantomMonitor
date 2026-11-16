using Avalonia.Controls;
using Avalonia.Interactivity;
using PhantomMonitor.App.ViewModels;

namespace PhantomMonitor.App.Views;

public partial class SensorTuningPage : UserControl
{
    private MainWindowViewModel ViewModel => (MainWindowViewModel)DataContext!;

    public SensorTuningPage() => InitializeComponent();

    private void OnStartClick(object? sender, RoutedEventArgs e) => ViewModel.Start();
    private void OnPauseClick(object? sender, RoutedEventArgs e) => ViewModel.TogglePause();
    private void OnStopClick(object? sender, RoutedEventArgs e) => ViewModel.Stop();
    private void OnAddSensorTuningClick(object? sender, RoutedEventArgs e) => ViewModel.AddSensorTuning();
    private void OnSaveSensorTuningsClick(object? sender, RoutedEventArgs e) => ViewModel.SaveSensorTunings();

    private void OnRemoveSensorTuningClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: SensorTuningEditorViewModel editor })
            ViewModel.RemoveSensorTuning(editor);
    }

    private void OnMoveSensorTuningUpClick(object? sender, RoutedEventArgs e) => MoveSensorTuning(sender, -1);
    private void OnMoveSensorTuningDownClick(object? sender, RoutedEventArgs e) => MoveSensorTuning(sender, 1);

    private void MoveSensorTuning(object? sender, int offset)
    {
        if (sender is Control { DataContext: SensorTuningEditorViewModel editor })
            ViewModel.MoveSensorTuning(editor, offset);
    }
}
