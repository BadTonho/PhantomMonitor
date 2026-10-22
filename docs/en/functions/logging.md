> **Language:** English · [Português (Brasil)](../../funções/logging.md)

# Logging (CSV / JSON / JSONL)

WPF and Avalonia share the log formats. Their output directories differ: WPF writes `logs/` beside the app, while Avalonia Linux writes to the user's data directory, normally `~/.local/share/PhantomMonitor/logs/` and respecting `XDG_DATA_HOME`.

## CSV (`CsvLogger`)

CSV columns are created from the first snapshot: `Timestamp`, canonical sensor series, then per-instance keys such as `GpuCore#1`, `CpuTemperature#3`, and `FanSpeed#2`. A new file is created for each collection session. Names are `sensors_YYYYMMDD_HHmmss.csv` in WPF and `sensors_YYYYMMDD_HHmmss_fff.csv` in Avalonia. If hardware disappears, its column remains and the row contains zero to preserve table alignment.

`WriteMarker()` adds `#marker,<timestamp>,<label>` to the active CSV. Resetting session statistics creates a marker such as `reset (inventory)` without disrupting spreadsheet columns.

## Latest snapshot JSON (`JsonLogger`)

`sensors_latest.json` is atomically replaced through a temporary file so readers never see a partial document. It includes canonical `values` and per-instance `series`/`seriesLabels`, `seriesStats` (min, max, sum, count, and average), and connected-monitor metadata in `monitors`. GPU process details are shown in the dashboard and are not serialized.

Only series that have values in the snapshot are included. Instance series use their full `Category#index` key. The monitor metadata includes manufacturer, model, product code, serial, diagonal, manufacturing year/week, and primary status from EDID/WMI. The JSON snapshot is useful for external integrations that need one current state rather than a time series.

## Append-only JSONL (`JsonJournalLogger`)

Each session gets `sensors_YYYYMMDD_HHmmss.jsonl` (WPF) or a millisecond-suffixed filename (Avalonia). One JSON object is appended per event:

```json
{"kind":"meta","ts":"...","labels":{"GpuCore":"GPU Core","Custom#1":"DeltaTemp (°C)"}}
{"kind":"sample","ts":"...","series":{"GpuCore":12.3,"GpuCore#1":8.1,"Custom#1":7.7}}
{"kind":"note","ts":"...","text":"reset (tray)"}
```

The journal starts with a session-start note. The first snapshot adds a `meta` labels record, followed by `sample` records containing every present series. Reset events add `note` records. Writes are flushed promptly; I/O failures go to `errors.log` and do not stop monitoring.

Because the metadata record is created with the first sample, it may follow the initial session-start note rather than being the first line in the file. `sample` entries include canonical, per-instance, and `Custom#i` series. A typical tail command is `Get-Content -Wait logs\sensors_*.jsonl -Tail 1`; closed journals can be loaded with `jq -s`.

## Error log

`logs/errors.log` records internal errors as timestamp, context, and message. It is stored in a `logs/` directory beside the executable in both frontends; Linux telemetry files use the per-user data directory described above. Consecutive duplicate messages are suppressed; the file is cleared after it grows beyond roughly 1 MB. Logging failures never throw into the app. Global UI, background-thread, and unobserved-task exception handlers also report errors here.

The WPF handlers include dispatcher exceptions (a dialog is shown only for a new error), `AppDomain.UnhandledException` for background threads, and `TaskScheduler.UnobservedTaskException`, which is marked observed after logging.

## Activation and lifecycle

The `config.json` flags `logToCsv`, `logToJson`, and `logToJsonJournal` control each format independently. On Linux, the Avalonia **Settings** page edits these flags and applies changes when saving successfully. WPF exposes its existing main-window toggles, applied to the next monitoring session. Pausing stops new samples while keeping files open; stopping collection or closing the app closes active files. See [configuration](configuration.md) for paths and settings.

CSV filenames use session timestamps (`sensors_YYYYMMDD_HHmmss.csv` on WPF and a millisecond suffix on Avalonia). JSON always uses the fixed name `sensors_latest.json`. JSONL receives a timestamped filename per session. The error log is beside the executable for both frontends. Linux telemetry CSV/JSON/JSONL files are stored below `~/.local/share/PhantomMonitor/logs/`, or under `$XDG_DATA_HOME` when set.
