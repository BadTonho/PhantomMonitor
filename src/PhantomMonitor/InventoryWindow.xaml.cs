using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using PhantomMonitor.Models;
using PhantomMonitor.Services;

namespace PhantomMonitor;

/// <summary>
/// Janela de inventário: lista as séries publicadas no snapshot atual (útil para descobrir
/// chaves para alertas/whitelist do OSD) e a árvore de hardware detectada pelo serviço.
/// Atualiza a cada 2 s a partir do snapshot mais recente do serviço ativo.
/// </summary>
public partial class InventoryWindow : Window
{
    private readonly Func<SensorData?> _snapshotProvider;
    private readonly Action<string>? _resetStats;
    private readonly DispatcherTimer _refreshTimer;

    public InventoryWindow(Func<SensorData?> snapshotProvider, Action<string>? resetStats = null)
    {
        _snapshotProvider = snapshotProvider;
        _resetStats = resetStats;

        InitializeComponent();
        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _refreshTimer.Tick += (_, _) => Refresh();

        Loaded += (_, _) => { Refresh(); _refreshTimer.Start(); };
        Closed += (_, _) => _refreshTimer.Stop();
    }

    private void OnResetStatsClick(object sender, RoutedEventArgs e)
    {
        _resetStats?.Invoke("inventário");
        Refresh();
    }

    private void Refresh()
    {
        var data = _snapshotProvider();
        if (data == null)
        {
            SummaryText.Text = "Sem dados — inicie o monitoramento.";
            return;
        }

        var series = InventoryBuilder.BuildSeries(data);
        var hardware = InventoryBuilder.BuildHardware(data);
        var monitors = data.Monitors;

        SummaryText.Text = $"{hardware.Count} nós de hardware • {series.Count} séries publicadas • " +
                           $"{monitors.Count} monitor(es) • atualizado às {data.Timestamp:HH:mm:ss}";

        SeriesGrid.ItemsSource = series;
        MonitorsGrid.ItemsSource = monitors;
        RefreshText.Text = $"Atualiza automaticamente a cada 2 s (snapshot atual). Mín/Máx/Média são da sessão desde o último \"Resetar estatísticas\".";

        // Árvore só é reconstruída se mudou (a enumeração do hardware é estável na prática;
        // evita colapsar nós que o usuário abriu a cada 2 s). A indentação do rastro
        // marca a hierarquia (placa-mãe → SuperIO, EC, etc.).
        var signature = string.Join("\n", hardware.Select(h => $"{h.Depth}:{h.Type}:{h.Name}"));
        if (HardwareTree.Tag as string == signature) return;
        HardwareTree.Tag = signature;

        HardwareTree.Items.Clear();
        if (hardware.Count == 0)
        {
            HardwareTree.Items.Add(new TreeViewItem
            {
                Header = "Nenhum hardware enumerado (inicie o monitoramento como Administrador).",
                IsExpanded = true
            });
            return;
        }

        var ancestors = new List<TreeViewItem>();
        foreach (var node in hardware)
        {
            while (ancestors.Count > node.Depth) ancestors.RemoveAt(ancestors.Count - 1);

            var item = new TreeViewItem { Header = node.ToString(), IsExpanded = true };
            if (node.Depth == 0) HardwareTree.Items.Add(item);
            else ancestors[^1].Items.Add(item);
            ancestors.Add(item);
        }
    }
}
