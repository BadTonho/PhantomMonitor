using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using PhantomMonitor.Models;
using PhantomMonitor.Services;

namespace PhantomMonitor.Overlay;

public partial class OverlayEditorWindow : Window
{
    private readonly AppConfig _config;
    private readonly string _configPath;
    private bool _isInitialized = false;

    public event Action<AppConfig>? ConfigApplied;

    private FrameworkElement? _selectedBlock;
    private FrameworkElement? _draggedBlock;
    private Point _dragStartPoint;
    private double _initialLeft;
    private double _initialTop;
    private double _configStartX;
    private double _configStartY;
    private DrawingBrush? _gridBrush;

    private static readonly string[] DefaultSeries = ["Gpu*", "FanSpeed*", "Disk*"];

    /// <summary>Checkbox de séries por instância → padrões wildcard liberados em overlay.shownSeries.</summary>
    private (CheckBox box, string[] patterns)[] SeriesOptions => new[]
    {
        (CheckSeriesGpu, new[] { "Gpu*" }),
        (CheckSeriesFans, new[] { "FanSpeed*" }),
        (CheckSeriesDisks, new[] { "Disk*" }),
        (CheckSeriesCpuCores, new[] { "CpuTemperature#*", "CpuPerCore#*", "CpuClock#*" }),
        (CheckSeriesCpuPower, new[] { "CpuPower*" }),
        (CheckSeriesMbTemps, new[] { "MotherboardTemp#*" }),
        (CheckSeriesVrm, new[] { "VrmTemperature*" }),
        (CheckSeriesRails, new[] { "RailVoltage*" }),
        (CheckSeriesNet, new[] { "Network*" }),
        (CheckSeriesRamTemps, new[] { "RamTemperature*" })
    };

    public OverlayEditorWindow(AppConfig config, string configPath)
    {
        _config = config;
        _configPath = configPath;

        InitializeComponent();

        InitGridBackground();
        LoadConfigToUi();

        _isInitialized = true;
    }

    private void InitGridBackground()
    {
        try
        {
            var group = new DrawingGroup();
            group.Children.Add(new GeometryDrawing(
                new SolidColorBrush(Color.FromRgb(0x0A, 0x0C, 0x10)),
                null,
                new RectangleGeometry(new Rect(0, 0, 20, 20))));

            var pen = new Pen(new SolidColorBrush(Color.FromArgb(0x28, 0xFF, 0xFF, 0xFF)), 1.0);
            pen.Freeze();

            var lines = new GeometryGroup();
            lines.Children.Add(new LineGeometry(new Point(0, 0), new Point(20, 0)));
            lines.Children.Add(new LineGeometry(new Point(0, 0), new Point(0, 20)));
            lines.Freeze();

            group.Children.Add(new GeometryDrawing(null, pen, lines));
            group.Freeze();

            _gridBrush = new DrawingBrush(group)
            {
                TileMode = TileMode.Tile,
                Viewport = new Rect(0, 0, 20, 20),
                ViewportUnits = BrushMappingMode.Absolute
            };
            _gridBrush.Freeze();

            if (EditorCanvas != null)
                EditorCanvas.Background = _gridBrush;
        }
        catch
        {
            // Fallback caso DrawingBrush falhe em algum ambiente
            if (EditorCanvas != null)
                EditorCanvas.Background = new SolidColorBrush(Color.FromRgb(0x0A, 0x0C, 0x10));
        }
    }

    private void LoadConfigToUi()
    {
        if (PosXBox != null) PosXBox.Text = _config.OverlayPosition.X.ToString("F0");
        if (PosYBox != null) PosYBox.Text = _config.OverlayPosition.Y.ToString("F0");

        if (CheckFps != null) CheckFps.IsChecked = _config.Overlay.ShowFps;
        if (CheckGraph != null) CheckGraph.IsChecked = _config.Overlay.ShowFrametimeGraph;
        if (CheckCpu != null) CheckCpu.IsChecked = _config.Overlay.ShowCpu;
        if (CheckGpu != null) CheckGpu.IsChecked = _config.Overlay.ShowGpu;
        if (CheckRam != null) CheckRam.IsChecked = _config.Overlay.ShowRam;
        if (CheckDisk != null) CheckDisk.IsChecked = _config.Overlay.ShowDisk;
        if (CheckNetwork != null) CheckNetwork.IsChecked = _config.Overlay.ShowNetwork;
        if (CheckFans != null) CheckFans.IsChecked = _config.Overlay.ShowFans;
        if (CheckMotherboard != null) CheckMotherboard.IsChecked = _config.Overlay.ShowMotherboard;

        if (OpacitySlider != null) OpacitySlider.Value = _config.OverlayOpacity;
        if (OpacityLabel != null) OpacityLabel.Text = $"{(_config.OverlayOpacity * 100):F0}%";

        if (WidthSlider != null) WidthSlider.Value = _config.Overlay.Width;
        if (WidthLabel != null) WidthLabel.Text = $"{_config.Overlay.Width} px";

        if (CheckCompact != null) CheckCompact.IsChecked = _config.Overlay.CompactMode;

        if (FontSizeSlider != null)
            FontSizeSlider.Value = Math.Clamp(_config.Overlay.FontSize, FontSizeSlider.Minimum, FontSizeSlider.Maximum);
        if (FontSizeLabel != null) FontSizeLabel.Text = $"{_config.Overlay.FontSize:F1} pt";

        if (AccentLabel != null) AccentLabel.Text = _config.Overlay.AccentColor;

        if (HotkeyCombo != null) HotkeyCombo.Text = _config.ToggleOverlayHotkey;

        if (_config.Overlay.ShownSeries is { Count: > 0 } series)
        {
            foreach (var (box, patterns) in SeriesOptions)
                box.IsChecked = patterns.Any(p => series.Contains(p, StringComparer.OrdinalIgnoreCase));
        }

        RebuildProfilesPanel();

        UpdateLayersVisibility();
    }

    private void UpdateLayersVisibility()
    {
        if (BlockFps != null) BlockFps.Visibility = CheckFps?.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        if (BlockGraph != null) BlockGraph.Visibility = CheckGraph?.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        if (BlockCpu != null) BlockCpu.Visibility = CheckCpu?.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        if (BlockGpu != null) BlockGpu.Visibility = CheckGpu?.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        if (BlockRam != null) BlockRam.Visibility = CheckRam?.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        if (BlockDisk != null) BlockDisk.Visibility = CheckDisk?.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        if (BlockNetwork != null) BlockNetwork.Visibility = CheckNetwork?.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        if (BlockFan != null) BlockFan.Visibility = CheckFans?.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        if (BlockMotherboard != null) BlockMotherboard.Visibility = CheckMotherboard?.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnLayerToggled(object sender, RoutedEventArgs e)
    {
        if (!_isInitialized) return;
        UpdateLayersVisibility();
    }

    private void OnBlockMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement block)
        {
            _draggedBlock = block;
            _dragStartPoint = e.GetPosition(EditorCanvas);
            _initialLeft = Canvas.GetLeft(block);
            if (double.IsNaN(_initialLeft)) _initialLeft = 0;
            _initialTop = Canvas.GetTop(block);
            if (double.IsNaN(_initialTop)) _initialTop = 0;

            // Arrastar um bloco desloca o offset GLOBAL do overlay (mesma quantidade),
            // então a base do offset no início do arraste é capturada aqui.
            _configStartX = _config.OverlayPosition.X;
            _configStartY = _config.OverlayPosition.Y;

            SelectBlock(block);
            block.CaptureMouse();
            e.Handled = true;
        }
    }

    private void SelectBlock(FrameworkElement block)
    {
        if (_selectedBlock is Border prevBorder)
        {
            prevBorder.BorderBrush = new SolidColorBrush(Color.FromArgb(0x60, 0xFF, 0xFF, 0xFF));
            prevBorder.BorderThickness = new Thickness(1);
        }

        _selectedBlock = block;

        if (_selectedBlock is Border newBorder)
        {
            newBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(0x00, 0xF0, 0xFF));
            newBorder.BorderThickness = new Thickness(1.5);
        }

        string blockName = block.Name switch
        {
            "BlockFps" => "Taxa de Quadros (FPS)",
            "BlockGraph" => "Gráfico de Frametime",
            "BlockCpu" => "Processador (CPU)",
            "BlockGpu" => "Placa de Vídeo (GPU)",
            "BlockRam" => "Memória RAM",
            "BlockDisk" => "Armazenamento (Disco)",
            "BlockNetwork" => "Rede (↓/↑)",
            "BlockFan" => "Ventoinhas (GPU/Sistema)",
            "BlockMotherboard" => "Placa-mãe (Temperatura)",
            _ => "Bloco Selecionado"
        };

        if (SelectedBlockText != null)
            SelectedBlockText.Text = $"Selecionado: {blockName}";
        if (StatusCoordinatesText != null)
            StatusCoordinatesText.Text = $"X: {Canvas.GetLeft(block):F0} px | Y: {Canvas.GetTop(block):F0} px";
    }

    private void OnCanvasMouseMove(object sender, MouseEventArgs e)
    {
        if (_draggedBlock != null && e.LeftButton == MouseButtonState.Pressed && EditorCanvas != null)
        {
            var curPoint = e.GetPosition(EditorCanvas);
            var deltaX = curPoint.X - _dragStartPoint.X;
            var deltaY = curPoint.Y - _dragStartPoint.Y;

            var newLeft = _initialLeft + deltaX;
            var newTop = _initialTop + deltaY;

            if (SnapGridCheck?.IsChecked == true)
            {
                newLeft = Math.Round(newLeft / 10.0) * 10;
                newTop = Math.Round(newTop / 10.0) * 10;
            }

            newLeft = Math.Max(10, Math.Min(EditorCanvas.ActualWidth - 60, newLeft));
            newTop = Math.Max(10, Math.Min(EditorCanvas.ActualHeight - 30, newTop));

            Canvas.SetLeft(_draggedBlock, newLeft);
            Canvas.SetTop(_draggedBlock, newTop);

            // O arraste persiste de verdade: o delta do bloco vira delta do offset do
            // overlay. Escrever nos boxes dispara OnPositionBoxChanged, que já atualiza
            // _config (antes o arraste mexia só no preview e nada era salvo).
            if (PosXBox != null && PosYBox != null)
            {
                PosXBox.Text = Math.Clamp(_configStartX + deltaX, -5000, 5000).ToString("F0");
                PosYBox.Text = Math.Clamp(_configStartY + deltaY, -5000, 5000).ToString("F0");
            }

            if (StatusCoordinatesText != null)
                StatusCoordinatesText.Text = $"X: {newLeft:F0} px | Y: {newTop:F0} px";
        }
    }

    private void OnCanvasMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_draggedBlock != null)
        {
            _draggedBlock.ReleaseMouseCapture();
            _draggedBlock = null;
        }
    }

    private void OnToggleGrid(object sender, RoutedEventArgs e)
    {
        if (!_isInitialized || EditorCanvas == null) return;
        if (ShowGridCheck?.IsChecked == true && _gridBrush != null)
        {
            EditorCanvas.Background = _gridBrush;
        }
        else
        {
            EditorCanvas.Background = new SolidColorBrush(Color.FromRgb(0x0A, 0x0C, 0x10));
        }
    }

    private void OnPositionBoxChanged(object sender, TextChangedEventArgs e)
    {
        if (!_isInitialized || PosXBox == null || PosYBox == null) return;

        if (double.TryParse(PosXBox.Text, out var x) && double.TryParse(PosYBox.Text, out var y))
        {
            _config.OverlayPosition.X = x;
            _config.OverlayPosition.Y = y;
            if (StatusCoordinatesText != null)
                StatusCoordinatesText.Text = $"Posição do Overlay na Tela: X: {x:F0} px | Y: {y:F0} px";
        }
    }

    private void OnPosTopLeft(object sender, RoutedEventArgs e)
    {
        if (PosXBox != null) PosXBox.Text = "20";
        if (PosYBox != null) PosYBox.Text = "40";
    }

    private void OnPosTopRight(object sender, RoutedEventArgs e)
    {
        var screenW = SystemParameters.PrimaryScreenWidth;
        var w = _config.Overlay.Width > 0 ? _config.Overlay.Width : 220;
        if (PosXBox != null) PosXBox.Text = Math.Max(20, screenW - w - 20).ToString("F0");
        if (PosYBox != null) PosYBox.Text = "40";
    }

    private void OnPosBottomLeft(object sender, RoutedEventArgs e)
    {
        var screenH = SystemParameters.PrimaryScreenHeight;
        if (PosXBox != null) PosXBox.Text = "20";
        if (PosYBox != null) PosYBox.Text = Math.Max(40, screenH - 260).ToString("F0");
    }

    private void OnPosBottomRight(object sender, RoutedEventArgs e)
    {
        var screenW = SystemParameters.PrimaryScreenWidth;
        var screenH = SystemParameters.PrimaryScreenHeight;
        var w = _config.Overlay.Width > 0 ? _config.Overlay.Width : 220;
        if (PosXBox != null) PosXBox.Text = Math.Max(20, screenW - w - 20).ToString("F0");
        if (PosYBox != null) PosYBox.Text = Math.Max(40, screenH - 260).ToString("F0");
    }

    private void OnOpacitySliderChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_isInitialized) return;
        if (OpacityLabel != null)
        {
            OpacityLabel.Text = $"{(e.NewValue * 100):F0}%";
            _config.OverlayOpacity = e.NewValue;
        }
    }

    private void OnWidthSliderChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_isInitialized) return;
        if (WidthLabel != null)
        {
            WidthLabel.Text = $"{e.NewValue:F0} px";
            _config.Overlay.Width = (int)e.NewValue;
            if (BlockGraph != null) BlockGraph.Width = e.NewValue;
            if (BlockCpu != null) BlockCpu.Width = e.NewValue;
            if (BlockGpu != null) BlockGpu.Width = e.NewValue;
            if (BlockRam != null) BlockRam.Width = e.NewValue;
            if (BlockDisk != null) BlockDisk.Width = e.NewValue;
            if (BlockNetwork != null) BlockNetwork.Width = e.NewValue;
            if (BlockFan != null) BlockFan.Width = e.NewValue;
            if (BlockMotherboard != null) BlockMotherboard.Width = e.NewValue;
        }
    }

    private void OnFontSizeSliderChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_isInitialized) return;
        if (FontSizeLabel != null)
        {
            FontSizeLabel.Text = $"{e.NewValue:F1} pt";
            _config.Overlay.FontSize = e.NewValue;
        }
    }

    private void OnAccentPreset(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string hex })
        {
            _config.Overlay.AccentColor = hex;
            if (AccentLabel != null) AccentLabel.Text = hex;
        }
    }

    // ---------- Perfis por aplicativo (cartões editáveis) ----------

    private readonly List<(TextBox Box, Action<string> Apply)> _profileBindings = [];

    private void RebuildProfilesPanel()
    {
        if (ProfilesPanel == null) return;
        ProfilesPanel.Children.Clear();
        _profileBindings.Clear();

        for (int i = 0; i < _config.Profiles.Count; i++)
        {
            var profile = _config.Profiles[i];
            var card = new Border
            {
                BorderBrush = (System.Windows.Media.Brush)TryFindResource("BorderBrush")!,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(8),
                Margin = new Thickness(0, 0, 0, 8)
            };

            var stack = new StackPanel();

            // Cabeçalho: nome atual + remover
            var header = new Grid { Margin = new Thickness(0, 0, 0, 4) };
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.Children.Add(new TextBlock
            {
                Text = $"Perfil {i + 1}",
                FontWeight = FontWeights.Bold,
                VerticalAlignment = VerticalAlignment.Center
            });
            var remove = new Button
            {
                Content = "Remover",
                Padding = new Thickness(8, 2, 8, 2),
                FontSize = 10,
                Tag = profile
            };
            if (TryFindResource("SecondaryButton") is System.Windows.Style removeStyle) remove.Style = removeStyle;
            remove.Click += OnRemoveProfileClicked;
            Grid.SetColumn(remove, 1);
            header.Children.Add(remove);
            stack.Children.Add(header);

            AddProfileField(stack, "Nome do perfil", profile.Name, v => profile.Name = v.Trim());

            var appRow = new Grid { Margin = new Thickness(0, 0, 0, 4) };
            appRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var appBox = new TextBox { Text = profile.AppMatch, VerticalContentAlignment = VerticalAlignment.Center };
            Grid.SetColumn(appBox, 0);
            appRow.Children.Add(appBox);
            var capture = new Button
            {
                Content = "Capturar",
                Padding = new Thickness(8, 2, 8, 2),
                FontSize = 10,
                Margin = new Thickness(6, 0, 0, 0),
                Tag = appBox
            };
            if (TryFindResource("SecondaryButton") is System.Windows.Style captureStyle) capture.Style = captureStyle;
            capture.Click += OnCaptureAppClicked;
            Grid.SetColumn(capture, 1);
            appRow.Children.Add(capture);
            stack.Children.Add(new TextBlock { Text = "Executável (AppMatch) — vazio/* casam com qualquer app", FontSize = 10, Opacity = 0.75 });
            stack.Children.Add(appRow);
            _profileBindings.Add((appBox, v => profile.AppMatch = v.Trim()));

            AddProfileField(stack, "Offset X (vazio = padrão)", profile.OffsetX?.ToString("F0") ?? "", v => profile.OffsetX = ParseDoubleOrNull(v));
            AddProfileField(stack, "Offset Y (vazio = padrão)", profile.OffsetY?.ToString("F0") ?? "", v => profile.OffsetY = ParseDoubleOrNull(v));
            AddProfileField(stack, "Largura px (vazio = padrão)", profile.Width?.ToString("F0") ?? "", v => profile.Width = ParseIntOrNull(v));
            AddProfileField(stack, "Fonte pt (vazio = padrão)", profile.FontSize?.ToString("F1") ?? "", v => profile.FontSize = ParseDoubleOrNull(v));
            AddProfileField(stack, "Cor de acento #RRGGBB (vazio = padrão)", profile.AccentColor ?? "", v => profile.AccentColor = string.IsNullOrWhiteSpace(v) ? null : v.Trim());

            card.Child = stack;
            ProfilesPanel.Children.Add(card);
        }
    }

    private void AddProfileField(StackPanel stack, string label, string value, Action<string> apply)
    {
        stack.Children.Add(new TextBlock { Text = label, FontSize = 10, Opacity = 0.75 });
        var box = new TextBox { Text = value, Margin = new Thickness(0, 0, 0, 4) };
        stack.Children.Add(box);
        _profileBindings.Add((box, apply));
    }

    private static double? ParseDoubleOrNull(string text)
    {
        var trimmed = text.Trim();
        if (trimmed.Length == 0) return null;
        return double.TryParse(trimmed, out var value) ? value : null;
    }

    private static int? ParseIntOrNull(string text)
    {
        var trimmed = text.Trim();
        if (trimmed.Length == 0) return null;
        return int.TryParse(trimmed, out var value) ? value : null;
    }

    private void ApplyProfileFields()
    {
        foreach (var (box, apply) in _profileBindings)
        {
            apply(box.Text ?? "");
        }
    }

    private void OnRemoveProfileClicked(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: OverlayProfile profile })
        {
            _config.Profiles.Remove(profile);
            RebuildProfilesPanel();
        }
    }

    private void OnAddProfileClicked(object sender, RoutedEventArgs e)
    {
        _config.Profiles.Add(new OverlayProfile
        {
            Name = $"Perfil {_config.Profiles.Count + 1}",
            AppMatch = CaptureForegroundApp() ?? ""
        });
        RebuildProfilesPanel();
    }

    private void OnCaptureAppClicked(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: TextBox box })
        {
            var app = CaptureForegroundApp();
            box.Text = app ?? box.Text;
        }
    }

    /// <summary>Nome do executável sem .exe da janela em foco (null se a captura falhar).</summary>
    private static string? CaptureForegroundApp()
    {
        try
        {
            var hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero) return null;
            GetWindowThreadProcessId(hwnd, out var pid);
            using var process = System.Diagnostics.Process.GetProcessById((int)pid);
            var name = process.ProcessName;
            if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) name = name[..^4];
            return name;
        }
        catch
        {
            return null;
        }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        if (PosXBox != null && double.TryParse(PosXBox.Text, out var x)) _config.OverlayPosition.X = x;
        if (PosYBox != null && double.TryParse(PosYBox.Text, out var y)) _config.OverlayPosition.Y = y;

        if (CheckFps != null) _config.Overlay.ShowFps = CheckFps.IsChecked == true;
        if (CheckGraph != null) _config.Overlay.ShowFrametimeGraph = CheckGraph.IsChecked == true;
        if (CheckCpu != null) _config.Overlay.ShowCpu = CheckCpu.IsChecked == true;
        if (CheckGpu != null) _config.Overlay.ShowGpu = CheckGpu.IsChecked == true;
        if (CheckRam != null) _config.Overlay.ShowRam = CheckRam.IsChecked == true;
        if (CheckDisk != null) _config.Overlay.ShowDisk = CheckDisk.IsChecked == true;
        if (CheckNetwork != null) _config.Overlay.ShowNetwork = CheckNetwork.IsChecked == true;
        if (CheckFans != null) _config.Overlay.ShowFans = CheckFans.IsChecked == true;
        if (CheckMotherboard != null) _config.Overlay.ShowMotherboard = CheckMotherboard.IsChecked == true;

        if (OpacitySlider != null) _config.OverlayOpacity = OpacitySlider.Value;
        if (WidthSlider != null) _config.Overlay.Width = (int)WidthSlider.Value;
        if (CheckCompact != null) _config.Overlay.CompactMode = CheckCompact.IsChecked == true;
        if (FontSizeSlider != null) _config.Overlay.FontSize = FontSizeSlider.Value;

        // Séries por instância: padrões marcados entram na whitelist; os desmarcados saem.
        // Padrões manuais do config que nenhuma checkbox cobre são preservados.
        var covered = SeriesOptions.SelectMany(o => o.patterns).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var series = (_config.Overlay.ShownSeries ?? [])
            .Where(p => !covered.Contains(p))
            .ToList();
        foreach (var (box, patterns) in SeriesOptions)
        {
            if (box.IsChecked != true) continue;
            foreach (var pattern in patterns)
            {
                if (!series.Contains(pattern, StringComparer.OrdinalIgnoreCase))
                    series.Add(pattern);
            }
        }

        _config.Overlay.ShownSeries = series;

        // Atalho global: valida antes de salvar (vazio = desativado)
        if (HotkeyCombo != null)
        {
            var hotkey = HotkeyCombo.Text.Trim();
            if (hotkey.Length > 0 && !HotkeyParser.TryParse(hotkey, out _, out _))
            {
                MessageBox.Show(
                    $"Atalho inválido: \"{hotkey}\"\n\nUse combinações como Ctrl+Shift+O, Alt+F10 ou uma tecla de função (F1–F24).\n" +
                    "Atalhos com uma única letra/número exigem um modificador (Ctrl/Alt/Shift/Win).",
                    "Editor de Overlay", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            _config.ToggleOverlayHotkey = hotkey;
        }

        // Perfis por aplicativo: aplica o conteúdo dos campos aos objetos antes de salvar.
        ApplyProfileFields();

        var saved = _config.Save(_configPath);
        ConfigApplied?.Invoke(_config);

        if (saved)
        {
            MessageBox.Show("Configuração do Overlay salva e aplicada com sucesso!",
                "Editor de Overlay", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        else
        {
            MessageBox.Show($"Configuração aplicada, mas não foi possível salvar em:\n{_configPath}\n\nVeja logs/errors.log.",
                "Editor de Overlay", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OnResetClick(object sender, RoutedEventArgs e)
    {
        _config.OverlayPosition.X = 20;
        _config.OverlayPosition.Y = 80;
        _config.OverlayOpacity = 0.85;
        _config.Overlay.Width = 220;
        _config.Overlay.ShowFps = true;
        _config.Overlay.ShowFrametimeGraph = true;
        _config.Overlay.ShowCpu = true;
        _config.Overlay.ShowGpu = true;
        _config.Overlay.ShowRam = true;
        _config.Overlay.ShowDisk = true;
        _config.Overlay.ShowNetwork = false;
        _config.Overlay.ShowFans = true;
        _config.Overlay.ShowMotherboard = false;

        _config.Overlay.CompactMode = false;
        _config.Overlay.AccentColor = "#1E90FF";
        _config.Overlay.FontSize = 11.5;
        _config.Overlay.ShownSeries = [.. DefaultSeries];

        _config.ToggleOverlayHotkey = "";

        LoadConfigToUi();
    }

    private void OnPresetPositionsClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button) return;

        // Menu com os 4 cantos: antes o botão da barra aplicava apenas o superior esquerdo
        // (os demais só eram alcançáveis pela grade de botões na seção "POSIÇÃO").
        var menu = new ContextMenu { PlacementTarget = button };

        var topLeft = new MenuItem { Header = "Canto Superior Esquerdo" };
        topLeft.Click += OnPosTopLeft;
        var topRight = new MenuItem { Header = "Canto Superior Direito" };
        topRight.Click += OnPosTopRight;
        var bottomLeft = new MenuItem { Header = "Canto Inferior Esquerdo" };
        bottomLeft.Click += OnPosBottomLeft;
        var bottomRight = new MenuItem { Header = "Canto Inferior Direito" };
        bottomRight.Click += OnPosBottomRight;

        menu.Items.Add(topLeft);
        menu.Items.Add(topRight);
        menu.Items.Add(bottomLeft);
        menu.Items.Add(bottomRight);
        menu.IsOpen = true;
    }
}
