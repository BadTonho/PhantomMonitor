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

// Ciclo de execução: Start/Stop/Pause.
public sealed partial class MainWindowViewModel
{
    public void Start()
    {
        if (_disposed || IsRunning) return;
        StartLoggers();
        _sensorService.Start();
        _started = true;
        ConfigureTelemetryExporter();
        StatusText = "Monitorando";
        Raise(nameof(IsRunning));
        Raise(nameof(IsPaused));
        Raise(nameof(CanStart));
        Raise(nameof(CanStop));
        Raise(nameof(CanPause));
        Raise(nameof(PauseButtonText));
    }

    public void Stop()
    {
        if (!IsRunning) return;
        _sensorService.SetPaused(false);
        _sensorService.Stop();
        _started = false;
        DisposeTelemetryExporter();
        _latestSensorData = null;
        UpdateTelemetryExportStatus();
        DisposeLoggers();
        StatusText = "Parado";
        HasFpsData = false;
        FpsHint = "Monitoramento parado. Inicie novamente para continuar.";
        Raise(nameof(IsRunning));
        Raise(nameof(IsPaused));
        Raise(nameof(CanStart));
        Raise(nameof(CanStop));
        Raise(nameof(CanPause));
        Raise(nameof(PauseButtonText));
    }

    public void TogglePause()
    {
        if (!IsRunning) return;
        _sensorService.SetPaused(!_sensorService.Paused);
        StatusText = IsPaused ? "Pausado" : "Monitorando";
        Raise(nameof(IsPaused));
        Raise(nameof(PauseButtonText));
    }

}
