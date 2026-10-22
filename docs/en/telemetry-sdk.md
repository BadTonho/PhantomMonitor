> **Language:** English · [Português (Brasil)](../telemetry-sdk.md)

# Telemetry SDK (shared memory and HTTP)

External tools such as OBS, AutoHotkey, dashboards, scripts, and other apps can consume Phantom Monitor telemetry without reading log files:

1. **Shared memory** is always available while monitoring. A structured snapshot is published at the monitor cadence (minimum 100 ms).
2. **HTTP exporter** is optional through `telemetryExport.enabled`; it exposes the latest snapshot as JSON at `GET /values`.

The shared-memory map is created when collection starts and removed when it stops. `sensors_latest.json` remains a legacy fallback.

## Shared-memory identity and layout

- Map name: `phantom_monitor_telemetry_v1`, scoped to the local user/session. Readers should run in the same session; a global namespace would require broader permissions.
- Size: 20,544 bytes: 64-byte header plus 256 entries of 80 bytes each.
- Read permission is sufficient for consumers.

```text
0x00  int32   magic        "PMTS" = 0x53544D50 (little-endian)
0x04  int32   version      1
0x08  int32   seq          publication counter (seqlock)
0x0C  int32   seriesCount  number of valid entries
0x10  int64   timestamp    snapshot UnixTimeMilliseconds
0x18  40 bytes reserved    zero

Entry i at 64 + i*80:
+000  char[40] key    ASCII series key, NUL padded ("GpuCore", "GpuCore#1")
+040  double   value  telemetry value (NaN means removed this cycle)
+048  char[32] label  ASCII source label (non-ASCII characters become *)
```

Entries follow `GetSeriesKeys()`: canonical categories in `SensorType` enum order, then instance keys sorted by key. CSV columns use the same ordering. Series keys are stable identifiers; labels are for display and may be truncated or transliterated.

## Consistent reads (seqlock)

The writer first sets `seq` to an odd value, writes the header and current entries (marking removed entries as NaN), then writes the next even value. Readers should retry if the initial sequence is odd or changes during a read:

```csharp
int seq1 = acc.ReadInt32(0x08);
if ((seq1 & 1) != 0) retry;
var magic = acc.ReadInt32(0x00);
var count = acc.ReadInt32(0x0C);
var ts = acc.ReadInt64(0x10);
// Read entries...
if (acc.ReadInt32(0x08) != seq1) retry;
```

In C, read the same sequence around the copied entries. On ARM64, load `seq` with acquire semantics (for example, `__atomic_load_n(..., __ATOMIC_ACQUIRE)`). The publication interval follows `updateIntervalMs`, with a 100 ms minimum. The mapped layout supports at most 256 series, keys up to 40 characters, and labels up to 32 characters.

## HTTP exporter (`telemetryExport`)

```json
"telemetryExport": { "enabled": false, "port": 7317, "listenAll": false }
```

- `GET /values` returns the same snapshot payload as `sensors_latest.json`: `timestamp`, `values`, `labels`, `series`, `seriesLabels`, `seriesStats`, and `monitors`.
- `GET /health` returns a health-check response such as `{"ok":true,"timestamp":"..."}`. Other paths return 404.
- Default address: `http://127.0.0.1:7317/`. `listenAll: true` binds all interfaces, which can require firewall changes. The service has no TLS or authentication; use only on a trusted network. The port is normalized to 1024–65535.
- Responses use `Cache-Control: no-store`, JSON content type, and content length.
- The listener exists only while monitoring is started. It continues serving the frozen last snapshot while paused. Bind failures are logged and do not stop collection.
- On Linux, **Settings** edits `enabled`, `port`, and `listenAll`, and shows the endpoint and listener state. Changing the address or port during a pause reopens the listener and republishes the frozen snapshot.

HTTP is used instead of UDP for discoverability, keep-alive, standard headers, and easy access from browsers and common clients. Shared memory remains better suited to high-frequency local consumers.

```bash
curl http://127.0.0.1:7317/values
```

## Reference reader

`tools/TelemetrySdkReader/` is a .NET 10 console reader outside the main solution:

```bash
dotnet run --project tools/TelemetrySdkReader
dotnet run --project tools/TelemetrySdkReader -- 250
```

Consumers should check the `version` before parsing. A breaking layout change requires a new map name (for example, `_v2`) and a version increment.
