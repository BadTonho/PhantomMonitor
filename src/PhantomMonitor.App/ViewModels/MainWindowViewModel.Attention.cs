using System.Collections.Generic;
using PhantomMonitor.Services;

namespace PhantomMonitor.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    private string _attentionMessage = "";

    /// <summary>Aviso exibido na visão geral quando algo impede o funcionamento esperado.</summary>
    public string AttentionMessage
    {
        get => _attentionMessage;
        private set
        {
            if (Set(ref _attentionMessage, value)) Raise(nameof(HasAttentionMessage));
        }
    }

    public bool HasAttentionMessage => AttentionMessage.Length > 0;

    private void RefreshAttentionMessage()
    {
        var messages = new List<string>();
        if (_sensorService is IOverlayProcessDiscovery discovery
            && (discovery.OverlayDiscoveryDiagnostic ?? "").StartsWith("Requer", StringComparison.Ordinal))
        {
            messages.Add("FPS indisponível: execute como administrador ou adicione sua conta ao grupo Performance Log Users.");
        }
        if ((OverlayHotkeyStatus ?? "").StartsWith("Falha", StringComparison.Ordinal))
            messages.Add(OverlayHotkeyStatus);
        AttentionMessage = string.Join("  ", messages);
    }
}
