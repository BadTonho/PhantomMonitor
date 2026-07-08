using System.IO;
using System.Text.Json;

namespace RivaMonitor.Services;

public sealed class AppConfig
{
    public int UpdateIntervalMs { get; set; } = 1000;
    public bool LogToCsv { get; set; } = false;
    public bool LogToJson { get; set; } = true;
    public bool ShowOverlay { get; set; } = true;
    public double OverlayOpacity { get; set; } = 0.85;
    public OverlayPositionConfig OverlayPosition { get; set; } = new();
    public OverlayConfig Overlay { get; set; } = new();
    public SensorConfig Sensors { get; set; } = new();

    public static AppConfig Load(string path)
    {
        try
        {
            var json = File.ReadAllText(path);
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            return JsonSerializer.Deserialize<AppConfig>(json, options) ?? new AppConfig();
        }
        catch
        {
            return new AppConfig();
        }
    }
}

public sealed class OverlayPositionConfig
{
    public double X { get; set; } = 20;
    public double Y { get; set; } = 80;
}

public sealed class OverlayConfig
{
    public int Width { get; set; } = 220;
    public bool ShowCpu { get; set; } = true;
    public bool ShowGpu { get; set; } = true;
    public bool ShowRam { get; set; } = true;
    public bool ShowDisk { get; set; } = true;
    public bool ShowNetwork { get; set; } = false;
    public bool ShowGpuProcesses { get; set; } = false;
    public bool ShowFans { get; set; } = true;
    public bool ShowFps { get; set; } = true;
    public bool CompactMode { get; set; } = false;
    public string AccentColor { get; set; } = "#1E90FF";
}

public sealed class SensorConfig
{
    public bool Cpu { get; set; } = true;
    public bool Gpu { get; set; } = true;
    public bool Ram { get; set; } = true;
    public bool Disk { get; set; } = true;
    public bool Network { get; set; } = false;
    public bool Motherboard { get; set; } = false;
}
