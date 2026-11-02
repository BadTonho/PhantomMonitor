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

// Loggers de sessão (CSV/JSON/journal) em thread dedicada.
public sealed partial class MainWindowViewModel
{
    private void StartLoggers()
    {
        var logDirectory = UserDataPaths.LogDirectory;
        var sessionStamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture);

        if (_config.LogToCsv)
            _csvLogger = new CsvLogger(Path.Combine(logDirectory, $"sensors_{sessionStamp}.csv"));

        if (_config.LogToJson)
            _jsonLogger = new JsonLogger(Path.Combine(logDirectory, "sensors_latest.json"));

        if (_config.LogToJsonJournal)
        {
            _jsonJournalLogger = new JsonJournalLogger(Path.Combine(logDirectory, $"sensors_{sessionStamp}.jsonl"));
            _jsonJournalLogger.WriteNote("início da sessão");
        }

        // Loggers em thread dedicada (antes: CSV + JSON + journal serializados na
        // UI thread a cada snapshot, em até 10 Hz). Mesma bomba do frontend WPF;
        // Drain no DisposeLoggers garante drenagem antes de dispor os escritores.
        _snapshotWriter?.Dispose();
        _snapshotWriter = new BackgroundSnapshotWriter();
        if (_csvLogger is not null) _snapshotWriter.AddWriter(_csvLogger.Write);
        if (_jsonLogger is not null) _snapshotWriter.AddWriter(_jsonLogger.Write);
        if (_jsonJournalLogger is not null) _snapshotWriter.AddWriter(_jsonJournalLogger.Write);
    }

    /// <summary>Bomba de fundo para loggers; vida útil = sessão de loggers.
    /// Ver BackgroundSnapshotWriter (drenagem antes de dispor os escritores).</summary>
    private BackgroundSnapshotWriter? _snapshotWriter;

    private void DisposeLoggers()
    {
        _snapshotWriter?.Dispose();
        _snapshotWriter = null;
        _csvLogger?.Dispose();
        _jsonLogger?.Dispose();
        _jsonJournalLogger?.Dispose();
        _csvLogger = null;
        _jsonLogger = null;
        _jsonJournalLogger = null;
    }

}
