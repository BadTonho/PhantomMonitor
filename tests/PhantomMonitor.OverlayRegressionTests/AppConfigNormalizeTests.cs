using PhantomMonitor.Models;
using PhantomMonitor.Services;

/// <summary>Testes unitários de lógica pura para <see cref="AppConfig.Normalize"/> e <see cref="TelemetryExportConfig"/>.</summary>
public sealed class AppConfigNormalizeTests
{
    [Fact]
    public void Normalize_aplica_limites_em_intervalo_tema_navegacao_e_opacidade()
    {
        var config = new AppConfig
        {
            UpdateIntervalMs = 10,
            Theme = "  DESCONHECIDO ",
            NavigationPosition = "top",
            NavigationDisplayMode = "ICONSonly",
            OverlayOpacity = 5.0,
            OverlayPosition = new OverlayPositionConfig { X = -99999, Y = 99999 },
            TelemetryExport = new TelemetryExportConfig { Port = 80 }
        };

        config.Normalize();

        Assert.Equal(100, config.UpdateIntervalMs);
        Assert.Equal("neptune", config.Theme);
        Assert.Equal("left", config.NavigationPosition);
        Assert.Equal("iconsOnly", config.NavigationDisplayMode);
        Assert.Equal(1.0, config.OverlayOpacity);
        Assert.Equal(-5000, config.OverlayPosition.X);
        Assert.Equal(5000, config.OverlayPosition.Y);
        Assert.Equal(1024, config.TelemetryExport.Port);
    }

    [Fact]
    public void Normalize_preserva_temas_e_modos_de_navegacao_validos()
    {
        var config = new AppConfig
        {
            Theme = " SLATE ",
            NavigationPosition = "DOCK",
            NavigationDisplayMode = "qualquerOutro"
        };

        config.Normalize();

        Assert.Equal("slate", config.Theme);
        Assert.Equal("dock", config.NavigationPosition);
        Assert.Equal("iconsAndText", config.NavigationDisplayMode);
    }

    [Fact]
    public void Normalize_filtra_paletas_de_tema_invalidas_e_normaliza_hex()
    {
        var validGroup = ThemePaletteCatalog.GroupKeys.First();
        var validToken = ThemePaletteCatalog.TokenKeys.First();

        var config = new AppConfig
        {
            ThemePalettes = new Dictionary<string, ThemePaletteConfig>
            {
                ["unknown"] = new() { BaseColors = new() { [validGroup] = "#112233" } },
                ["neptune"] = new()
                {
                    BaseColors = new()
                    {
                        [validGroup] = "#11aa22",
                        ["invalido"] = "#112233",
                        [validGroup + "_bad"] = "red"
                    },
                    TokenOverrides = new()
                    {
                        [validToken] = "#aabbccdd",
                        ["tokenInexistente"] = "#112233"
                    }
                },
                ["slate"] = new()
                {
                    BaseColors = new() { ["invalido"] = "not-a-color" }
                }
            }
        };

        config.Normalize();

        Assert.Single(config.ThemePalettes);
        Assert.True(config.ThemePalettes.TryGetValue("neptune", out var neptune));
        Assert.Equal("#11AA22", neptune!.BaseColors[validGroup]);
        Assert.Equal("#AABBCCDD", neptune.TokenOverrides[validToken]);
    }

    [Fact]
    public void Normalize_higieniza_overlay_blocos_overrides_graficos_e_templates()
    {
        var config = new AppConfig
        {
            Overlay = new OverlayConfig
            {
                RenderMode = "INGAME",
                Width = 50,
                FontSize = 99,
                AccentColor = "invalida",
                BackgroundColor = "#12345678", // exige 6 dígitos RGB
                BorderColor = "#abcdef",
                BackgroundOpacity = double.NaN,
                FontFamily = "Fonte\nComControle",
                ShownSeries = ["  ", "Gpu*", "gpu*"],
                HiddenMetrics = [" ", "CpuTotal", "cputotal"],
                InGameTargets =
                [
                    null!,
                    new OverlayLaunchTargetConfig { ExecutablePath = "  " },
                    new OverlayLaunchTargetConfig { ExecutablePath = " game.exe ", Arguments = " -dx11 " },
                    new OverlayLaunchTargetConfig { ExecutablePath = "game.exe", Arguments = "-dup" }
                ],
                Blocks =
                [
                    null!,
                    new OverlayBlockConfig
                    {
                        Id = " ",
                        X = double.PositiveInfinity,
                        Y = -0.25,
                        Items =
                        [
                            null!,
                            new OverlayBlockItemConfig { Kind = " CATEGORY ", Value = " cpu " },
                            new OverlayBlockItemConfig { Kind = "desconhecido", Value = "x" },
                            new OverlayBlockItemConfig { Kind = "series", Value = "   " }
                        ],
                        MetricOrder = new Dictionary<string, List<string>>
                        {
                            [" category:cpu "] = [" CpuTotal ", "", "cputotal"],
                            ["vazio"] = ["  "]
                        },
                        MetricOverrides =
                        [
                            null!,
                            new OverlayMetricOverrideConfig { Identity = "CpuTotal", Decimals = 9, Color = "bad" }, // sem campos válidos -> removido
                            new OverlayMetricOverrideConfig { Identity = "CpuTotal", DisplayName = "CPU 1", Decimals = 1, Color = "#112233" },
                            new OverlayMetricOverrideConfig { Identity = "cputotal", DisplayName = "CPU Final", Decimals = 2, Color = "#abcdef" }
                        ]
                    }
                ],
                Graphs =
                [
                    null!,
                    new OverlayGraph { Series = "  " },
                    new OverlayGraph { Id = "dup", Series = " CpuTotal ", Height = 2, Window = 5000, Min = 100, Max = 10, Color = "xyz" },
                    new OverlayGraph { Id = "dup", Series = "GpuCore", Height = 50, Window = 120, Min = double.NaN, Max = double.PositiveInfinity, Color = "#112233" }
                ],
                Templates =
                [
                    null!,
                    new OverlayTemplate { Text = "   " },
                    new OverlayTemplate { Id = "t1", Text = " Olá ", Color = "invalida" },
                    new OverlayTemplate { Id = "t1", Text = "Mundo", Color = "#112233" }
                ]
            }
        };

        config.Normalize();

        Assert.Equal("inGame", config.Overlay.RenderMode);
        Assert.Equal(140, config.Overlay.Width);
        Assert.Equal(20, config.Overlay.FontSize);
        Assert.Equal("#1E90FF", config.Overlay.AccentColor);
        Assert.Null(config.Overlay.BackgroundColor);
        Assert.Equal("#abcdef", config.Overlay.BorderColor);
        Assert.Equal(1, config.Overlay.BackgroundOpacity);
        Assert.Equal("", config.Overlay.FontFamily);
        Assert.Equal(["Gpu*"], config.Overlay.ShownSeries);
        Assert.Equal(["CpuTotal"], config.Overlay.HiddenMetrics);
        var target = Assert.Single(config.Overlay.InGameTargets);
        Assert.Equal("game.exe", target.ExecutablePath);
        Assert.Equal("-dx11", target.Arguments);

        var block = Assert.Single(config.Overlay.Blocks);
        Assert.False(string.IsNullOrWhiteSpace(block.Id));
        Assert.Equal(0, block.X);
        Assert.Equal(-0.25, block.Y);
        var item = Assert.Single(block.Items);
        Assert.Equal("category", item.Kind);
        Assert.Equal("cpu", item.Value);
        Assert.NotNull(block.MetricOrder);
        Assert.Equal(["CpuTotal"], block.MetricOrder!["category:cpu"]);
        Assert.False(block.MetricOrder.ContainsKey("vazio"));
        var ov = Assert.Single(block.MetricOverrides);
        Assert.Equal("CPU Final", ov.DisplayName);
        Assert.Equal(2, ov.Decimals);
        Assert.Equal("#abcdef", ov.Color);

        Assert.Equal(2, config.Overlay.Graphs.Count);
        Assert.NotEqual(config.Overlay.Graphs[0].Id, config.Overlay.Graphs[1].Id);
        Assert.Equal("CpuTotal", config.Overlay.Graphs[0].Series);
        Assert.Equal(12, config.Overlay.Graphs[0].Height);
        Assert.Equal(1200, config.Overlay.Graphs[0].Window);
        Assert.Equal(10, config.Overlay.Graphs[0].Min);
        Assert.Equal(100, config.Overlay.Graphs[0].Max);
        Assert.Null(config.Overlay.Graphs[0].Color);
        Assert.Null(config.Overlay.Graphs[1].Min);
        Assert.Null(config.Overlay.Graphs[1].Max);

        Assert.Equal(2, config.Overlay.Templates.Count);
        Assert.NotEqual(config.Overlay.Templates[0].Id, config.Overlay.Templates[1].Id);
        Assert.Equal("Olá", config.Overlay.Templates[0].Text);
        Assert.Null(config.Overlay.Templates[0].Color);
    }

    [Fact]
    public void Normalize_higieniza_alertas_perfis_sensores_customizados_e_tunings()
    {
        var config = new AppConfig
        {
            UpdateIntervalMs = 500,
            Alerts = new AlertsConfig
            {
                CooldownSeconds = -5,
                Rules =
                [
                    null!,
                    new AlertRule { CooldownSeconds = 99999, Script = null!, ScriptArguments = null!, Message = null! }
                ]
            },
            Profiles =
            [
                null!,
                new OverlayProfile
                {
                    Name = "  ",
                    AppMatch = " ..jogo.EXE ",
                    OffsetX = -9999,
                    OffsetY = 9999,
                    Width = 10,
                    FontSize = 2,
                    Opacity = 0.01,
                    ShownSeries = [" ", "Cpu*", "cpu*"],
                    AccentColor = "nao-hex"
                }
            ],
            CustomSensors =
            [
                null!,
                new CustomSensorConfig { Name = "  ", Formula = "1+1" },
                new CustomSensorConfig { Name = "Invalido", Formula = "1+" },
                new CustomSensorConfig { Name = " Delta ", Formula = " {CpuTemperature} - 10 ", Unit = " C " }
            ],
            SensorTunings =
            [
                null!,
                new SensorTuningConfig { Series = "  " },
                new SensorTuningConfig { Series = " CpuTemperature ", IntervalMs = 100, Smoothing = 999 }
            ]
        };

        config.Normalize();

        Assert.Equal(1, config.Alerts.CooldownSeconds);
        var rule = Assert.Single(config.Alerts.Rules);
        Assert.Equal(3600, rule.CooldownSeconds);
        Assert.Equal("", rule.Script);
        Assert.Equal("", rule.ScriptArguments);
        Assert.Equal("", rule.Message);

        var profile = Assert.Single(config.Profiles);
        Assert.Equal("Perfil 1", profile.Name);
        Assert.Equal("jogo", profile.AppMatch);
        Assert.Equal(-5000, profile.OffsetX);
        Assert.Equal(5000, profile.OffsetY);
        Assert.Equal(140, profile.Width);
        Assert.Equal(8, profile.FontSize);
        Assert.Equal(0.1, profile.Opacity);
        Assert.Equal(["Cpu*"], profile.ShownSeries);
        Assert.Null(profile.AccentColor);

        var custom = Assert.Single(config.CustomSensors);
        Assert.Equal("Delta", custom.Name);
        Assert.Equal("{CpuTemperature} - 10", custom.Formula);
        Assert.Equal("C", custom.Unit);

        var tuning = Assert.Single(config.SensorTunings);
        Assert.Equal("CpuTemperature", tuning.Series);
        Assert.Equal(500, tuning.IntervalMs); // nunca menor que UpdateIntervalMs
        Assert.Equal(SensorTuningEngine.MaxSmoothing, tuning.Smoothing);
    }

    [Fact]
    public void GenerateAccessToken_gera_tokens_url_safe_distintos_de_256_bits()
    {
        var token1 = TelemetryExportConfig.GenerateAccessToken();
        var token2 = TelemetryExportConfig.GenerateAccessToken();

        Assert.NotEqual(token1, token2);
        Assert.Equal(43, token1.Length); // 32 bytes em base64 sem '='
        Assert.DoesNotContain("+", token1);
        Assert.DoesNotContain("/", token1);
        Assert.DoesNotContain("=", token1);
    }
}
