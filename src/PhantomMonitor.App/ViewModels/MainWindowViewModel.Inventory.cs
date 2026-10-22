using System.Collections.ObjectModel;
using Avalonia.Threading;
using PhantomMonitor.App.ViewModels;
using PhantomMonitor.Models;
using PhantomMonitor.Services;

namespace PhantomMonitor.App.ViewModels;

// Partes do inventário do WPF portadas na F1.4 (InventoryWindow): árvore de
// hardware reconstruída apenas quando a enumeração muda (assinatura dos nós,
// como no WPF — evita colapsar a árvore a cada snapshot), lista de monitores
// (EDID) reconciliada por identidade e reset das estatísticas de sessão com
// marcador #marker no CSV ativo + nota no journal JSONL (mesma semântica do
// ResetSeriesStats do WPF, que também serve à bandeja na F1.5).
public sealed partial class MainWindowViewModel
{
    public ObservableCollection<InventoryHardwareNodeViewModel> InventoryHardwareNodes => _inventoryHardwareNodes;
    private readonly ObservableCollection<InventoryHardwareNodeViewModel> _inventoryHardwareNodes = new();
    private string _inventoryHardwareSignature = "";

    /// <summary>Total de nós da árvore (raiz + filhos), para o texto-resumo.</summary>
    private int _inventoryHardwareNodeCount;

    public ObservableCollection<MonitorInfo> Monitors => _inventoryMonitors;
    private readonly ObservableCollection<MonitorInfo> _inventoryMonitors = new();
    private string _inventoryMonitorsSignature = "";
    private int _inventoryMonitorCount;

    public bool HasInventoryHardware { get => _hasInventoryHardware; private set => Set(ref _hasInventoryHardware, value); }
    private bool _hasInventoryHardware;

    public bool HasMonitors { get => _hasMonitors; private set => Set(ref _hasMonitors, value); }
    private bool _hasMonitors;

    /// <summary>Confirmação transitória do reset (aparece junto do botão e some).</summary>
    public string InventoryActionStatusText { get => _inventoryActionStatusText; private set => Set(ref _inventoryActionStatusText, value); }
    private string _inventoryActionStatusText = "";

    public bool HasInventoryActionStatus { get => _hasInventoryActionStatus; private set => Set(ref _hasInventoryActionStatus, value); }
    private bool _hasInventoryActionStatus;

    private DispatcherTimer? _inventoryStatusClearTimer;

    /// <summary>Chamado por UpdateSnapshot: árvore + monitores com guardas por assinatura.</summary>
    private void UpdateInventorySession(SensorData data)
    {
        UpdateInventoryHardwareTree(data);
        UpdateMonitors(data);
    }

    /// <summary>Árvore de hardware: rebuild só quando a enumeração [HWR] mudou de fato.</summary>
    private void UpdateInventoryHardwareTree(SensorData data)
    {
        var nodes = InventoryBuilder.BuildHardware(data);
        var signature = string.Join("\n", nodes.Select(n => $"{n.Depth}:{n.Type}:{n.Name}"));
        if (signature == _inventoryHardwareSignature) return;
        _inventoryHardwareSignature = signature;

        var roots = new List<InventoryHardwareNodeViewModel>();
        var stack = new List<InventoryHardwareNodeViewModel>();
        foreach (var node in nodes)
        {
            // O builder devolve os nós já em ordem DFS com Depth = indentação/2
            // (2 espaços por nível); o desempilhamento reconstitui os pais.
            var item = new InventoryHardwareNodeViewModel(node.ToString());
            while (stack.Count > node.Depth) stack.RemoveAt(stack.Count - 1);
            if (node.Depth == 0) roots.Add(item);
            else stack[^1].Children.Add(item);
            stack.Add(item);
        }

        _inventoryHardwareNodes.Clear();
        foreach (var root in roots) _inventoryHardwareNodes.Add(root);
        _inventoryHardwareNodeCount = CountNodes();
        HasInventoryHardware = _inventoryHardwareNodes.Count > 0;
    }

    private int CountNodes()
    {
        var count = 0;
        foreach (var root in _inventoryHardwareNodes)
        {
            count++;
            count += Count(root);
        }
        return count;

        int Count(InventoryHardwareNodeViewModel node)
        {
            var sum = node.Children.Count;
            foreach (var child in node.Children) sum += Count(child);
            return sum;
        }
    }

    /// <summary>Monitores (EDID): MonitorInfo é POCO — as instâncias são recriadas por
    /// snapshot, então a coleção só troca quando o conjunto de identidades muda.</summary>
    private void UpdateMonitors(SensorData data)
    {
        var monitors = data.Monitors ?? [];
        var signature = string.Join("|",
            monitors.Select(m => $"{m.InstanceName}:{m.Manufacturer}:{m.Model}:{m.Serial}:{m.DiagonalText}:{m.MadeText}"));
        if (signature == _inventoryMonitorsSignature) return;
        _inventoryMonitorsSignature = signature;

        _inventoryMonitors.Clear();
        foreach (var monitor in monitors) _inventoryMonitors.Add(monitor);
        _inventoryMonitorCount = _inventoryMonitors.Count;
        HasMonitors = _inventoryMonitors.Count > 0;
    }

    /// <summary>Reset das estatísticas por série da sessão (min/max/média) + marcador
    /// nos logs ativos, igual ao ResetSeriesStats do WPF pedido pela janela de inventário.</summary>
    public void ResetSessionStats()
    {
        try
        {
            _sensorService?.ResetSeriesStats();
        }
        catch
        {
            // Serviço em troca (config apply) não precisa quebrar o botão.
        }

        _csvLogger?.WriteMarker("reset (inventário)");
        _jsonJournalLogger?.WriteNote("reset (inventário)");

        // Reflete na hora: reconciliação adiantada (o próximo snapshot reconciliaria
        // do mesmo jeito, mas alterar a sessão inteira para "--" já ali confirma o clique).
        if (_latestSensorData is { } data)
            ReconcileInventoryGroups(SensorInventoryBuilder.Plan(data));

        InventoryActionStatusText = "Estatísticas resetadas · marcador gravado no CSV/JSONL em uso";
        HasInventoryActionStatus = true;
        ScheduleInventoryStatusClear();
    }

    private void ScheduleInventoryStatusClear()
    {
        _inventoryStatusClearTimer ??= new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
        _inventoryStatusClearTimer.Tick -= OnInventoryStatusClearTick;
        _inventoryStatusClearTimer.Tick += OnInventoryStatusClearTick;
        _inventoryStatusClearTimer.Stop();
        _inventoryStatusClearTimer.Start();
    }

    private void OnInventoryStatusClearTick(object? sender, EventArgs e)
    {
        _inventoryStatusClearTimer?.Stop();
        InventoryActionStatusText = "";
        HasInventoryActionStatus = false;
    }
}
