using System.Diagnostics;

namespace PhantomMonitor.Services.Linux;

/// <summary>Consumes complete Vulkan frame rings and chooses a live, recently presenting process.</summary>
public sealed class LinuxFpsMonitor : IFpsMonitor
{
    private const int MaxStatsSamples = 3600;
    private readonly FrametimeBuffer _history = new(600);
    private readonly Queue<double> _samples = new();
    private readonly object _gate = new();
    private readonly Timer _timer;
    private bool _running;
    private uint _pid;
    private ulong _session;
    private ulong _lastFrame;
    private DateTime _lastStats;
    private volatile float _fps, _frametime, _average, _minimum, _maximum, _low1, _low01;
    private volatile bool _hasData;
    private volatile string _debug = "Aguardando PhantomVulkanLayer...";
    public float Fps => _fps;
    public float FrametimeMs => _frametime;
    public float FpsAvg => _average;
    public float FpsMin => _minimum;
    public float FpsMax => _maximum;
    public float Fps1Low => _low1;
    public float Fps01Low => _low01;
    public bool HasData => _hasData;
    public FrametimeBuffer FrametimeHistory => _history;
    public string DebugInfo => _debug;

    public LinuxFpsMonitor() => _timer = new Timer(_ => Poll(), null, Timeout.Infinite, Timeout.Infinite);
    public void Start() { lock (_gate) { Reset(); _running = true; _timer.Change(0, 100); } }
    public void Stop() { _timer.Change(Timeout.Infinite, Timeout.Infinite); lock (_gate) { _running = false; Reset(); } }
    public void Reset()
    {
        lock (_gate)
        {
            _samples.Clear(); _history.Clear();
            _fps = _frametime = _average = _minimum = _maximum = _low1 = _low01 = 0;
            _hasData = false; _pid = 0; _session = _lastFrame = 0; _lastStats = DateTime.MinValue;
            _debug = "Aguardando PhantomVulkanLayer...";
        }
    }

    private void Poll()
    {
        if (!Monitor.TryEnter(_gate)) return;
        try
        {
            if (!_running) return;
            var snapshots = new List<VulkanTelemetrySnapshot>();
            foreach (var root in new[] { "/dev/shm", "/tmp" })
            {
                if (!Directory.Exists(root)) continue;
                foreach (var path in Directory.EnumerateFiles(root, "phantom_fps*"))
                {
                    var snapshot = LinuxVulkanTelemetryReader.Read(path);
                    if (snapshot is not null && Directory.Exists($"/proc/{snapshot.Pid}")) snapshots.Add(snapshot);
                }
            }
            var nowNs = (ulong)(Stopwatch.GetTimestamp() * (1_000_000_000.0 / Stopwatch.Frequency));
            Consume(snapshots, nowNs);
        }
        catch (Exception ex) { ErrorLog.Write("LinuxFpsMonitor.Poll", ex); }
        finally { Monitor.Exit(_gate); }
    }

    internal void Consume(IReadOnlyList<VulkanTelemetrySnapshot> snapshots, ulong nowNs)
    {
        lock (_gate)
        {
            var candidates = snapshots.Where(s => s.TimestampNs <= nowNs && nowNs - s.TimestampNs <= 2_000_000_000UL).ToArray();
            // Retain the current live game to avoid switching on every poll;
            // stale/idle entries cannot block another active process.
            var selected = candidates.Where(s => s.Pid == _pid).OrderByDescending(s => s.TimestampNs).FirstOrDefault()
                ?? candidates.OrderByDescending(s => s.TimestampNs).FirstOrDefault();
            if (selected is null) { Reset(); return; }
            if (selected.Pid != _pid || selected.Session != _session || selected.FrameCount < _lastFrame)
            {
                Reset(); _pid = selected.Pid; _session = selected.Session;
            }
            var frames = selected.Frames.Where(f => f.Number > _lastFrame).ToArray();
            if (frames.Length == 0) return;
            var gap = _lastFrame > 0 && frames[0].Number > _lastFrame + 1;
            foreach (var frame in frames)
            {
                _history.Add(frame.FrametimeMs);
                _frametime = frame.FrametimeMs;
                if (frame.Fps <= 0) continue;
                _fps = _hasData ? 0.35f * frame.Fps + 0.65f * _fps : frame.Fps;
                _hasData = true;
                _samples.Enqueue(frame.Fps);
                if (_samples.Count > MaxStatsSamples) _samples.Dequeue();
            }
            _lastFrame = selected.FrameCount;
            if (_samples.Count > 0 && (DateTime.UtcNow - _lastStats).TotalSeconds >= 1)
            {
                var sorted = _samples.Order().ToArray();
                _average = (float)sorted.Average(); _minimum = (float)sorted[0]; _maximum = (float)sorted[^1];
                _low1 = (float)sorted[Math.Min(sorted.Length - 1, (int)(sorted.Length * 0.01))];
                _low01 = (float)sorted[Math.Min(sorted.Length - 1, (int)(sorted.Length * 0.001))];
                _lastStats = DateTime.UtcNow;
            }
            _debug = $"{_fps:F0} FPS ({_frametime:F1} ms) - {selected.Name} (PID {_pid}) [Vulkan v{(selected.Legacy ? 1 : 2)}]"
                + (selected.Legacy ? " · camada antiga: reinstale para capturar todos os frames" : "")
                + (gap ? " · ring excedido: frames perdidos durante a pausa de leitura" : "");
        }
    }

    public void Dispose() { Stop(); _timer.Dispose(); }
}
