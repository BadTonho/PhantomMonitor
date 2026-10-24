using Avalonia.Controls;
using PhantomMonitor.App.ViewModels;

namespace PhantomMonitor.App.Views;

/// <summary>Floating view of the hardware tree maintained by the inventory view model.</summary>
public partial class HardwareInventoryWindow : Window
{
    public HardwareInventoryWindow() => InitializeComponent();

    public HardwareInventoryWindow(MainWindowViewModel viewModel) : this()
    {
        DataContext = viewModel;
    }
}
