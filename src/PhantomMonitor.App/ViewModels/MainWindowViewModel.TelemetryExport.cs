using Avalonia.Threading;
using PhantomMonitor.Services.Telemetry;

namespace PhantomMonitor.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    private void ConfigureTelemetryExporter()
    {
        var options = _config.TelemetryExport;
        if (!options.Enabled)
        {
            DisposeTelemetryExporter();
            UpdateTelemetryExportStatus();
            return;
        }

        if (!IsRunning)
        {
            DisposeTelemetryExporter();
            UpdateTelemetryExportStatus();
            return;
        }

        if (_telemetryHttpExporter is not null
            && _activeTelemetryPort == options.Port
            && _activeTelemetryListenAll == options.ListenAll
            && _telemetryHttpExporter.IsListening)
        {
            UpdateTelemetryExportStatus();
            return;
        }

        DisposeTelemetryExporter();
        var exporter = new TelemetryHttpExporter(options.Port, options.ListenAll);
        _telemetryHttpExporter = exporter;
        _activeTelemetryPort = options.Port;
        _activeTelemetryListenAll = options.ListenAll;
        exporter.StateChanged += OnTelemetryExporterStateChanged;
        if (_latestSensorData is not null)
            exporter.Publish(_latestSensorData);

        exporter.Start();
        UpdateTelemetryExportStatus();
    }

    private void OnTelemetryExporterStateChanged(TelemetryHttpExporter exporter) => Dispatcher.UIThread.Post(() =>
    {
        if (_disposed || !ReferenceEquals(exporter, _telemetryHttpExporter)) return;
        UpdateTelemetryExportStatus();
    });

    private void UpdateTelemetryExportStatus()
    {
        if (!_config.TelemetryExport.Enabled)
        {
            TelemetryExportStatusText = "Desativado";
            return;
        }

        if (!IsRunning)
        {
            TelemetryExportStatusText = "Aguardando coleta";
            return;
        }

        if (_telemetryHttpExporter is null)
        {
            TelemetryExportStatusText = "Aguardando inicialização";
            return;
        }

        TelemetryExportStatusText = _telemetryHttpExporter.IsListening
            ? $"Escutando em {_telemetryHttpExporter.Endpoint}"
            : $"Falha ao abrir o listener: {_telemetryHttpExporter.LastError ?? "erro desconhecido"}";
    }

    private void DisposeTelemetryExporter()
    {
        var exporter = _telemetryHttpExporter;
        _telemetryHttpExporter = null;
        _activeTelemetryPort = null;
        _activeTelemetryListenAll = null;
        if (exporter is null) return;
        exporter.StateChanged -= OnTelemetryExporterStateChanged;
        exporter.Dispose();
    }
}
