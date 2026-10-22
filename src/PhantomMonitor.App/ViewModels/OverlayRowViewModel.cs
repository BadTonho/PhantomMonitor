using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia.Media;

namespace PhantomMonitor.App.ViewModels;

/// <summary>
/// Row de gráfico por série no overlay (P8.5 do WPF): sparkline persistente com
/// buffer circular próprio no VM (OverlayMetricsBuilder empurra a amostra quando a
/// série resolve). Reconciliada por <see cref="Identity"/> como as rows de texto.
/// </summary>
public sealed class OverlayGraphRowViewModel : INotifyPropertyChanged
{
    private IReadOnlyList<double> _samples = [];
    private IBrush? _stroke;
    private double? _minY;
    private double? _maxY;
    private double _height = 36;
    private bool _isVisible;

    public OverlayGraphRowViewModel(string identity)
    {
        Identity = identity;
    }

    /// <summary>Chave estável ("#graph:&lt;padrão da série&gt;").</summary>
    public string Identity { get; }

    /// <summary>false enquanto a série não resolve (row escondida; buffer fica).</summary>
    public bool IsVisible
    {
        get => _isVisible;
        set { if (_isVisible != value) { _isVisible = value; Raise(); } }
    }

    public IReadOnlyList<double> Samples
    {
        get => _samples;
        set { if (!ReferenceEquals(_samples, value)) { _samples = value; Raise(); } }
    }

    public IBrush? Stroke
    {
        get => _stroke;
        set { if (!ReferenceEquals(_stroke, value)) { _stroke = value; Raise(); } }
    }

    public double? MinY
    {
        get => _minY;
        set { if (!Nullable.Equals(_minY, value)) { _minY = value; Raise(); } }
    }

    public double? MaxY
    {
        get => _maxY;
        set { if (!Nullable.Equals(_maxY, value)) { _maxY = value; Raise(); } }
    }

    public double Height
    {
        get => _height;
        set { if (Math.Abs(_height - value) > 0.01) { _height = value; Raise(); } }
    }

    private void Raise([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    public event PropertyChangedEventHandler? PropertyChanged;
}

/// <summary>
/// Row de template do usuário (P6.6 do WPF): texto livre com placeholders resolvidos
/// por tick ({Chave}, {Chave:N}, {padrão*}, nome amigável); escondida enquanto
/// algum placeholder não resolve. A cor vem do config (hex) — dados, não token.
/// </summary>
public sealed class OverlayTemplateRowViewModel : INotifyPropertyChanged
{
    private string _text = "";
    private IBrush? _textBrush;
    private bool _isVisible;

    public OverlayTemplateRowViewModel(string identity)
    {
        Identity = identity;
    }

    /// <summary>Chave estável ("#template:&lt;texto&gt;").</summary>
    public string Identity { get; }

    public string Text
    {
        get => _text;
        set { if (_text != value) { _text = value; Raise(); } }
    }

    public IBrush? TextBrush
    {
        get => _textBrush;
        set { if (!ReferenceEquals(_textBrush, value)) { _textBrush = value; Raise(); } }
    }

    /// <summary>false enquanto algum placeholder não resolve (row escondida).</summary>
    public bool IsVisible
    {
        get => _isVisible;
        set { if (_isVisible != value) { _isVisible = value; Raise(); } }
    }

    private void Raise([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    public event PropertyChangedEventHandler? PropertyChanged;
}
