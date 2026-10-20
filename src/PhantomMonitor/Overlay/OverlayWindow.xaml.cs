using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using PhantomMonitor.Models;
using PhantomMonitor.Services;

namespace PhantomMonitor.Overlay;

public partial class OverlayWindow : Window
{
    private readonly List<Action<SensorData>> _updaters = new();
    private Canvas? _frametimeGraph;
    private bool _showFps;
    private double _detailMaxWidth = 160;

    // Séries por instância (multi-GPU, núcleos, ventoinhas, discos...): as linhas
    // são criadas no primeiro snapshot que as contiver, filtradas pela whitelist
    // overlay.shownSeries e limitadas para não explodir o OSD em máquinas cheias.
    private static readonly int MaxAdvancedRows = 24;
    private List<string> _seriesPatterns;
    private List<string>? _baseSeriesPatterns;
    private readonly Dictionary<SensorKey, Grid> _dynamicRows = [];

    // Aparência configurável (compactMode / fontSize / accentColor)
    private double _baseFontSize = 11.5;
    private double _rowMargin = 1.5;
    private double _graphHeight = 24;
    private Brush _accentBrush = new SolidColorBrush(Color.FromRgb(0x00, 0xF0, 0xFF));

    private static readonly FontFamily OsdFont = new("Consolas, Cascadia Mono, Lucida Console, Segoe UI");
    private static readonly Brush NormalBrush = new SolidColorBrush(Color.FromRgb(0xF1, 0xF5, 0xF9));
    private static readonly Brush WarmBrush = new SolidColorBrush(Color.FromRgb(0xFA, 0xCC, 0x15)); // Amarelo
    private static readonly Brush HotBrush = new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44));  // Vermelho
    private static readonly Brush MutedBrush = new SolidColorBrush(Color.FromRgb(0x8E, 0x9A, 0xA8));
    private static readonly Brush ColonBrush = new SolidColorBrush(Color.FromArgb(90, 255, 255, 255));
    private static readonly Brush GreenFpsBrush = new SolidColorBrush(Color.FromRgb(0x00, 0xFF, 0x88));
    private static readonly Brush GuideBrush = MakeFrozenBrush(30, 255, 255, 255);

    public OverlayWindow(AppConfig config, double left = 20, double top = 80, Func<OverlayProfileState?>? profileState = null)
    {
        InitializeComponent();
        _profileState = profileState;
        Left = left;
        Top = top;
        Opacity = config.OverlayOpacity;
        if (config.Overlay.Width > 0)
            MainBorder.MinWidth = config.Overlay.Width;

        _detailMaxWidth = Math.Max(120, (config.Overlay.Width > 0 ? config.Overlay.Width : 220) - 62);
        _baseWidth = config.Overlay.Width > 0 ? config.Overlay.Width : 220;

        _baseFontSize = Math.Clamp(config.Overlay.FontSize, 8, 20);
        var accent = ParseAccentColor(config.Overlay.AccentColor);
        _accentBrush = new SolidColorBrush(accent);
        MainBorder.BorderBrush = new SolidColorBrush(Color.FromArgb(0x30, accent.R, accent.G, accent.B));

        if (config.Overlay.CompactMode)
        {
            MainBorder.Padding = new Thickness(6, 4, 6, 4);
            _rowMargin = 0.5;
            _graphHeight = 18;
        }

        MakeClickThrough();
        StartTopmostTimer();

        // GPU
        if (config.Overlay.ShowGpu)
        {
            AddMetricRow("GPU", Color.FromRgb(0xFF, 0x8A, 0x00), data =>
            {
                string tempStr = data.GpuTemp > 0 ? $"{data.GpuTemp:F0}°C" : "--°C";
                string usageStr = $"{data.GpuUsage:F1}%";
                return (tempStr, GetTempBrush(data.GpuTemp), usageStr, GetUsageBrush(data.GpuUsage));
            });
            AddDetailLine(SensorFormatter.BuildGpuDetail);
        }

        // CPU
        if (config.Overlay.ShowCpu)
        {
            AddMetricRow("CPU", Color.FromRgb(0x00, 0xD2, 0xFF), data =>
            {
                string tempStr = data.CpuTemp > 0 ? $"{data.CpuTemp:F0}°C" : "--°C";
                string usageStr = $"{data.CpuUsage:F1}%";
                return (tempStr, GetTempBrush(data.CpuTemp), usageStr, GetUsageBrush(data.CpuUsage));
            });
            AddDetailLine(SensorFormatter.BuildCpuDetail);
        }

        // RAM
        if (config.Overlay.ShowRam)
        {
            AddMetricRow("RAM", Color.FromRgb(0xC0, 0x84, 0xFC), data =>
            {
                double total = data.GetValue(SensorType.RamTotal);
                double avail = data.RamAvailableGB;
                string usedStr;
                if (total > 0 && avail > 0 && total >= avail)
                {
                    double used = total - avail;
                    usedStr = $"{used:F1} GB";
                }
                else
                {
                    usedStr = $"{data.RamUsagePercent:F0}%";
                }

                string pctStr = $"{data.RamUsagePercent:F0}%";
                return (usedStr, NormalBrush, pctStr, GetUsageBrush(data.RamUsagePercent));
            });
        }

        // DISCO
        if (config.Overlay.ShowDisk)
        {
            AddMetricRow("DSK", Color.FromRgb(0x60, 0xA5, 0xFA), data =>
            {
                string readStr = "R: " + SensorFormatter.FormatBytesPerSec(data.GetValue(SensorType.DiskRead));
                string writeStr = "W: " + SensorFormatter.FormatBytesPerSec(data.GetValue(SensorType.DiskWrite));
                return (readStr, MutedBrush, writeStr, MutedBrush);
            });
        }

        // REDE (linha oculta até o primeiro dado real)
        if (config.Overlay.ShowNetwork)
        {
            AddMetricRow("NET", Color.FromRgb(0x34, 0xD3, 0x99), data =>
            {
                string downStr = "↓ " + SensorFormatter.FormatBytesPerSec(data.GetValue(SensorType.NetworkDown));
                string upStr = "↑ " + SensorFormatter.FormatBytesPerSec(data.GetValue(SensorType.NetworkUp));
                return (downStr, MutedBrush, upStr, MutedBrush);
            }, data => data.GetValue(SensorType.NetworkDown) > 0 || data.GetValue(SensorType.NetworkUp) > 0);
        }

        // VENTOINHAS (linha oculta até o primeiro dado real)
        if (config.Overlay.ShowFans)
        {
            AddMetricRow("FAN", Color.FromRgb(0xF4, 0x72, 0xB6), data =>
            {
                string gpuStr = SensorFormatter.FormatFan(data.GetValue(SensorType.GpuFan));
                string sysStr = SensorFormatter.FormatFan(data.GetValue(SensorType.FanSpeed));
                return (gpuStr, MutedBrush, sysStr, MutedBrush);
            }, data => data.GetValue(SensorType.GpuFan) > 0 || data.GetValue(SensorType.FanSpeed) > 0);
        }

        // PLACA-MÃE (linha oculta até o primeiro dado real)
        if (config.Overlay.ShowMotherboard)
        {
            AddMetricRow("MB", Color.FromRgb(0xCB, 0xD5, 0xE1), data =>
            {
                double temp = data.GetValue(SensorType.MotherboardTemp);
                string tempStr = temp > 0 ? $"{temp:F0}°C" : "--°C";
                return (tempStr, temp > 0 ? GetTempBrush(temp) : MutedBrush, "", NormalBrush);
            }, data => data.GetValue(SensorType.MotherboardTemp) > 0);
        }

        // FPS
        _showFps = config.Overlay.ShowFps;
        if (_showFps)
        {
            AddFpsSection(config.Overlay.ShowFrametimeGraph);
        }

        _offsetX = left;
        _offsetY = top;
        _baseOffsetX = left;
        _baseOffsetY = top;
        _baseSeriesPatterns = [.. (config.Overlay.ShownSeries ?? ["Gpu*", "FanSpeed*", "Disk*"])];
        _seriesPatterns = [.. _baseSeriesPatterns];

        // Templates do usuário (P6.6): compilam uma vez por overlay (recriado quando
        // o config muda); linhas entram num container próprio no FIM do painel.
        foreach (var template in config.Overlay.Templates)
        {
            var segments = CompileTemplate(template.Text);
            if (segments.Count == 0) continue;
            var color = TryParseTemplateColor(template.Color);
            _compiledTemplates.Add((segments, color));
        }

        // Gráficos por série (P8.5): compile como placeholder de template única —
        // a resolução (canônica → 1ª instância, wildcard, nome amigável) é a mesma
        // das linhas de texto. Buffer circular por gráfico (window) + eixo fixo opcional.
        foreach (var graph in config.Overlay.Graphs)
        {
            var segments = CompileTemplate($"{{{graph.Series}}}");
            var segment = segments.FirstOrDefault(s => !s.IsLiteral);
            if (segment is null) continue;

            var canvas = new Canvas
            {
                Height = graph.Height,
                Background = new SolidColorBrush(Color.FromArgb(25, 0, 0, 0)),
                ClipToBounds = true
            };
            var border = new Border
            {
                CornerRadius = new CornerRadius(2),
                BorderThickness = new Thickness(0.5),
                BorderBrush = GuideBrush,
                Child = canvas,
                Margin = new Thickness(0, _rowMargin, 0, 0)
            };

            _graphRows.Add(new GraphRow(
                new FrametimeBuffer(graph.Window),
                segment,
                border,
                canvas,
                TryParseTemplateColor(graph.Color) ?? _accentBrush,
                graph.Min,
                graph.Max));
        }

        _baseOpacity = Math.Clamp(config.OverlayOpacity <= 0 ? 0.85 : config.OverlayOpacity, 0.1, 1.0);
        Opacity = _baseOpacity;
        _positionTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(30) };
        _positionTimer.Tick += (s, e) => UpdatePosition();
        _positionTimer.Start();
    }

    private readonly List<(List<TemplateSegment> Segments, Brush Color)> _compiledTemplates = [];
    private StackPanel? _templatesPanel;

    // Gráficos por série (P8.5): sparklines com buffer circular próprio; o container
    // é criado no primeiro snapshot e fica sempre imediatamente ANTES do bloco de
    // templates (ambos "sempre no fim" — ver MovePanelsToEnd).
    private readonly List<GraphRow> _graphRows = [];
    private StackPanel? _graphsPanel;

    /// <summary>Estado de um gráfico por série compilado no construtor.</summary>
    private sealed class GraphRow
    {
        public GraphRow(FrametimeBuffer buffer, TemplateSegment segment, Border border, Canvas canvas, Brush stroke, double? min, double? max)
        {
            Buffer = buffer;
            Segment = segment;
            Border = border;
            Canvas = canvas;
            Stroke = stroke;
            MinAxis = min;
            MaxAxis = max;
        }

        public FrametimeBuffer Buffer { get; }
        public TemplateSegment Segment { get; }
        public Border Border { get; }
        public Canvas Canvas { get; }
        public Brush Stroke { get; }
        public double? MinAxis { get; }    // eixo Y fixo (null = auto)
        public double? MaxAxis { get; }
    }

    private double _offsetX;
    private double _offsetY;
    private int _baseWidth = 220;
    private double _baseOffsetX;
    private double _baseOffsetY;
    private double _baseOpacity = 0.85;

    // Perfis por aplicativo: cascata AppMatch do processo em foco (ou fixado pela
    // bandeja). Fonte de verdade continua sendo o config global; o perfil só faz
    // override dos campos que definiu.
    private readonly Func<OverlayProfileState?>? _profileState;
    private OverlayProfile? _activeProfile;
    private string? _pinnedProfile;
    private TextBlock? _profileTag;
    private bool _isProfileHidden;
    private readonly Dictionary<int, string> _foregroundNames = [];

    // Timers guardados no campo e parados no OnClosed: como variáveis locais eles
    // sobreviviam ao fechamento da janela (foreground check a cada 30 ms e SetWindowPos
    // a cada 500 ms em handle destruído), acumulando a cada recriação do overlay.
    private System.Windows.Threading.DispatcherTimer? _positionTimer;
    private System.Windows.Threading.DispatcherTimer? _topmostTimer;

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    private void UpdatePosition()
    {
        var hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return;

        GetWindowThreadProcessId(hwnd, out uint pid);
        if (pid == (uint)System.Diagnostics.Process.GetCurrentProcess().Id) return;

        if (pid == GetExplorerPid()) return;

        // Perfil por aplicativo: resolve o processo em foco e aplica overrides (offset,
        // largura, acento, fonte). Executa a cada 30 ms, mas o trabalho real só ocorre
        // quando o perfil ativo muda.
        RefreshProfileFor(pid);

        if (GetWindowRect(hwnd, out var rect))
        {
            if (rect.Left <= -32000) return;

            // GetWindowRect devolve PIXELS FÍSICOS; Left/Top do Window (WPF) são DIPs.
            // Sem a conversão, no escalonamento de 125/150% o overlay derivava
            // proporcionalmente ao fator (rect px eram interpretados como DIPs).
            var source = PresentationSource.FromVisual(this);
            var dpiScale = source?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;

            Left = rect.Left / dpiScale + _offsetX;
            Top = rect.Top / dpiScale + _offsetY;
        }
    }

    // ---------- Perfis por aplicativo ----------

    private void RefreshProfileFor(uint pid)
    {
        if (_profileState == null) return;

        OverlayProfileState? state = null;
        try { state = _profileState(); }
        catch { /* config em recriação: mantém o perfil atual */ }
        if (state == null) return;

        OverlayProfile? target;
        if (state.Pinned is { Length: > 0 } pinned)
        {
            // Fixado por bandeja: aplica mesmo em outro app, até o usuário desfixar.
            target = state.Profiles.FirstOrDefault(p =>
                string.Equals(p.Name, pinned, StringComparison.OrdinalIgnoreCase));
        }
        else
        {
            var processName = ResolveForegroundName(pid);
            target = state.Profiles.FirstOrDefault(p => MatchesApp(p.AppMatch, processName));
        }

        if (string.Equals(_activeProfile?.Name, target?.Name, StringComparison.OrdinalIgnoreCase) &&
            _pinnedProfile == state.Pinned)
            return;

        _pinnedProfile = state.Pinned;
        _activeProfile = target;
        ApplyProfile(target);
        UpdateProfileTag();
    }

    private string? ResolveForegroundName(uint pid)
    {
        var key = (int)pid;
        if (_foregroundNames.TryGetValue(key, out var cached)) return cached;

        string name = "";
        try
        {
            using var process = System.Diagnostics.Process.GetProcessById(key);
            name = process.ProcessName;
        }
        catch
        {
            // Processo saiu entre GetForegroundWindow e a busca: sem match neste tick.
        }

        // Cache limitado (processos mudam bem menos que os ticks de 30 ms).
        if (_foregroundNames.Count > 64) _foregroundNames.Clear();
        _foregroundNames[key] = name;
        return name;
    }

    /// <summary>Case-insensitive sem caminho/“— .exe”; "" ou "*" casa com qualquer app.</summary>
    private static bool MatchesApp(string appMatch, string? processName)
    {
        var pattern = appMatch?.Trim();
        if (string.IsNullOrEmpty(pattern) || pattern == "*") return true;
        if (string.IsNullOrEmpty(processName)) return false;

        if (processName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            processName = processName[..^4];

        return processName.StartsWith(pattern, StringComparison.OrdinalIgnoreCase);
    }

    private void ApplyProfile(OverlayProfile? profile)
    {
        // Offsets: perfil > padrão capturado no construtor.
        _offsetX = profile?.OffsetX ?? _baseOffsetX;
        _offsetY = profile?.OffsetY ?? _baseOffsetY;

        // Visibilidade: um perfil pode esconder o OSD por completo enquanto casa
        // (jogo que marca como "sem overlay"). Volta ao abrir outro app / desfixar.
        var hidden = profile?.Visible == false;
        if (_isProfileHidden != hidden)
        {
            _isProfileHidden = hidden;
            MainBorder.Visibility = hidden ? Visibility.Collapsed : Visibility.Visible;
        }

        // Largura/redondagem do painel de detalhes.
        var width = profile?.Width ?? _baseWidth;
        if (width is int w && w > 0)
        {
            MainBorder.MinWidth = w;
            _detailMaxWidth = Math.Max(120, w - 62);
        }

        // Opacidade do painel (vazio = global).
        Opacity = profile?.Opacity is { } op ? Math.Clamp(op, 0.1, 1.0) : _baseOpacity;

        // Whitelist de séries por instância: perfil > global. Linhas que deixam de
        // pertencer à whitelist vigente são escondidas (sem quebrar as existentes:
        // reaparecem se a whitelist voltar a contê-las).
        if (profile?.ShownSeries is { Count: > 0 } series)
            _seriesPatterns = [.. series];
        else
            _seriesPatterns = [.. _baseSeriesPatterns ?? ["Gpu*", "FanSpeed*", "Disk*"]];

        // Acento: substitui a escova do gráfico e o contorno do painel.
        if (!string.IsNullOrWhiteSpace(profile?.AccentColor))
        {
            var accent = ParseAccentColor(profile.AccentColor);
            _accentBrush = new SolidColorBrush(accent);
            MainBorder.BorderBrush = new SolidColorBrush(Color.FromArgb(0x30, accent.R, accent.G, accent.B));
        }

        // Fonte: re-escala a árvore existente (os deltas por tipo de bloco são preservados).
        if (profile?.FontSize is double font)
        {
            var old = _baseFontSize;
            var target = Math.Clamp(font, 8, 20);
            if (Math.Abs(old - target) > 0.05)
            {
                RescaleFont(MainBorder, old, target);
                _baseFontSize = target;
                _rowMargin = Math.Max(0.5, 1.5 - (target - 11.5) * 0.1);
            }
        }
    }

    /// <summary>Reescala TextBlocks da árvore preservando os deltas relativos (-2 dados, +0.5 FPS).</summary>
    private static void RescaleFont(System.Windows.DependencyObject root, double oldBase, double newBase)
    {
        foreach (var text in FindTextBlocks(root))
        {
            var size = text.FontSize;
            if (Math.Abs(size - oldBase) < 0.05) text.FontSize = newBase;
            else if (Math.Abs(size - (oldBase - 2)) < 0.05) text.FontSize = newBase - 2;
            else if (Math.Abs(size - (oldBase + 0.5)) < 0.05) text.FontSize = newBase + 0.5;
        }
    }

    private static IEnumerable<TextBlock> FindTextBlocks(System.Windows.DependencyObject root)
    {
        int count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
            if (child is TextBlock block) yield return block;
            foreach (var inner in FindTextBlocks(child)) yield return inner;
        }
    }

    private void UpdateProfileTag()
    {
        var name = _activeProfile?.Name;
        if (string.IsNullOrWhiteSpace(name))
        {
            if (_profileTag != null) _profileTag.Visibility = Visibility.Collapsed;
            return;
        }

        if (_profileTag == null)
        {
            _profileTag = new TextBlock
            {
                FontFamily = OsdFont,
                FontSize = _baseFontSize - 2.5,
                Foreground = MutedBrush,
                Margin = new Thickness(46, 1, 0, 0)
            };
            SensorPanel.Children.Add(_profileTag);
        }

        _profileTag.Text = $"◉ perfil: {_activeProfile!.Name}";
        _profileTag.FontSize = _baseFontSize - 2.5;
        _profileTag.Visibility = Visibility.Visible;
    }

    private static uint _explorerPid = 0;
    private static DateTime _explorerPidCheckedAt = DateTime.MinValue;

    private static uint GetExplorerPid()
    {
        if (_explorerPid != 0 && DateTime.UtcNow - _explorerPidCheckedAt < TimeSpan.FromSeconds(10))
            return _explorerPid;

        _explorerPidCheckedAt = DateTime.UtcNow;

        if (_explorerPid != 0)
        {
            // O explorer pode reiniciar e o PID em cache deixa de existir → re-resolve.
            try
            {
                using var p = System.Diagnostics.Process.GetProcessById((int)_explorerPid);
                if (!p.HasExited) return _explorerPid;
            }
            catch
            {
                // PID inválido/saído: cai no re-resolve abaixo.
            }
            _explorerPid = 0;
        }

        var procs = System.Diagnostics.Process.GetProcessesByName("explorer");
        if (procs.Length > 0)
        {
            using var first = procs[0];
            _explorerPid = (uint)first.Id;
        }
        foreach (var p in procs) p.Dispose();
        return _explorerPid;
    }

    private void AddMetricRow(
        string tag,
        Color tagColor,
        Func<SensorData, (string val1, Brush col1, string val2, Brush col2)> dataProvider,
        Func<SensorData, bool>? revealWhen = null)
    {
        var grid = new Grid
        {
            Margin = new Thickness(0, _rowMargin, 0, _rowMargin)
        };

        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(38) }); // Tag
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });  // :
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) }); // Métrica 1
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // Métrica 2

        var tagBlock = new TextBlock
        {
            Text = tag,
            FontFamily = OsdFont,
            FontSize = _baseFontSize,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(tagColor),
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(tagBlock, 0);
        grid.Children.Add(tagBlock);

        var colonBlock = new TextBlock
        {
            Text = ":",
            FontFamily = OsdFont,
            FontSize = _baseFontSize,
            Foreground = ColonBrush,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        Grid.SetColumn(colonBlock, 1);
        grid.Children.Add(colonBlock);

        var val1Block = new TextBlock
        {
            FontFamily = OsdFont,
            FontSize = _baseFontSize,
            FontWeight = FontWeights.SemiBold,
            Foreground = NormalBrush,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Left,
            Text = "--"
        };
        Grid.SetColumn(val1Block, 2);
        grid.Children.Add(val1Block);

        var val2Block = new TextBlock
        {
            FontFamily = OsdFont,
            FontSize = _baseFontSize,
            FontWeight = FontWeights.SemiBold,
            Foreground = NormalBrush,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right,
            Text = "--"
        };
        Grid.SetColumn(val2Block, 3);
        grid.Children.Add(val2Block);

        bool revealed = revealWhen == null;

        _updaters.Add(data =>
        {
            if (!revealed && revealWhen!(data))
            {
                revealed = true;
                grid.Visibility = Visibility.Visible;
            }

            var (v1, c1, v2, c2) = dataProvider(data);
            val1Block.Text = v1;
            val1Block.Foreground = c1 ?? NormalBrush;
            val2Block.Text = v2;
            val2Block.Foreground = c2 ?? NormalBrush;
        });

        if (!revealed) grid.Visibility = Visibility.Collapsed;
        SensorPanel.Children.Add(grid);
    }

    /// <summary>
    /// Linha secundária compacta sob uma métrica (ex.: clock/potência/voltagem).
    /// Fica oculta enquanto o provedor não retornar dados.
    /// </summary>
    private void AddDetailLine(Func<SensorData, string?> provider)
    {
        var block = new TextBlock
        {
            FontFamily = OsdFont,
            FontSize = _baseFontSize - 2,
            Foreground = MutedBrush,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = _detailMaxWidth,
            Margin = new Thickness(46, 0, 0, 2),
            Text = ""
        };

        _updaters.Add(data =>
        {
            var text = provider(data);
            block.Text = text ?? "";
            block.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
        });

        SensorPanel.Children.Add(block);
    }

    private void AddFpsSection(bool showFrametimeGraph = true)
    {
        // Linha Principal de FPS
        var grid = new Grid
        {
            Margin = new Thickness(0, _rowMargin, 0, 1)
        };

        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(38) }); // FPS
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });  // :
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) }); // FPS valor
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // Frametime ms

        var tagBlock = new TextBlock
        {
            Text = "FPS",
            FontFamily = OsdFont,
            FontSize = _baseFontSize,
            FontWeight = FontWeights.Bold,
            Foreground = GreenFpsBrush,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(tagBlock, 0);
        grid.Children.Add(tagBlock);

        var colonBlock = new TextBlock
        {
            Text = ":",
            FontFamily = OsdFont,
            FontSize = _baseFontSize,
            Foreground = ColonBrush,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        Grid.SetColumn(colonBlock, 1);
        grid.Children.Add(colonBlock);

        var fpsBlock = new TextBlock
        {
            FontFamily = OsdFont,
            FontSize = _baseFontSize + 0.5,
            FontWeight = FontWeights.Bold,
            Foreground = GreenFpsBrush,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Left,
            Text = "-- FPS"
        };
        Grid.SetColumn(fpsBlock, 2);
        grid.Children.Add(fpsBlock);

        var ftBlock = new TextBlock
        {
            FontFamily = OsdFont,
            FontSize = _baseFontSize,
            FontWeight = FontWeights.SemiBold,
            Foreground = WarmBrush,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right,
            Text = "-- ms"
        };
        Grid.SetColumn(ftBlock, 3);
        grid.Children.Add(ftBlock);

        SensorPanel.Children.Add(grid);

        // Sublinha: AVG / 1% Low
        var statsGrid = new Grid
        {
            Margin = new Thickness(0, 0, 0, 2)
        };
        statsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(46) }); // Espaço sob a tag
        statsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var statsBlock = new TextBlock
        {
            FontFamily = OsdFont,
            FontSize = _baseFontSize - 2,
            Foreground = MutedBrush,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right,
            Text = "AVG: --  1%: --  0.1%: --  MAX: --",
            TextWrapping = TextWrapping.Wrap
        };
        Grid.SetColumn(statsBlock, 1);
        statsGrid.Children.Add(statsBlock);

        SensorPanel.Children.Add(statsGrid);

        if (showFrametimeGraph)
        {
            // Gráfico de Frametime compacto de barras suaves
            _frametimeGraph = new Canvas
            {
                Height = _graphHeight,
                Background = new SolidColorBrush(Color.FromArgb(25, 0, 0, 0)),
                ClipToBounds = true
            };

            var graphBorder = new Border
            {
                CornerRadius = new CornerRadius(2),
                BorderThickness = new Thickness(0.5),
                BorderBrush = GuideBrush,
                Child = _frametimeGraph,
                Margin = new Thickness(0, 2, 0, 0)
            };

            SensorPanel.Children.Add(graphBorder);
        }

        _updaters.Add(data =>
        {
            fpsBlock.Text = data.Fps > 0 ? $"{data.Fps:F0} FPS" : "-- FPS";
            ftBlock.Text = data.FrametimeMs > 0 ? $"{data.FrametimeMs:F1} ms" : "-- ms";

            string avg = data.FpsAvg > 0 ? $"{data.FpsAvg:F0}" : "--";
            string low = data.Fps1Low > 0 ? $"{data.Fps1Low:F0}" : "--";
            string low01 = data.Fps01Low > 0 ? $"{data.Fps01Low:F0}" : "--";
            string max = data.FpsMax > 0 ? $"{data.FpsMax:F0}" : "--";
            statsBlock.Text = $"AVG: {avg}  1%: {low}  0.1%: {low01}  MAX: {max}";
        });
    }

    public void Update(SensorData data)
    {
        EnsureDynamicRows(data);
        EnsureGraphRows(data);
        EnsureTemplateRows(data); // P6.6: linhas personalizadas por último no painel

        foreach (var updater in _updaters)
        {
            updater(data);
        }

        // Gráficos por série (P8.5): resolvem a série como as templates e redesenham.
        foreach (var row in _graphRows)
        {
            UpdateSeriesGraph(row, data);
        }

        // Atualizar o gráfico de frametime
        if (_frametimeGraph != null && data.FrametimeHistory.Length > 1)
        {
            _frametimeGraph.Children.Clear();
            double w = _frametimeGraph.ActualWidth;
            double h = _frametimeGraph.ActualHeight;
            if (w <= 0 || h <= 0) { w = _frametimeGraph.Width; h = _frametimeGraph.Height; }
            if (w <= 0) w = 184;
            if (h <= 0) h = _graphHeight;

            var history = data.FrametimeHistory;
            double maxFt = 33.3; // 30 FPS baseline
            for (int i = 0; i < history.Length; i++)
            {
                if (history[i] > maxFt) maxFt = history[i];
            }
            if (maxFt < 16.6) maxFt = 16.6;

            // Linha guia sutil de 60 FPS (16.6ms)
            double y60 = h - (16.66 / maxFt) * (h - 2);
            if (y60 >= 0 && y60 <= h)
            {
                var guide = new Line
                {
                    X1 = 0,
                    X2 = w,
                    Y1 = y60,
                    Y2 = y60,
                    Stroke = GuideBrush,
                    StrokeThickness = 0.8,
                    StrokeDashArray = new DoubleCollection { 2, 2 }
                };
                _frametimeGraph.Children.Add(guide);
            }

            var pts = new PointCollection();
            int count = history.Length;
            for (int i = 0; i < count; i++)
            {
                double x = w - (count - i) * (w / Math.Max(count, 120));
                double y = h - (history[i] / maxFt) * (h - 2);
                pts.Add(new Point(x, Math.Clamp(y, 0, h)));
            }

            var poly = new Polyline
            {
                Points = pts,
                Stroke = _accentBrush,
                StrokeThickness = 1.2,
                StrokeLineJoin = PenLineJoin.Round
            };
            _frametimeGraph.Children.Add(poly);
        }
    }

    // ---------- Séries dinâmicas por instância ----------

    /// <summary>
    /// Cria as linhas por instância (GPU 1..n, ventoinhas, discos...) no primeiro
    /// snapshot em que aparecerem. Linhas novas vão para o fim do painel, depois
    /// do bloco de FPS.
    /// </summary>
    private void EnsureDynamicRows(SensorData data)
    {
        // Padrões do config conferidos por série; ordenação por família evita que um
        // hardware abundante (núcleos de CPU) capture o teto de linhas primeiro.
        var keys = data.Series.Keys
            .Where(k => _seriesPatterns.Any(p => k.MatchesPattern(p)))
            .ToList();
        keys.Sort((a, b) =>
        {
            var byFamily = FamilyOrder(a.Type).CompareTo(FamilyOrder(b.Type));
            return byFamily != 0 ? byFamily : a.CompareTo(b);
        });

        foreach (var key in keys)
        {
            if (_dynamicRows.Count >= MaxAdvancedRows) break;
            if (_dynamicRows.ContainsKey(key)) continue;
            AddAdvancedRow(key, data);

            // Containers do fim (gráficos P8.5 + templates P6.6) permanecem os
            // últimos filhos do painel: séries que aparecem depois não "passam
            // por baixo" desses blocos.
            MovePanelsToEnd();
        }
    }

    /// <summary>
    /// Mantém os containers de fim do painel (gráficos por série, templates) sempre
    /// nas duas últimas posições — após as linhas dinâmicas recém-criadas.
    /// </summary>
    private void MovePanelsToEnd()
    {
        if (_graphsPanel is { } graphs && SensorPanel.Children.Contains(graphs))
        {
            SensorPanel.Children.Remove(graphs);
            SensorPanel.Children.Add(graphs);
        }
        if (_templatesPanel is { } templates && SensorPanel.Children.Contains(templates))
        {
            SensorPanel.Children.Remove(templates);
            SensorPanel.Children.Add(templates);
        }
    }

    private void AddAdvancedRow(SensorKey key, SensorData data)
    {
        var grid = new Grid { Margin = new Thickness(0, _rowMargin, 0, _rowMargin) };

        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(38) });  // Tag
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });   // :
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });  // Valor
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // Instância

        var tagBlock = new TextBlock
        {
            Text = TagFor(key),
            FontFamily = OsdFont,
            FontSize = _baseFontSize,
            FontWeight = FontWeights.Bold,
            Foreground = PinFrozenBrush(TagColorFor(key.Type)),
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(tagBlock, 0);
        grid.Children.Add(tagBlock);

        var colonBlock = new TextBlock
        {
            Text = ":",
            FontFamily = OsdFont,
            FontSize = _baseFontSize,
            Foreground = ColonBrush,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        Grid.SetColumn(colonBlock, 1);
        grid.Children.Add(colonBlock);

        var valueBlock = new TextBlock
        {
            FontFamily = OsdFont,
            FontSize = _baseFontSize,
            FontWeight = FontWeights.SemiBold,
            Foreground = NormalBrush,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Left
        };
        Grid.SetColumn(valueBlock, 2);
        grid.Children.Add(valueBlock);

        var labelBlock = new TextBlock
        {
            FontFamily = OsdFont,
            FontSize = _baseFontSize - 2,
            Foreground = MutedBrush,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        Grid.SetColumn(labelBlock, 3);
        grid.Children.Add(labelBlock);

        labelBlock.Text = data.GetLabel(key);
        valueBlock.Text = SensorFormatter.FormatSeries(key, data.GetValue(key));

        var gridRef = grid;

        // O updater refresca valor e rótulo: nomes de hardware podem mudar quando o
        // driver enumera de novo (ex.: troca de nome de NIC). Série ausente — ou que
        // deixou de pertencer à whitelist vigente (perfil aplicado) — esconde a linha.
        _updaters.Add(d =>
        {
            if (!d.Series.ContainsKey(key) || !_seriesPatterns.Any(p => key.MatchesPattern(p)))
            {
                gridRef.Visibility = Visibility.Collapsed;
                return;
            }

            gridRef.Visibility = Visibility.Visible;
            valueBlock.Text = SensorFormatter.FormatSeries(key, d.GetValue(key));
            labelBlock.Text = d.GetLabel(key);
        });

        _dynamicRows[key] = grid;
        SensorPanel.Children.Add(grid);
    }

    private static Brush PinFrozenBrush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    // ---------- Gráficos de histórico por série (P8.5) ----------

    /// <summary>
    /// Container dos sparklines criado no primeiro snapshot com layout resolvido,
    /// inserido imediatamente antes do bloco de templates (MovePanelsToEnd o mantém
    /// após as linhas dinâmicas entre cada ciclo).
    /// </summary>
    private void EnsureGraphRows(SensorData data)
    {
        if (_graphsPanel != null || _graphRows.Count == 0) return;
        _ = data;

        _graphsPanel = new StackPanel();
        foreach (var row in _graphRows)
        {
            _graphsPanel.Children.Add(row.Border);
        }
        InsertGraphsPanel(_graphsPanel);
    }

    /// <summary>Insere o container antes do bloco de templates (ou no fim, enquanto a templates ainda não existe).</summary>
    private void InsertGraphsPanel(StackPanel graphs)
    {
        if (_templatesPanel is { } templates && SensorPanel.Children.Contains(templates))
        {
            SensorPanel.Children.Insert(SensorPanel.Children.IndexOf(templates), graphs);
        }
        else
        {
            SensorPanel.Children.Add(graphs);
        }
    }

    /// <summary>Resolve a série e empurra a amostra pro buffer + redesenha (sem dado → linha escondida).</summary>
    private void UpdateSeriesGraph(GraphRow row, SensorData data)
    {
        if (!TryResolveTemplateValue(data, row.Segment, out var value))
        {
            row.Border.Visibility = Visibility.Collapsed; // série ausente esconde a sparkline (buffer fica)
            return;
        }

        row.Border.Visibility = Visibility.Visible;
        row.Buffer.Add((float)value);
        DrawSeriesGraph(row);
    }

    /// <summary>
    /// Desenho da sparkline (espelha o gráfico de frametime): polyline com espaçamento
    /// uniforme, eixo Y fixo quando configurado (min/max) senão auto pela janela (com
    /// piso de ε para séries "planas"), guias tracejadas nos extremos do eixo.
    /// </summary>
    private void DrawSeriesGraph(GraphRow row)
    {
        var canvas = row.Canvas;
        canvas.Children.Clear();

        double w = canvas.ActualWidth;
        double h = canvas.ActualHeight;
        if (w <= 0 || h <= 0) { w = canvas.Width; h = canvas.Height; }
        if (w <= 0) w = _baseWidth - 24;
        if (h <= 0) h = 36;

        var history = row.Buffer.ToArray();
        if (history.Length < 2) return;

        // Eixo: fixo quando configurado; senão auto (mín/máx da janela). Séries
        // "planas" (todos os valores iguais) ganham piso simétrico de ±0.5.
        double lo = row.MinAxis ?? double.MaxValue;
        double hi = row.MaxAxis ?? double.MinValue;
        if (row.MinAxis is null || row.MaxAxis is null)
        {
            foreach (var v in history)
            {
                if (row.MinAxis is null && v < lo) lo = v;
                if (row.MaxAxis is null && v > hi) hi = v;
            }
            if (hi - lo < 0.001)
            {
                lo -= 0.5;
                hi += 0.5;
            }
        }
        if (hi - lo <= 0) hi = lo + 1; // guarda final contra divisão por zero
        var span = hi - lo;

        // Guias tracejadas no topo e na base do eixo (mesmo estilo do frametime).
        foreach (var y in new[] { 0.5, h - 1.5 })
        {
            canvas.Children.Add(new Line
            {
                X1 = 0,
                X2 = w,
                Y1 = y,
                Y2 = y,
                Stroke = GuideBrush,
                StrokeThickness = 0.6,
                StrokeDashArray = new DoubleCollection { 2, 2 }
            });
        }

        var pts = new PointCollection(history.Length);
        for (int i = 0; i < history.Length; i++)
        {
            double x = w - (history.Length - i) * (w / Math.Max(history.Length, 120));
            var y = h - ((history[i] - lo) / span) * (h - 2) - 1;
            pts.Add(new Point(x, Math.Clamp(y, 0, h)));
        }

        canvas.Children.Add(new Polyline
        {
            Points = pts,
            Stroke = row.Stroke,
            StrokeThickness = 1.2,
            StrokeLineJoin = PenLineJoin.Round
        });
    }

    // ---------- Templates do usuário (P6.6) ----------

    /// <summary>Segmento compilado: texto literal OU placeholder (chave/patrão + decimais).</summary>
    private sealed record TemplateSegment(string Literal, string Pattern, SensorKey Key, int Decimals)
    {
        public bool IsLiteral => Pattern.Length == 0;
    }

    /// <summary>
    /// Parser simples e deliberadamente tolerante: "{Key}" e "{Key:N}" (N = decimais
    /// 0–3, padrão 1); "{Gpu*}" é tratado como padrão wildcard (primeiro match);
    /// "{" solitário permanece literal. Chave inválida (não é SensorType) só sobrevive
    /// como padrão e nunca resolve → linha escondida até existir série que case.
    /// </summary>
    private static List<TemplateSegment> CompileTemplate(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return [];

        var segments = new List<TemplateSegment>();
        var sb = new StringBuilder();

        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '{')
            {
                var close = text.IndexOf('}', i + 1);
                if (close < 0) { sb.Append(text[i..]); break; }

                var token = text[(i + 1)..close].Trim();
                i = close;

                var decimals = 1;
                var colon = token.IndexOf(':');
                if (colon > 0 &&
                    int.TryParse(token[(colon + 1)..].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n))
                {
                    decimals = Math.Clamp(n, 0, 3);
                    token = token[..colon].Trim();
                }

                if (token.Length == 0)
                {
                    segments.Add(new TemplateSegment(sb.ToString(), "", default, 0));
                    sb.Clear();
                    continue;
                }

                if (SensorKey.TryParse(token, out var key))
                    segments.Add(new TemplateSegment(sb.ToString(), "", key, decimals));
                else
                    segments.Add(new TemplateSegment(sb.ToString(), token, default, decimals));
                sb.Clear();
            }
            else
            {
                sb.Append(text[i]);
            }
        }

        if (sb.Length > 0) segments.Add(new TemplateSegment(sb.ToString(), "", default, 0));
        return segments;
    }

    /// <summary>Cor do template (hex #RGB-, #RRGGBB e #AARRGGBB); inválida/ausente = branco padrão.</summary>
    private static Brush TryParseTemplateColor(string? color)
    {
        var value = (color ?? "").Trim();
        try
        {
            if (value.Length is 4 or 7 or 9 && value[0] == '#')
            {
                var c = (Color)ColorConverter.ConvertFromString(value);
                return PinFrozenBrush(c);
            }
        }
        catch (FormatException)
        {
            // ValueError → cai no branco (Normalize já derruba cores inválidas no config).
        }
        return NormalBrush;
    }

    /// <summary>
    /// Resolve um placeholder para valor numérico; false quando NADA casa (linha oculta).
    /// Chave canônica: valor canônico primeiro, senão a primeira instância da categoria;
    /// chave com índice: exata; texto não-parseado: primeiro match de <see cref="SensorKey.MatchesPattern"/>.
    /// </summary>
    private static bool TryResolveTemplateValue(SensorData data, TemplateSegment segment, out double value)
    {
        value = 0;

        if (segment.IsLiteral) return false;

        if (segment.Pattern.Length > 0)
        {
            // Padrão wildcard: primeiro componente em GetSeriesKeys (canônicas primeiro).
            foreach (var key in data.GetSeriesKeys())
            {
                if (!key.MatchesPattern(segment.Pattern)) continue;
                value = ReadSeriesValue(data, key);
                return true;
            }

            // Sem match na CHAVE textual: rótulo amigável — exato ou nome-base antes
            // de " (" (sensores customizados são referenciáveis pelo nome do config).
            foreach (var key in data.GetSeriesKeys())
            {
                if (!LabelMatchesPattern(data.GetLabel(key), segment.Pattern)) continue;
                value = ReadSeriesValue(data, key);
                return true;
            }

            return false;
        }

        var k = segment.Key;
        if (k.IsPrimary)
        {
            if (data.Values.TryGetValue(k.Type, out var canonical)) { value = canonical; return true; }

            // Sem canônica: primeira instância publicada da categoria (#1, #2...).
            var inst = data.Series.Keys
                .Where(s => s.Type == k.Type && s.Index > 0)
                .OrderBy(s => s.Index)
                .FirstOrDefault();
            if (inst is { } found && data.Series.TryGetValue(found, out var v)) { value = v; return true; }
            return false;
        }

        return data.Series.TryGetValue(k, out value);
    }

    /// <summary>Valor com verificação de presença (GetValue devolve 0 para série ausente).</summary>
    private static double ReadSeriesValue(SensorData data, SensorKey key) =>
        key.IsPrimary
            ? data.Values.TryGetValue(key.Type, out var v) ? v : 0
            : data.Series.TryGetValue(key, out var s) ? s : 0;

    /// <summary>
    /// Padrão casa o rótulo por igualdade ou por nome-base (rótulo "DeltaTemp (°C)"
    /// casa "DeltaTemp"): mesma regra da resolução de fórmulas dos sensores customizados.
    /// </summary>
    private static bool LabelMatchesPattern(string label, string pattern)
    {
        if (string.Equals(label, pattern, StringComparison.OrdinalIgnoreCase)) return true;
        var paren = label.IndexOf(" (", StringComparison.Ordinal);
        if (paren <= 0 || paren == label.Length - 2) return false;
        return string.Equals(label[..paren], pattern, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Renderiza o texto do template; false = algum placeholder não resolveu.</summary>
    private static bool TryRenderTemplate(SensorData data, List<TemplateSegment> segments, out string text)
    {
        var sb = new StringBuilder();
        foreach (var segment in segments)
        {
            if (segment.IsLiteral)
            {
                sb.Append(segment.Literal);
                continue;
            }

            if (!TryResolveTemplateValue(data, segment, out var value)) { text = ""; return false; }
            sb.Append(value.ToString($"F{segment.Decimals}", CultureInfo.InvariantCulture));
        }

        text = sb.ToString();
        return text.Length > 0;
    }

    /// <summary>
    /// Linhas de template (P6.6) num container próprio criado no primeiro snapshot —
    /// e sempre reposicionado para o fim do painel (ver EnsureDynamicRows).
    /// </summary>
    private void EnsureTemplateRows(SensorData data)
    {
        if (_templatesPanel != null || _compiledTemplates.Count == 0) return;

        _templatesPanel = new StackPanel();
        foreach (var (segments, color) in _compiledTemplates)
        {
            var row = new TextBlock
            {
                FontFamily = OsdFont,
                FontSize = _baseFontSize - 1,
                Foreground = color,
                Margin = new Thickness(0, _rowMargin, 0, 0),
                TextTrimming = TextTrimming.CharacterEllipsis,
                Visibility = Visibility.Collapsed // até todos os placeholders resolverem
            };

            _templatesPanel.Children.Add(row);
            _updaters.Add(d =>
            {
                if (!TryRenderTemplate(d, segments, out var text))
                {
                    row.Visibility = Visibility.Collapsed;
                    return;
                }

                row.Visibility = Visibility.Visible;
                row.Text = text;
            });
        }

        SensorPanel.Children.Add(_templatesPanel);
        _templatesPanel.SetCurrentValue(StackPanel.MarginProperty, new Thickness(0, _rowMargin, 0, 0));
        _ = data; // entrada uniforme com os demais services de linha (uso futuro de debug)
    }

    private static string TagFor(SensorKey key)
    {
        var num = key.Index > 0 ? key.Index.ToString() : "";
        return key.Type switch
        {
            SensorType.CpuTotal or SensorType.CpuPerCore or SensorType.CpuTemperature or
            SensorType.CpuClock or SensorType.CpuVoltage or SensorType.CpuPower => "CPU" + num,

            SensorType.GpuCore or SensorType.GpuMemory or SensorType.GpuMemoryUsed or SensorType.GpuMemoryTotal or
            SensorType.GpuTemperature or SensorType.GpuHotSpot or SensorType.GpuVramTemperature or
            SensorType.GpuCoreClock or
            SensorType.GpuMemoryClock or SensorType.GpuVoltage or SensorType.GpuFan or SensorType.GpuPower =>
                "GPU" + num,

            SensorType.FanSpeed => "FAN" + num,
            SensorType.RamUsage or SensorType.RamAvailable or SensorType.RamUsed or SensorType.RamTotal or
            SensorType.RamTemperature => "RAM" + num,
            SensorType.DiskUsage or SensorType.DiskRead or SensorType.DiskWrite or
            SensorType.DiskTemperature or SensorType.DiskHealth or
            SensorType.DiskTotalRead or SensorType.DiskTotalWritten => "DSK" + num,
            SensorType.BatteryLevel or SensorType.BatteryVoltage or SensorType.BatteryPower or
            SensorType.BatteryWearLevel or SensorType.BatteryAcPower => "BAT" + num,
            SensorType.NetworkUp or SensorType.NetworkDown => "NET" + num,
            SensorType.MotherboardTemp => "MB" + num,
            SensorType.VrmTemperature => "VRM" + num,
            SensorType.RailVoltage => "RL" + num,
            _ => key.Type.ToString() + num
        };
    }

    private static Color TagColorFor(SensorType type) => type switch
    {
        SensorType.CpuTotal or SensorType.CpuPerCore or SensorType.CpuTemperature or
        SensorType.CpuClock or SensorType.CpuVoltage or SensorType.CpuPower => Color.FromRgb(0x00, 0xD2, 0xFF),

        SensorType.GpuCore or SensorType.GpuMemory or SensorType.GpuMemoryUsed or SensorType.GpuMemoryTotal or
        SensorType.GpuTemperature or SensorType.GpuHotSpot or SensorType.GpuVramTemperature or
        SensorType.GpuCoreClock or
        SensorType.GpuMemoryClock or SensorType.GpuVoltage or SensorType.GpuFan or SensorType.GpuPower =>
            Color.FromRgb(0xFF, 0x8A, 0x00),

        SensorType.RamUsage or SensorType.RamAvailable or SensorType.RamUsed or SensorType.RamTotal or
        SensorType.RamTemperature => Color.FromRgb(0xC0, 0x84, 0xFC),

        SensorType.DiskUsage or SensorType.DiskRead or SensorType.DiskWrite or
        SensorType.DiskTemperature or SensorType.DiskHealth or
        SensorType.DiskTotalRead or SensorType.DiskTotalWritten => Color.FromRgb(0x60, 0xA5, 0xFA),

        SensorType.BatteryLevel or SensorType.BatteryVoltage or SensorType.BatteryPower or
        SensorType.BatteryWearLevel or SensorType.BatteryAcPower => Color.FromRgb(0xA3, 0xE6, 0x35),

        SensorType.NetworkUp or SensorType.NetworkDown => Color.FromRgb(0x34, 0xD3, 0x99),
        SensorType.FanSpeed => Color.FromRgb(0xF4, 0x72, 0xB6),
        _ => Color.FromRgb(0xCB, 0xD5, 0xE1)
    };

    private static int FamilyOrder(SensorType type) => type switch
    {
        SensorType.GpuCore or SensorType.GpuMemory or SensorType.GpuMemoryUsed or SensorType.GpuMemoryTotal or
        SensorType.GpuTemperature or SensorType.GpuHotSpot or SensorType.GpuVramTemperature or
        SensorType.GpuCoreClock or
        SensorType.GpuMemoryClock or SensorType.GpuVoltage or SensorType.GpuFan or SensorType.GpuPower => 0,

        SensorType.FanSpeed => 1,

        SensorType.DiskUsage or SensorType.DiskRead or SensorType.DiskWrite or
        SensorType.DiskTemperature or SensorType.DiskHealth or
        SensorType.DiskTotalRead or SensorType.DiskTotalWritten => 2,

        SensorType.RamTemperature => 3,

        SensorType.CpuTotal or SensorType.CpuPerCore or SensorType.CpuTemperature or
        SensorType.CpuClock or SensorType.CpuVoltage or SensorType.CpuPower => 4,

        SensorType.NetworkUp or SensorType.NetworkDown => 5,

        SensorType.BatteryLevel or SensorType.BatteryVoltage or SensorType.BatteryPower or
        SensorType.BatteryWearLevel or SensorType.BatteryAcPower => 6,

        _ => 7
    };

    protected override void OnClosed(EventArgs e)
    {
        // Sem isso, cada fechamento/recriação do overlay acumulava timers órfãos
        // (foreground check a cada 30 ms + SetWindowPos a cada 500 ms) para sempre.
        _positionTimer?.Stop();
        _topmostTimer?.Stop();
        _frametimeGraph?.Children.Clear();
        _updaters.Clear();
        base.OnClosed(e);
    }

    private static Brush GetTempBrush(double temp)
    {
        if (temp >= 80) return HotBrush;
        if (temp >= 70) return WarmBrush;
        return NormalBrush;
    }

    private static Brush MakeFrozenBrush(byte a, byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromArgb(a, r, g, b));
        brush.Freeze(); // escovas compartilhadas entre ticks não precisam de alteração
        return brush;
    }

    private static Brush GetUsageBrush(double usage)
    {
        if (usage >= 90) return HotBrush;
        if (usage >= 75) return WarmBrush;
        return NormalBrush;
    }

    /// <summary>Cor de acento do overlay (fallback azul royal quando o hex é inválido).</summary>
    private static Color ParseAccentColor(string? hex)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(hex) && ColorConverter.ConvertFromString(hex) is Color color)
                return color;
        }
        catch
        {
            // hex inválido: usa o padrão
        }
        return Color.FromRgb(0x1E, 0x90, 0xFF);
    }

    private void MakeClickThrough()
    {
        var handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero)
            SourceInitialized += (_, _) => SetExTransparent();
        else
            SetExTransparent();
    }

    private void SetExTransparent()
    {
        var handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero) return;

        var exStyle = GetWindowLongPtr(handle, GWL_EXSTYLE).ToInt64();
        SetWindowLongPtr(handle, GWL_EXSTYLE, new IntPtr(exStyle | WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_TOPMOST | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW));
    }

    private void StartTopmostTimer()
    {
        _topmostTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _topmostTimer.Tick += (_, _) =>
        {
            var handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            if (handle != IntPtr.Zero)
                SetWindowPos(handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        };
        _topmostTimer.Start();
    }

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const int WS_EX_LAYERED = 0x00080000;
    private const int WS_EX_TOPMOST = 0x00000008;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private static readonly IntPtr HWND_TOPMOST = new(-1);
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOACTIVATE = 0x0010;

    // GetWindowLong/SetWindowLong (32 bits) perderam uso recomendado em processo x64 —
    // usam as variantes *_PtrW, com fallback para WindowLongW quando o processo é 32 bits.
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)]
    private static extern int GetWindowLong32(IntPtr hwnd, int index);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtr64(IntPtr hwnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
    private static extern int SetWindowLong32(IntPtr hwnd, int index, int newStyle);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr64(IntPtr hwnd, int index, IntPtr newStyle);

    private static IntPtr GetWindowLongPtr(IntPtr hwnd, int index) =>
        IntPtr.Size == 4 ? new IntPtr(GetWindowLong32(hwnd, index)) : GetWindowLongPtr64(hwnd, index);

    private static IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr newStyle) =>
        IntPtr.Size == 4 ? new IntPtr(SetWindowLong32(hwnd, index, (int)newStyle)) : SetWindowLongPtr64(hwnd, index, newStyle);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
}
