using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Collections.ObjectModel;
using System.Threading;
using Avalonia.Threading;
using PhantomMonitor.App.Services;
using PhantomMonitor.Models;
using PhantomMonitor.Services;
using PhantomMonitor.Services.Telemetry;


namespace PhantomMonitor.App.ViewModels;

// Troca de páginas (Overview, Inventário, Configurações, Alertas...).
public sealed partial class MainWindowViewModel
{
    public void ShowOverview()
    {
        IsOverview = true;
        IsSettings = false;
        IsAlerts = false;
        IsCustomSensors = false;
        IsSensorTuning = false;
        IsDiagnostics = false;
        Raise(nameof(IsInventory));
    }

    public void ShowInventory()
    {
        IsOverview = false;
        IsSettings = false;
        IsAlerts = false;
        IsCustomSensors = false;
        IsSensorTuning = false;
        IsDiagnostics = false;
        Raise(nameof(IsInventory));
    }

    public void ShowSettings()
    {
        IsOverview = false;
        IsSettings = true;
        IsAlerts = false;
        IsCustomSensors = false;
        IsSensorTuning = false;
        IsDiagnostics = false;
        Raise(nameof(IsInventory));
    }

    public void ShowAlerts()
    {
        IsOverview = false;
        IsSettings = false;
        IsAlerts = true;
        IsCustomSensors = false;
        IsSensorTuning = false;
        IsDiagnostics = false;
        Raise(nameof(IsInventory));
    }

    public void ShowCustomSensors()
    {
        IsOverview = false;
        IsSettings = false;
        IsAlerts = false;
        IsCustomSensors = true;
        IsSensorTuning = false;
        IsDiagnostics = false;
        Raise(nameof(IsInventory));
    }

    public void ShowSensorTuning()
    {
        IsOverview = false;
        IsSettings = false;
        IsAlerts = false;
        IsCustomSensors = false;
        IsSensorTuning = true;
        IsDiagnostics = false;
        Raise(nameof(IsInventory));
    }

    public void ShowDiagnostics()
    {
        IsOverview = false;
        IsSettings = false;
        IsAlerts = false;
        IsCustomSensors = false;
        IsSensorTuning = false;
        IsDiagnostics = true;
        SelectDiagnosticSection("summary");
        LoadDiagnosticEntries();
        RefreshDiagnosticSensorState();
        Raise(nameof(IsInventory));
    }

}
