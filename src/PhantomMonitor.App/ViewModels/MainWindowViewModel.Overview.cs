using System.Diagnostics;
using System.Runtime.Versioning;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using PhantomMonitor.Models;
using PhantomMonitor.Services;

namespace PhantomMonitor.App.ViewModels;

// Porta dos extras de dashboard do WPF (F1.2 da unificação visual): linhas de
// detalhe de CPU/GPU (SensorFormatter do Core), card Placa-mãe & ventoinhas,
// lista de processos na GPU e cores por faixa (normal/morno/quente). As cores
// não trazem hex: todas saem dos tokens dos Themes/Tokens.*.axaml resolvidos
// por chave (equivalente ao FindResource do WPF). O cache vale por sessão e é
// limpo a cada troca de tema em runtime (Fase 3 — App.ThemeChanged): os pincéis
// da VM re-resolvem na próxima repintura (tick de coleta ou repintura forçada
// da VM no evento).
public sealed partial class MainWindowViewModel
{
    private static readonly Dictionary<string, IBrush> DashboardBrushCache = new();

    internal static IBrush ResolveBrush(string tokenKey)
    {
        if (DashboardBrushCache.TryGetValue(tokenKey, out var cached)) return cached;
        var brush = Application.Current?.TryGetResource(tokenKey, null, out var value) == true
            && value is IBrush resolved
            ? resolved
            : Brushes.Gray;
        DashboardBrushCache[tokenKey] = brush;
        return brush;
    }

    /// <summary>
    /// Faixa de temperatura: leitura ausente (muted),
    /// ≤65 verde, ≤80 morno (âmbar), acima = quente (vermelho). A pílula usa o
    /// mesmo tom translúcido do par bg/borda do tema.
    /// </summary>
    private static (IBrush Text, IBrush Pill, IBrush Border) TempVisual(double? temp) =>
        temp is null ? (ResolveBrush("TextMutedBrush"), ResolveBrush("BgChipBrush"), ResolveBrush("BgCardBorderBrush"))
        : temp <= 65 ? (ResolveBrush("AccentGreenBrush"), ResolveBrush("BgTintGreenBrush"), ResolveBrush("BorderTintGreenBrush"))
        : temp <= 80 ? (ResolveBrush("AccentWarningBrush"), ResolveBrush("BgTintWarmBrush"), ResolveBrush("BorderTintWarmBrush"))
        : (ResolveBrush("AccentHotBrush"), ResolveBrush("BgTintHotBrush"), ResolveBrush("BorderTintHotBrush"));

    /// <summary>Faixa de uso do WPF (GetUsageBrush): &lt;75 primário, &lt;90 morno, acima quente.</summary>
    private static IBrush UsageBrush(double percent) =>
        percent < 75 ? ResolveBrush("TextPrimaryBrush")
        : percent < 90 ? ResolveBrush("AccentWarningBrush")
        : ResolveBrush("AccentHotBrush");

    // ---- Linhas de detalhe (SensorFormatter) ----
    public string CpuDetailText { get => _cpuDetailText; private set => Set(ref _cpuDetailText, value); }
    private string _cpuDetailText = "Clock, potência e voltagem indisponíveis";

    public string GpuDetailText { get => _gpuDetailText; private set => Set(ref _gpuDetailText, value); }
    private string _gpuDetailText = "VRAM, clocks e hotspot indisponíveis";

    // ---- Card Placa-mãe & ventoinhas ----
    public string MotherboardTempText { get => _motherboardTempText; private set => Set(ref _motherboardTempText, value); }
    private string _motherboardTempText = "Indisponível";

    public string FanSummaryText { get => _fanSummaryText; private set => Set(ref _fanSummaryText, value); }
    private string _fanSummaryText = "Ventoinhas: --";

    public IBrush MbTempBrush { get => _mbTempBrush; private set => Set(ref _mbTempBrush, value); }
    private IBrush _mbTempBrush = ResolveBrush("TextMutedBrush");

    // ---- Cores por faixa: temperatura (texto + pílula) ----
    public IBrush CpuTempBrush { get => _cpuTempBrush; private set => Set(ref _cpuTempBrush, value); }
    private IBrush _cpuTempBrush = ResolveBrush("TextBodyBrush");

    public IBrush CpuTempPillBrush { get => _cpuTempPillBrush; private set => Set(ref _cpuTempPillBrush, value); }
    private IBrush _cpuTempPillBrush = ResolveBrush("BgChipBrush");

    public IBrush CpuTempPillBorderBrush { get => _cpuTempPillBorderBrush; private set => Set(ref _cpuTempPillBorderBrush, value); }
    private IBrush _cpuTempPillBorderBrush = ResolveBrush("BgCardBorderBrush");

    public IBrush GpuTempBrush { get => _gpuTempBrush; private set => Set(ref _gpuTempBrush, value); }
    private IBrush _gpuTempBrush = ResolveBrush("TextBodyBrush");

    public IBrush GpuTempPillBrush { get => _gpuTempPillBrush; private set => Set(ref _gpuTempPillBrush, value); }
    private IBrush _gpuTempPillBrush = ResolveBrush("BgChipBrush");

    public IBrush GpuTempPillBorderBrush { get => _gpuTempPillBorderBrush; private set => Set(ref _gpuTempPillBorderBrush, value); }
    private IBrush _gpuTempPillBorderBrush = ResolveBrush("BgCardBorderBrush");

    // ---- Cores por faixa: métricas de uso (%, texto grande) ----
    public IBrush CpuUsageBrush { get => _cpuUsageBrush; private set => Set(ref _cpuUsageBrush, value); }
    private IBrush _cpuUsageBrush = ResolveBrush("TextPrimaryBrush");

    public IBrush GpuUsageBrush { get => _gpuUsageBrush; private set => Set(ref _gpuUsageBrush, value); }
    private IBrush _gpuUsageBrush = ResolveBrush("TextPrimaryBrush");

    public IBrush RamUsageBrush { get => _ramUsageBrush; private set => Set(ref _ramUsageBrush, value); }
    private IBrush _ramUsageBrush = ResolveBrush("TextPrimaryBrush");

    // ---- Processos na GPU ----
    public IReadOnlyList<GpuProcessInfo> GpuProcessRows { get => _gpuProcessRows; private set => Set(ref _gpuProcessRows, value); }
    private IReadOnlyList<GpuProcessInfo> _gpuProcessRows = [];

    public bool HasGpuProcesses { get => _hasGpuProcesses; private set => Set(ref _hasGpuProcesses, value); }
    private bool _hasGpuProcesses;

    // ---- Banner de elevação admin (só faz sentido no Windows) ----
    public bool IsAdminBannerVisible { get; private set; } = OperatingSystem.IsWindows() && !IsRunningAsAdmin();

    /// <summary>Reinicia elevado (Verb runas): mesmo fluxo do botão do WPF; recai no log em falha.</summary>
    public void RestartAsAdmin()
    {
        if (!OperatingSystem.IsWindows()) return;
        try
        {
            var exePath = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(exePath))
            {
                ErrorLog.Write("RestartAsAdmin", new InvalidOperationException("Caminho do executável indisponível."));
                return;
            }
            var startInfo = new ProcessStartInfo
            {
                FileName = exePath,
                UseShellExecute = true,
                Verb = "runas"
            };
            foreach (var argument in Environment.GetCommandLineArgs().Skip(1))
            {
                if (!string.Equals(argument, PhantomMonitor.App.Program.WaitForSingleInstanceArgument, StringComparison.Ordinal))
                    startInfo.ArgumentList.Add(argument);
            }
            startInfo.ArgumentList.Add(PhantomMonitor.App.Program.WaitForSingleInstanceArgument);

            using var elevated = Process.Start(startInfo)
                ?? throw new InvalidOperationException("O processo elevado não foi iniciado.");
            Shutdown();
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            // UAC recusado pelo usuário: segue rodando sem elevação (não é falha).
            StatusText = "Elevação cancelada";
        }
        catch (Exception ex)
        {
            ErrorLog.Write("RestartAsAdmin", ex);
            StatusText = "Não foi possível elevar; execute como administrador";
        }
    }

    [SupportedOSPlatform("windows")]
    private static bool IsRunningAsAdmin()
    {
        try
        {
            using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
            return new System.Security.Principal.WindowsPrincipal(identity)
                .IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static void Shutdown()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.Shutdown();
    }

    /// <summary>
    /// Bloco extra do snapshot (hook chamado por UpdateSnapshot): detalhes, faixas
    /// de cor, placa-mãe/ventoinhas e processos na GPU. Barato: só strings/refs.
    /// </summary>
    private void UpdateDashboardExtras(SensorData data)
    {
        CpuDetailText = SensorFormatter.BuildCpuDetail(data)
            ?? "Clock, potência e voltagem indisponíveis";
        GpuDetailText = SensorFormatter.BuildGpuDetail(data)
            ?? "VRAM, clocks e hotspot indisponíveis";

        SetTempState(Has(data, SensorType.CpuTemperature) ? data.GetValue(SensorType.CpuTemperature) : null);
        SetGpuTempState(Has(data, SensorType.GpuTemperature) ? data.GetValue(SensorType.GpuTemperature) : null);
        SetUsageStates(data);
        SetMotherboard(data);
        SetGpuProcesses(data);
    }

    private void SetTempState(double? temp)
    {
        var (text, pill, border) = TempVisual(temp);
        CpuTempBrush = text;
        CpuTempPillBrush = pill;
        CpuTempPillBorderBrush = border;
    }

    private void SetGpuTempState(double? temp)
    {
        var (text, pill, border) = TempVisual(temp);
        GpuTempBrush = text;
        GpuTempPillBrush = pill;
        GpuTempPillBorderBrush = border;
    }

    private void SetUsageStates(SensorData data)
    {
        CpuUsageBrush = UsageBrush(data.GetValue(SensorType.CpuTotal));
        GpuUsageBrush = UsageBrush(data.GetValue(SensorType.GpuCore));
        RamUsageBrush = UsageBrush(data.GetValue(SensorType.RamUsage));
    }

    private void SetMotherboard(SensorData data)
    {
        var mbTemp = data.GetValue(SensorType.MotherboardTemp);
        MotherboardTempText = FormatMotherboardTemperature(data);
        MbTempBrush = TempVisual(Has(data, SensorType.MotherboardTemp) ? mbTemp : null).Text;
        FanSummaryText = SensorFormatter.BuildFanSummary(data);
    }

    internal static string FormatMotherboardTemperature(SensorData data) =>
        Has(data, SensorType.MotherboardTemp) ? $"{data.GetValue(SensorType.MotherboardTemp):F0} °C" : "Indisponível";

    private void SetGpuProcesses(SensorData data)
    {
        var rows = data.GpuProcesses;
        GpuProcessRows = rows.Count > 0 ? [.. rows] : [];
        HasGpuProcesses = rows.Count > 0;
    }
}
