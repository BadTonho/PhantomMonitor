using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Collections.ObjectModel;
using System.Threading;
using Avalonia;
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
    private readonly Dictionary<string, string> _linuxOverlayDiagnosticsByBlock = new(StringComparer.OrdinalIgnoreCase);

    internal void RecordLinuxOverlayGeometry(string key, string title, PixelPoint requested, PixelPoint effective,
        double widthDip, double heightDip, double scaling)
    {
        var session = Environment.GetEnvironmentVariable("XDG_SESSION_TYPE") ?? "desconhecida";
        var backend = GetLinuxOverlayBackend();
        var hasWaylandDisplay = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY"));
        var xdgDisplay = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DISPLAY"));
        var mismatch = Math.Abs(requested.X - effective.X) > 2 || Math.Abs(requested.Y - effective.Y) > 2;
        var nativeWaylandBackend = backend.Contains("Wayland nativo", StringComparison.OrdinalIgnoreCase);
        scaling = double.IsFinite(scaling) && scaling > 0 ? scaling : 1;
        var pixelWidth = widthDip * scaling;
        var pixelHeight = heightDip * scaling;
        var limitation = mismatch
            ? nativeWaylandBackend
                ? " Limitação: o compositor Wayland pode ignorar posições absolutas."
                : " O gerenciador de janelas ajustou a posição solicitada."
            : "";
        var line = string.Format(CultureInfo.InvariantCulture,
            "{0} ({1}) · sessão={2}; backend={3}; DISPLAY={4}; WAYLAND_DISPLAY={5}; posição solicitada=({6},{7}) px; efetiva=({8},{9}) px; tamanho={10:0.##}×{11:0.##} DIP (~{12:0.##}×{13:0.##} px, escala {14:0.##}).{15}",
            key, title, session, backend, xdgDisplay ? "sim" : "não", hasWaylandDisplay ? "sim" : "não",
            requested.X, requested.Y, effective.X, effective.Y, widthDip, heightDip, pixelWidth, pixelHeight, scaling,
            limitation);

        if (_linuxOverlayDiagnosticsByBlock.TryGetValue(key, out var existing) && existing == line) return;
        _linuxOverlayDiagnosticsByBlock[key] = line;
        LinuxOverlayDiagnosticLines = _linuxOverlayDiagnosticsByBlock
            .OrderBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase)
            .Select(entry => entry.Value)
            .ToArray();
        Raise(nameof(IsLinuxOverlayDiagnosticsVisible));
    }

    internal void RemoveLinuxOverlayGeometry(string key)
    {
        if (!_linuxOverlayDiagnosticsByBlock.Remove(key)) return;
        LinuxOverlayDiagnosticLines = _linuxOverlayDiagnosticsByBlock
            .OrderBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase)
            .Select(entry => entry.Value)
            .ToArray();
        Raise(nameof(IsLinuxOverlayDiagnosticsVisible));
    }

    internal void ClearLinuxOverlayGeometry()
    {
        if (_linuxOverlayDiagnosticsByBlock.Count == 0) return;
        _linuxOverlayDiagnosticsByBlock.Clear();
        LinuxOverlayDiagnosticLines = [];
        Raise(nameof(IsLinuxOverlayDiagnosticsVisible));
    }

    private static string GetLinuxOverlayBackend()
    {
        var loadedAssemblies = AppDomain.CurrentDomain.GetAssemblies()
            .Select(assembly => assembly.GetName().Name ?? "")
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (loadedAssemblies.Contains("Avalonia.Wayland")) return "Wayland nativo (Avalonia.Wayland)";
        if (loadedAssemblies.Contains("Avalonia.X11")) return "X11/XWayland (Avalonia.X11)";
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DISPLAY")))
            return "X11/XWayland inferido por DISPLAY";
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY")))
            return "Wayland indicado pelo ambiente; backend Avalonia não identificado";
        return "backend Avalonia não identificado";
    }

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
