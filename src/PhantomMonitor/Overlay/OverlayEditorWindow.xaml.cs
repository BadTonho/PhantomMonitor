using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
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
    private DrawingBrush? _gridBrush;

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

        if (OpacitySlider != null) OpacitySlider.Value = _config.OverlayOpacity;
        if (OpacityLabel != null) OpacityLabel.Text = $"{(_config.OverlayOpacity * 100):F0}%";

        if (WidthSlider != null) WidthSlider.Value = _config.Overlay.Width;
        if (WidthLabel != null) WidthLabel.Text = $"{_config.Overlay.Width} px";

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
        }
    }

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

        if (OpacitySlider != null) _config.OverlayOpacity = OpacitySlider.Value;
        if (WidthSlider != null) _config.Overlay.Width = (int)WidthSlider.Value;

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

        LoadConfigToUi();
    }

    private void OnPresetPositionsClick(object sender, RoutedEventArgs e)
    {
        OnPosTopLeft(sender, e);
    }
}
