using System.Text.RegularExpressions;
using PhantomMonitor.Models;

namespace PhantomMonitor.Services;

public sealed partial class AppConfig
{
    private static readonly Regex HexColorRegex =
        new("^#(?:[0-9a-fA-F]{6}|[0-9a-fA-F]{8})$", RegexOptions.Compiled);

    /// <summary>Corrige valores inválidos vindos de um config editado à mão.</summary>
    public void Normalize()
    {
        if (UpdateIntervalMs < 100) UpdateIntervalMs = 100;

        // Tema de interface (F1.6/Fase 3): só os dois builtin existem; valor desconhecido
        // volta ao Neptune em vez de deixar o app sem tokens.
        Theme = (Theme ?? "").Trim().ToLowerInvariant();
        if (Theme is not ("neptune" or "slate")) Theme = "neptune";

        OverlayOpacity = Math.Clamp(OverlayOpacity, 0.1, 1.0);
        OverlayPosition ??= new();
        Overlay ??= new();
        var normalizedOverlayLayout = new Dictionary<string, OverlayBlockPosition>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, position) in Overlay.Layout ?? [])
        {
            if (string.IsNullOrWhiteSpace(key) || position is null) continue;
            position.X = double.IsFinite(position.X) ? Math.Clamp(position.X, 0, 1) : 0;
            position.Y = double.IsFinite(position.Y) ? Math.Clamp(position.Y, 0, 1) : 0;
            normalizedOverlayLayout[key.Trim().ToLowerInvariant()] = position;
        }
        Overlay.Layout = normalizedOverlayLayout;
        Overlay.Graphs ??= [];
        var graphIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var graph in Overlay.Graphs)
            if (string.IsNullOrWhiteSpace(graph.Id) || !graphIds.Add(graph.Id)) graph.Id = Guid.NewGuid().ToString("N");
        Overlay.Templates ??= [];
        var templateIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var template in Overlay.Templates)
            if (string.IsNullOrWhiteSpace(template.Id) || !templateIds.Add(template.Id)) template.Id = Guid.NewGuid().ToString("N");
        Sensors ??= new();

        // Exportador HTTP: porta em faixa de usuário (abaixo de 1024 costuma exigir
        // privilégio e confunde com serviços do sistema), loopback por padrão.
        TelemetryExport ??= new();
        TelemetryExport.Port = Math.Clamp(TelemetryExport.Port, 1024, 65535);

        // Atualizações: repositório vazio usa o padrão embutido no UpdateService
        // (decisão de normalize, não trava a string no config).
        Update ??= new();
        Update.Repository = (Update.Repository ?? "").Trim();

        // Whitelist de séries por instância: config antigo sem a chave (ou com lista vazia)
        // volta ao padrão, senão o OSD ficaria sem multi-GPU/ventoinhas/discos extras.
        string[] defaultSeries = ["Gpu*", "FanSpeed*", "Disk*"];
        if (Overlay.ShownSeries is not { Count: > 0 })
        {
            Overlay.ShownSeries = [.. defaultSeries];
        }
        else
        {
            Overlay.ShownSeries.RemoveAll(string.IsNullOrWhiteSpace);
            Overlay.ShownSeries = Overlay.ShownSeries
                .Select(p => p.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (Overlay.ShownSeries.Count == 0) Overlay.ShownSeries = [.. defaultSeries];
        }

        if (Overlay.Blocks is null)
        {
            Overlay.Blocks = MigrateLegacyOverlayBlocks(Overlay);
            Overlay.BlocksMigrated = true;
        }
        Overlay.Blocks.RemoveAll(block => block is null);
        var blockIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var block in Overlay.Blocks)
        {
            block.Id = string.IsNullOrWhiteSpace(block.Id) || !blockIds.Add(block.Id)
                ? Guid.NewGuid().ToString("N")
                : block.Id.Trim();
            block.X = double.IsFinite(block.X) ? Math.Clamp(block.X, 0, 1) : 0;
            block.Y = double.IsFinite(block.Y) ? Math.Clamp(block.Y, 0, 1) : 0;
            block.Items ??= [];
            block.Items.RemoveAll(item => item is null);
            foreach (var item in block.Items)
            {
                item.Kind = (item.Kind ?? "").Trim().ToLowerInvariant();
                item.Value = (item.Value ?? "").Trim();
                if (item.Kind is not ("category" or "series" or "graph" or "template" or "frametime") || item.Value.Length == 0)
                    item.Kind = "invalid";
            }
            block.Items.RemoveAll(item => item.Kind == "invalid");
        }

        Overlay.Width = Math.Clamp(Overlay.Width, 140, 700);
        Overlay.FontSize = Math.Clamp(Overlay.FontSize, 8, 20);
        if (!IsValidHexColor(Overlay.AccentColor)) Overlay.AccentColor = "#1E90FF";

        // Offset do overlay relativo à janela em foco: extremos digitados à mão no editor
        // deixavam o overlay nascendo fora da área de trabalho, invisível e irrecuperável.
        OverlayPosition.X = Math.Clamp(OverlayPosition.X, -5000, 5000);
        OverlayPosition.Y = Math.Clamp(OverlayPosition.Y, -5000, 5000);

        ToggleOverlayHotkey = (ToggleOverlayHotkey ?? "").Trim();
        PauseMonitorHotkey = (PauseMonitorHotkey ?? "").Trim();

        Alerts ??= new();
        Alerts.CooldownSeconds = Math.Clamp(Alerts.CooldownSeconds, 1, 3600);
        Alerts.Rules ??= new();
        Alerts.Rules.RemoveAll(rule => rule is null);
        foreach (var rule in Alerts.Rules)
        {
            rule.CooldownSeconds = Math.Clamp(rule.CooldownSeconds, 0, 3600);
            rule.Script ??= "";
            rule.ScriptArguments ??= "";
            rule.Message ??= "";
        }

        // Perfis por aplicativo: nomes obrigatórios, faixas dos overrides, AppMatch limpo.
        Profiles ??= new();
        Profiles.RemoveAll(p => p is null);
        foreach (var profile in Profiles)
        {
            if (string.IsNullOrWhiteSpace(profile.Name))
                profile.Name = $"Perfil {Profiles.IndexOf(profile) + 1}";

            profile.AppMatch = (profile.AppMatch ?? "").Trim('.').Trim();
            if (profile.AppMatch.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                profile.AppMatch = profile.AppMatch[..^4].Trim();

            if (profile.OffsetX is { } x) profile.OffsetX = Math.Clamp(x, -5000, 5000);
            if (profile.OffsetY is { } y) profile.OffsetY = Math.Clamp(y, -5000, 5000);
            if (profile.Width is { } w) profile.Width = Math.Clamp(w, 140, 700);
            if (profile.FontSize is { } f) profile.FontSize = Math.Clamp(f, 8, 20);
            if (profile.Opacity is { } o) profile.Opacity = Math.Clamp(o, 0.1, 1.0);

            if (profile.ShownSeries is { Count: > 0 })
            {
                profile.ShownSeries.RemoveAll(string.IsNullOrWhiteSpace);
                profile.ShownSeries = profile.ShownSeries
                    .Select(p => p.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                if (profile.ShownSeries.Count == 0) profile.ShownSeries = null;
            }

            if (profile.AccentColor is { } c && !IsValidHexColor(c)) profile.AccentColor = null;
        }

        // Templates do OSD (P6.6): texto obrigatório, cor valida (hex) senão cai no
        // branco padrão; templates sem texto não renderizam nada — descartados aqui.
        Overlay.Templates ??= new();
        Overlay.Templates.RemoveAll(t => t is null);
        Overlay.Templates.RemoveAll(t => string.IsNullOrWhiteSpace(t.Text));
        foreach (var template in Overlay.Templates)
        {
            template.Text = template.Text.Trim();
            if (template.Color is { } color && !IsValidHexColor(color)) template.Color = null;
        }

        // Sensores customizados: nome obrigatório (removidos com fórmula que não
        // compilam — erro registrado, os válidos mantêm a ordem/índice do config).
        CustomSensors ??= [];
        CustomSensors.RemoveAll(s => s is null || string.IsNullOrWhiteSpace(s.Name));
        foreach (var sensor in CustomSensors)
        {
            sensor.Name = sensor.Name.Trim();
            sensor.Formula = (sensor.Formula ?? "").Trim();
            sensor.Unit = (sensor.Unit ?? "").Trim();

            if (!CustomSensorEngine.IsFormulaValid(sensor.Formula))
            {
                ErrorLog.Write("Config.Normalize",
                    $"customSensors: fórmula inválida descartada para '{sensor.Name}': '{sensor.Formula}'");
                sensor.Formula = "";
            }
        }
        CustomSensors.RemoveAll(s => string.IsNullOrWhiteSpace(s.Formula));
        if (CustomSensors.Count > CustomSensorEngine.MaxSensors)
        {
            CustomSensors.RemoveRange(CustomSensorEngine.MaxSensors,
                CustomSensors.Count - CustomSensorEngine.MaxSensors);
        }

        // Gráficos do OSD (P8.5): série obrigatória (descartada sem ela), altura e
        // janela clamped; eixo fixo com min>max troca os valores; cor valida (hex).
        Overlay.Graphs ??= new();
        Overlay.Graphs.RemoveAll(g => g is null || string.IsNullOrWhiteSpace(g.Series));
        foreach (var graph in Overlay.Graphs)
        {
            graph.Series = graph.Series!.Trim();
            graph.Height = Math.Clamp(graph.Height, 12, 120);
            graph.Window = (int)Math.Clamp(graph.Window, 30, 1200);
            if (graph.Color is { } color && !IsValidHexColor(color)) graph.Color = null;
            if (graph.Min is { } min && graph.Max is { } max && min > max) (graph.Min, graph.Max) = (graph.Max, graph.Min);
            if (graph.Min is { } m1 && !double.IsFinite(m1)) graph.Min = null;
            if (graph.Max is { } m2 && !double.IsFinite(m2)) graph.Max = null;
        }
        if (Overlay.Graphs.Count > 12) Overlay.Graphs.RemoveRange(12, Overlay.Graphs.Count - 12);

        // Ajuste por série (P9.1): série obrigatória; cadência nunca fica abaixo do
        // tick global (intervalo menor seria no-op — o Normalize já rodou o clamp do
        // UpdateIntervalMs acima); suavização 0–60 (0/1 desligada). Teto de 64 regras.
        SensorTunings ??= new();
        SensorTunings.RemoveAll(t => t is null || string.IsNullOrWhiteSpace(t.Series));
        foreach (var tuning in SensorTunings)
        {
            tuning.Series = tuning.Series!.Trim();
            if (tuning.IntervalMs > 0)
                tuning.IntervalMs = Math.Max(UpdateIntervalMs, Math.Clamp(tuning.IntervalMs, 100, 3_600_000));
            tuning.Smoothing = (int)Math.Clamp(tuning.Smoothing, 0, SensorTuningEngine.MaxSmoothing);
        }
        if (SensorTunings.Count > SensorTuningEngine.MaxTunings)
        {
            SensorTunings.RemoveRange(SensorTuningEngine.MaxTunings,
                SensorTunings.Count - SensorTuningEngine.MaxTunings);
        }
    }

    internal static bool IsValidHexColor(string? value) =>
        !string.IsNullOrWhiteSpace(value) && HexColorRegex.IsMatch(value);
}
