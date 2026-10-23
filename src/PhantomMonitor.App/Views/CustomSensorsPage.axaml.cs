using Avalonia.Controls;
using Avalonia.Interactivity;
using PhantomMonitor.App.ViewModels;

namespace PhantomMonitor.App.Views;

public partial class CustomSensorsPage : UserControl
{
    private MainWindowViewModel ViewModel => (MainWindowViewModel)DataContext!;

    public CustomSensorsPage() => InitializeComponent();

    private void OnStartClick(object? sender, RoutedEventArgs e) => ViewModel.Start();
    private void OnPauseClick(object? sender, RoutedEventArgs e) => ViewModel.TogglePause();
    private void OnStopClick(object? sender, RoutedEventArgs e) => ViewModel.Stop();
    private void OnAddCustomSensorClick(object? sender, RoutedEventArgs e) => ViewModel.AddCustomSensor();
    private void OnSaveCustomSensorsClick(object? sender, RoutedEventArgs e) => ViewModel.SaveCustomSensors();
    private void OnRemoveCustomSensorClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: CustomSensorEditorViewModel editor })
            ViewModel.RemoveCustomSensor(editor);
    }
    private void OnMoveCustomSensorUpClick(object? sender, RoutedEventArgs e) => MoveCustomSensor(sender, -1);
    private void OnMoveCustomSensorDownClick(object? sender, RoutedEventArgs e) => MoveCustomSensor(sender, 1);
    private void MoveCustomSensor(object? sender, int offset)
    {
        if (sender is Control { DataContext: CustomSensorEditorViewModel editor })
            ViewModel.MoveCustomSensor(editor, offset);
    }
}
