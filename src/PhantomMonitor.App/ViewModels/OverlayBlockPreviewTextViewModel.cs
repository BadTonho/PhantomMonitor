namespace PhantomMonitor.App.ViewModels;

/// <summary>Fallback preview for draft graph/template rows without a live runtime row yet.</summary>
public sealed class OverlayBlockPreviewTextViewModel(string text)
{
    public string Text { get; } = text;
}
