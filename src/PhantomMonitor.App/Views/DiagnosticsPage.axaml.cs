using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Interactivity;
using PhantomMonitor.App.ViewModels;
using PhantomMonitor.Services;

namespace PhantomMonitor.App.Views;

public partial class DiagnosticsPage : UserControl
{
    private static readonly string[] SectionCodes = ["summary", "errors", "sensors"];
    private Button?[]? _tabButtons;
    private MainWindowViewModel ViewModel => (MainWindowViewModel)DataContext!;

    public DiagnosticsPage() => InitializeComponent();

    public void SyncTabSelection()
    {
        _tabButtons ??=
        [
            this.FindControl<Button>("DiagnosticSummaryTab"),
            this.FindControl<Button>("DiagnosticErrorsTab"),
            this.FindControl<Button>("DiagnosticSensorsTab"),
        ];

        for (var index = 0; index < SectionCodes.Length; index++)
        {
            if (_tabButtons[index] is not { } button) continue;
            if (SectionCodes[index] == ViewModel.SelectedDiagnosticSection)
            {
                if (!button.Classes.Contains("selected")) button.Classes.Add("selected");
            }
            else
            {
                button.Classes.Remove("selected");
            }
        }
    }

    private void OnDiagnosticSummaryClick(object? sender, RoutedEventArgs e) => SelectSection("summary");
    private void OnDiagnosticErrorsClick(object? sender, RoutedEventArgs e) => SelectSection("errors");
    private void OnDiagnosticSensorsClick(object? sender, RoutedEventArgs e) => SelectSection("sensors");
    private void SelectSection(string section)
    {
        ViewModel.SelectDiagnosticSection(section);
        SyncTabSelection();
    }

    private void OnRefreshDiagnosticsClick(object? sender, RoutedEventArgs e) => ViewModel.RefreshDiagnostics();
    private void OnOpenErrorLogClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            ErrorLog.EnsureLogFileExists();
            Process.Start(new ProcessStartInfo(ErrorLog.LogFilePath) { UseShellExecute = true });
            ViewModel.SetDiagnosticsMessage("Arquivo de erros aberto no aplicativo padrão.");
        }
        catch (Exception ex)
        {
            ErrorLog.Write("Diagnostics.OpenErrorLog", ex);
            ViewModel.SetDiagnosticsMessage($"Não foi possível abrir o arquivo: {ex.Message}");
        }
    }
}
