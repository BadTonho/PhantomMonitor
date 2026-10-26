using System.ComponentModel;
using PhantomMonitor.Models;
using PhantomMonitor.Services;

namespace PhantomMonitor.App.ViewModels;

public sealed class OverlayCustomLineViewModel : INotifyPropertyChanged
{
    private readonly OverlayCanvasBlockViewModel _block;
    private string _text = "";
    private double? _fontSize;
    private double? _width;

    public OverlayCustomLineViewModel(OverlayCanvasBlockViewModel block, string id) { _block = block; Id = id; }
    public string Id { get; }
    public string Text => _text;
    public bool IsVisible => !string.IsNullOrEmpty(_text);
    public double FontSize => _fontSize ?? _block.PresentationValueFontSize;
    public double Width => Math.Min(_width ?? AvailableWidth, AvailableWidth);
    private double AvailableWidth => Math.Max(1, _block.PresentationWidth - _block.PresentationOuterMargin.Left
        - _block.PresentationOuterMargin.Right - _block.PresentationPadding.Left - _block.PresentationPadding.Right
        - _block.PresentationRowMargin.Left - _block.PresentationRowMargin.Right);

    public void Update(OverlayCustomLineConfig line, IReadOnlyList<HardwareIdentity> devices)
    {
        var text = OverlayCustomLineRenderer.Render(line, devices);
        if (_text != text) { _text = text; Raise(nameof(Text)); Raise(nameof(IsVisible)); }
        if (_fontSize != line.FontSize) { _fontSize = line.FontSize; Raise(nameof(FontSize)); }
        if (_width != line.Width) { _width = line.Width; Raise(nameof(Width)); }
    }

    public void RefreshPresentation() { Raise(nameof(FontSize)); Raise(nameof(Width)); }
    private void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    public event PropertyChangedEventHandler? PropertyChanged;
}
