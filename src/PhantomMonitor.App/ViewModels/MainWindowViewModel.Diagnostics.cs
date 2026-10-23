using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Collections.ObjectModel;
using System.Threading;
using Avalonia.Threading;
using PhantomMonitor.App.Services;
using PhantomMonitor.Models;
using PhantomMonitor.Services;
using PhantomMonitor.Services.Linux;
using PhantomMonitor.Services.Telemetry;

namespace PhantomMonitor.App.ViewModels;

// Diagnóstico: erros registrados e estado atual de disponibilidade dos sensores.
public sealed partial class MainWindowViewModel
{
    public void SelectDiagnosticSection(string section)
    {
        if (section is not ("summary" or "errors" or "sensors") ||
            _selectedDiagnosticSection == section)
            return;

        _selectedDiagnosticSection = section;
        Raise(nameof(SelectedDiagnosticSection));
        Raise(nameof(IsDiagnosticSummary));
        Raise(nameof(IsDiagnosticErrors));
        Raise(nameof(IsDiagnosticSensors));
        if (section is "errors" or "sensors")
            RefreshDiagnosticSensorState();
    }

    public void RefreshDiagnostics()
    {
        LoadDiagnosticEntries();
        RefreshDiagnosticSensorState();
        DiagnosticsMessage = "Diagnóstico atualizado.";
    }

    private void RefreshDiagnosticSensorState(SensorData? snapshot = null)
    {
        snapshot ??= _latestSensorData;
        if (snapshot is null) return;

        HasDiagnosticSensorSnapshot = true;
        DiagnosticSensorLines = snapshot.SensorDebugInfo.ToArray();
        DiagnosticSensorWarnings = snapshot.SensorAvailabilityDiagnostics
            .Where(diagnostic => !diagnostic.IsAvailable)
            .Select(DiagnosticSensorWarningViewModel.From)
            .ToArray();
    }

    public void SetDiagnosticsMessage(string message) => DiagnosticsMessage = message;

    private void LoadDiagnosticEntries()
    {
        DiagnosticEntries.Clear();
        foreach (var entry in ErrorLog.ReadRecentEntries())
            DiagnosticEntries.Add(entry);
        RaiseDiagnosticEntryProperties();
    }

    private void OnDiagnosticEntryWritten(ErrorLogEntry entry)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_disposed) return;
            if (DiagnosticEntries.Contains(entry)) return;
            DiagnosticEntries.Add(entry);
            while (DiagnosticEntries.Count > 500)
                DiagnosticEntries.RemoveAt(0);
            RaiseDiagnosticEntryProperties();
        });
    }

    private void RaiseDiagnosticEntryProperties()
    {
        Raise(nameof(HasDiagnosticEntries));
        Raise(nameof(DiagnosticEntriesSummary));
        Raise(nameof(DiagnosticErrorCountText));
        Raise(nameof(DiagnosticLatestErrorHeader));
        Raise(nameof(DiagnosticLatestErrorMessage));
    }

}
