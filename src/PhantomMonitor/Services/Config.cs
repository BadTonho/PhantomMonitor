using System.IO;
using System.Text.Json;

namespace PhantomMonitor.Services;

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
            if (!File.Exists(path))
            {
                var dir = Path.GetDirectoryName(path) ?? "";
                var examplePath = new[]
                {
                    Path.Combine(dir, "config.example.json"),
                    Path.Combine(AppContext.BaseDirectory, "config.example.json")
                }.FirstOrDefault(File.Exists);

                if (examplePath != null)
                {
                    if (dir.Length > 0) Directory.CreateDirectory(dir);
                    File.Copy(examplePath, path);
                }
            }

            if (File.Exists(path))
            {
                var json = File.ReadAllText(path);
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var cfg = JsonSerializer.Deserialize<AppConfig>(json, options) ?? new AppConfig();
                cfg.Normalize();
                return cfg;
            }

            return new AppConfig();
        }
        catch (Exception ex)
        {
            ErrorLog.Write("Config.Load", ex);
            return new AppConfig();
        }
    }

    private static readonly System.Text.RegularExpressions.Regex HexColorRegex =
        new("^#(?:[0-9a-fA-F]{6}|[0-9a-fA-F]{8})$", System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>Corrige valores inválidos vindos de um config editado à mão.</summary>
    public void Normalize()
    {
        if (UpdateIntervalMs < 100) UpdateIntervalMs = 100;
        OverlayOpacity = Math.Clamp(OverlayOpacity, 0.1, 1.0);
        OverlayPosition ??= new();
        Overlay ??= new();
        Sensors ??= new();

        Overlay.Width = Math.Clamp(Overlay.Width, 140, 700);
        Overlay.FontSize = Math.Clamp(Overlay.FontSize, 8, 20);
        if (!IsValidHexColor(Overlay.AccentColor)) Overlay.AccentColor = "#1E90FF";
    }

    private static bool IsValidHexColor(string? value) =>
        !string.IsNullOrWhiteSpace(value) && HexColorRegex.IsMatch(value);

    public bool Save(string path)
    {
        try
        {
            var options = new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
            var json = JsonSerializer.Serialize(this, options);

            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            // Escrita atômica: evita config.json truncado se o app fechar no meio.
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, json);
            File.Move(tmp, path, overwrite: true);
            return true;
        }
        catch (Exception ex)
        {
            ErrorLog.Write("Config.Save", ex);
            return false;
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
    public bool ShowMotherboard { get; set; } = false;
    public bool ShowFps { get; set; } = true;
    public bool ShowFrametimeGraph { get; set; } = true;
    public bool CompactMode { get; set; } = false;
    public string AccentColor { get; set; } = "#1E90FF";
    public double FontSize { get; set; } = 11.5;
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
