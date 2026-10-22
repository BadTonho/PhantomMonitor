using System.Collections.ObjectModel;

namespace PhantomMonitor.App.ViewModels;

/// <summary>
/// Nó da árvore de hardware do inventário, reconstruída das linhas <c>[HWR]</c>
/// do snapshot (Core <c>InventoryBuilder.BuildHardware</c>, como no InventoryWindow
/// do WPF). As VMs são recriadas apenas quando a assinatura da enumeração muda —
/// reconstruir a cada snapshot colapsaria os nós que o usuário abriu.
/// </summary>
public sealed class InventoryHardwareNodeViewModel
{
    public InventoryHardwareNodeViewModel(string title)
    {
        Title = title;
    }

    /// <summary>Linha do nó no formato do WPF: "Placa-mãe: Nuvoton NCT6791D".</summary>
    public string Title { get; }

    /// <summary>Filhos do nó, alimentados pela profundidade da enumeração (Depth do plano).</summary>
    public ObservableCollection<InventoryHardwareNodeViewModel> Children { get; } = [];
}
