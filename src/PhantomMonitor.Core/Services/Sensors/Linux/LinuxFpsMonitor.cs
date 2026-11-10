using System.Diagnostics;

namespace PhantomMonitor.Services.Linux;

/// <summary>Consumes complete Vulkan frame rings and chooses a live, recently presenting process.</summary>
public sealed class LinuxFpsMonitor : IFpsMonitor
{
    private const int MaxStatsSamples = 3600;
    private readonly FrametimeBuffer _history = new(600);
    private readonly Queue<double> _samples = new();
    private readonly object _gate = new();
    private readonly LinuxX11PresentMonitor _x11Monitor = new();
    private readonly Timer _timer;
    private bool _running;
    private uint _pid;
    private ulong _session;
    private ulong _lastFrame;
    private string _source = "";
    private DateTime _lastX11FrameReceived = DateTime.MinValue;
    private DateTime _lastStats;
    private volatile float _fps, _frametime, _average, _minimum, _maximum, _low1, _low01;
    private volatile bool _hasData;
    private volatile string _debug = "Aguardando frames Vulkan, EGL/GLX ou X11 Present...";
    public float Fps => _fps;
    public float FrametimeMs => _frametime;
    public float FpsAvg => _average;
    public float FpsMin => _minimum;
    public float FpsMax => _maximum;
    public float Fps1Low => _low1;
    public float Fps01Low => _low01;
    public bool HasData => _hasData;
    public uint TargetProcessId => _pid;
    public FrametimeBuffer FrametimeHistory => _history;
    public string DebugInfo => _debug;

    public LinuxFpsMonitor() => _timer = new Timer(_ => Poll(), null, Timeout.Infinite, Timeout.Infinite);
    public void Start()
    {
        lock (_gate) { Reset(); _running = true; _timer.Change(0, 100); }
        _x11Monitor.Start(ConsumeX11Frame);
    }

    public void Stop()
    {
        _timer.Change(Timeout.Infinite, Timeout.Infinite);
        _x11Monitor.Stop();
        lock (_gate) { _running = false; Reset(); }
    }
    public void Reset()
    {
        lock (_gate)
        {
            _samples.Clear(); _history.Clear();
            _fps = _frametime = _average = _minimum = _maximum = _low1 = _low01 = 0;
            _hasData = false; _pid = 0; _session = _lastFrame = 0; _source = "";
            _lastStats = DateTime.MinValue; _lastX11FrameReceived = DateTime.MinValue;
            _debug = $"Aguardando frames Vulkan, EGL/GLX ou X11 Present. {_x11Monitor.Status}";
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
            var hasFreshHookFrames = snapshots.Any(snapshot => snapshot.TimestampNs <= nowNs
                && nowNs - snapshot.TimestampNs <= 2_000_000_000UL);
            if (!hasFreshHookFrames && (DateTime.UtcNow - _lastX11FrameReceived).TotalSeconds <= 2)
                return;
            Consume(snapshots, nowNs);
        }
        catch (Exception ex) { ErrorLog.WriteThrottled("LinuxFpsMonitor.Poll", ex); }
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
                Reset(); _pid = selected.Pid; _session = selected.Session; _source = "shared memory";
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
            _debug = $"{_fps:F0} FPS ({_frametime:F1} ms) - {selected.Name} (PID {_pid}) [shared memory v{(selected.Legacy ? 1 : 2)}]"
                + (selected.Legacy ? " · camada antiga: reinstale para capturar todos os frames" : "")
                + (gap ? " · ring excedido: frames perdidos durante a pausa de leitura" : "");
        }
    }

    private void ConsumeX11Frame(uint pid, string name, float frametimeMs, float fps)
    {
        if (pid == 0 || !float.IsFinite(frametimeMs) || !float.IsFinite(fps)
            || frametimeMs <= 0 || fps <= 0) return;

        lock (_gate)
        {
            if (!_running) return;
            if (_pid != pid || !string.Equals(_source, "X11 Present", StringComparison.Ordinal))
            {
                Reset();
                _pid = pid;
                _source = "X11 Present";
            }

            _history.Add(frametimeMs);
            _frametime = frametimeMs;
            _fps = _hasData ? 0.35f * fps + 0.65f * _fps : fps;
            _hasData = true;
            _samples.Enqueue(fps);
            if (_samples.Count > MaxStatsSamples) _samples.Dequeue();
            _lastX11FrameReceived = DateTime.UtcNow;

            if ((DateTime.UtcNow - _lastStats).TotalSeconds >= 1)
            {
                var sorted = _samples.Order().ToArray();
                _average = (float)sorted.Average(); _minimum = (float)sorted[0]; _maximum = (float)sorted[^1];
                _low1 = (float)sorted[Math.Min(sorted.Length - 1, (int)(sorted.Length * 0.01))];
                _low01 = (float)sorted[Math.Min(sorted.Length - 1, (int)(sorted.Length * 0.001))];
                _lastStats = DateTime.UtcNow;
            }

            _debug = $"{_fps:F0} FPS ({_frametime:F1} ms) - {name} (PID {_pid}) [X11 Present]";
        }
    }

    public void Dispose() { Stop(); _x11Monitor.Dispose(); _timer.Dispose(); }
}
