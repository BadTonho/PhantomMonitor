using System.Collections.Generic;
using PhantomMonitor.Services;

namespace PhantomMonitor.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    private string _attentionMessage = "";
    private string _dismissedAttentionMessage = "";

    /// <summary>Aviso exibido na visão geral quando algo impede o funcionamento esperado.</summary>
    public string AttentionMessage
    {
        get => _attentionMessage;
        private set
        {
            if (Set(ref _attentionMessage, value)) Raise(nameof(HasAttentionMessage));
        }
    }

    public bool HasAttentionMessage => AttentionMessage.Length > 0 && AttentionMessage != _dismissedAttentionMessage;

    /// <summary>Oculta o aviso atual até que surja uma mensagem diferente.</summary>
    public void DismissAttentionMessage()
    {
        if (string.IsNullOrEmpty(AttentionMessage)) return;
        _dismissedAttentionMessage = AttentionMessage;
        Raise(nameof(HasAttentionMessage));
    }

    private void RefreshAttentionMessage()
    {
        var messages = new List<string>();
        if (_sensorService is IOverlayProcessDiscovery discovery
            && (discovery.OverlayDiscoveryDiagnostic ?? "").StartsWith("Requer", StringComparison.Ordinal))
        {
            messages.Add("FPS indisponível: execute como administrador ou adicione sua conta ao grupo Performance Log Users.");
        }
        if (OverlayHotkeyStatus is { Length: > 0 } hotkeyStatus
            && hotkeyStatus.StartsWith("Falha", StringComparison.Ordinal))
        {
            messages.Add(hotkeyStatus);
        }

        var next = string.Join("  ", messages);
        if (next != _dismissedAttentionMessage)
            _dismissedAttentionMessage = "";
        AttentionMessage = next;
        Raise(nameof(HasAttentionMessage));
    }
}
