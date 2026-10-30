using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using System.Diagnostics;
using PhantomMonitor.Services;
using PhantomMonitor.Services.Update;

namespace PhantomMonitor.App.ViewModels;

// Atualizações: checagem automática (arranque + DispatcherTimer de 24h), estado para
// o usuário (card de Configurações + toast no canto) e "Atualizar e reiniciar" via o
// applier por SO (F1.5): WindowsUpdateApplier (zip portable + one-shot cmd) ou
// LinuxUpdateApplier (tarball/AppImage; instalação de sistema fica em modo notificação
// com link). Os toggles e o campo de repositório são textos editáveis persistidos pelo
// SaveSettings (ConfigApply).
public sealed partial class MainWindowViewModel
{
    private UpdateService? _updates;
    private DispatcherTimer? _updateTimer;
    private bool _updateAutoCheck;
    private bool _updateBetaChannel;
    private string _updateRepositoryText = "";
    private string _updateVersionText = "";
    private string _updateStatusText = "";
    private string _updateToastText = "";
    private bool _updateToastVisible;
    private string _updateButtonText = "Atualizar e reiniciar";
    private bool _updateApplyVisible;
    private bool _updateApplyEnabled = true;

    public bool UpdateAutoCheck { get => _updateAutoCheck; set => SetSettingsDraft(ref _updateAutoCheck, value); }
    public bool UpdateBetaChannel { get => _updateBetaChannel; set => SetSettingsDraft(ref _updateBetaChannel, value); }
    public string UpdateRepositoryText { get => _updateRepositoryText; set => SetSettingsDraft(ref _updateRepositoryText, value); }

    public string UpdateVersionText { get => _updateVersionText; private set => Set(ref _updateVersionText, value); }
    public string UpdateStatusText { get => _updateStatusText; private set => Set(ref _updateStatusText, value); }
    public string UpdateToastText { get => _updateToastText; private set => Set(ref _updateToastText, value); }
    public bool UpdateToastVisible { get => _updateToastVisible; private set => Set(ref _updateToastVisible, value); }
    public string UpdateButtonText { get => _updateButtonText; private set => Set(ref _updateButtonText, value); }
    public bool CanApplyUpdate { get => _updateApplyVisible; private set => Set(ref _updateApplyVisible, value); }
    public bool CanApplyUpdateEnabled { get => _updateApplyEnabled; private set => Set(ref _updateApplyEnabled, value); }

    // Badge compacto no ESTADO (sidebar, porta do WPF — F1.2): resumo em uma linha,
    // clique leva ao card completo de atualizações nas Configurações.
    public string UpdateBadgeText
    {
        get => _updateBadgeText;
        private set
        {
            if (Set(ref _updateBadgeText, value)) OnNavigationStatusChanged();
        }
    }
    private string _updateBadgeText = "";

    public bool UpdateBadgeVisible
    {
        get => _updateBadgeVisible;
        private set
        {
            if (Set(ref _updateBadgeVisible, value)) OnNavigationStatusChanged();
        }
    }
    private bool _updateBadgeVisible;

    /// <summary>Liga o serviço do Core: cria o updater, faz a checagem inicial e o timer 24h.</summary>
    internal void InitUpdateService()
    {
        _updateAutoCheck = _config.Update.AutoCheck;
        _updateBetaChannel = _config.Update.CheckBeta;
        _updateRepositoryText = _config.Update.Repository;
        _updateVersionText = $"Versão instalada: v{UpdateVersion.Current}";

        // Applier por SO (F1.5): o zip com one-shot cmd no Windows, tarball/AppImage no Linux.
        _updates = new UpdateService(
            _config.Update.Repository,
            autoCheck: () => _config.Update.AutoCheck,
            includePreRelease: () => _config.Update.CheckBeta,
            applier: OperatingSystem.IsWindows() ? WindowsUpdateApplier.Shared : LinuxUpdateApplier.Shared);
        _updates.StateChanged += state => Dispatcher.UIThread.Post(() => OnUpdateStateChanged(state));

        if (UpdateVersion.IsDevelopmentBuild)
        {
            _updateStatusText = "Build de desenvolvimento: verificação de atualização desativada.";
        }
        else if (_updates.ShouldAutoCheck)
        {
            _ = _updates.CheckAsync();
        }

        _updateTimer = new DispatcherTimer(TimeSpan.FromHours(24), DispatcherPriority.Background, OnUpdateTimerTick);
        _updateTimer.Start();
    }

    private void OnUpdateTimerTick(object? sender, EventArgs e)
    {
        // ShouldAutoCheck já embute o toggle e a trava de build de desenvolvimento.
        if (_updates is { } updates && updates.ShouldAutoCheck) _ = updates.CheckAsync();
    }

    /// <summary>Botão manual: checa independentemente do toggle (dev é no-op guardado no serviço).</summary>
    public void CheckUpdatesNow() => _updates?.CheckAsync();

    public void OpenReleasesPage()
    {
        try
        {
            var repository = UpdateService.NormalizeRepository(_config.Update.Repository) ?? UpdateService.DefaultRepository;
            // UseShellExecute resolve xdg-open/gio do desktop no Linux.
            Process.Start(new ProcessStartInfo
            {
                FileName = $"https://github.com/{repository}/releases/latest",
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            ErrorLog.Write("Update.OpenReleases", ex);
        }
    }

    public async Task ApplyUpdateAsync()
    {
        if (_updates is { } updates) await updates.UpdateAndRestartAsync();
    }

    /// <summary>Estado do updater -> UI (toast + card); roda já no dispatcher (UIThread.Post).</summary>
    private void OnUpdateStateChanged(UpdateState state)
    {
        var versionLine = $"Versão instalada: v{state.CurrentVersion}";
        if (state.LastChecked is { } lastCheck)
            versionLine += $" · Última checagem: {lastCheck:dd/MM/yyyy HH:mm}";
        UpdateVersionText = versionLine;

        UpdateStatusText = state.Status switch
        {
            UpdateStatus.Checking => "Verificando atualizações...",
            UpdateStatus.Downloading => state.DownloadProgressPercent is { } percent
                ? $"Baixando atualização: {percent:F0} %"
                : "Baixando atualização...",
            UpdateStatus.ReadyToApply => "Atualização pronta; aplicando...",
            UpdateStatus.Applying => "Trocando arquivos e reiniciando...",
            UpdateStatus.Available when state.AvailableVersion is { } version =>
                $"Nova versão {version} disponível.",
            UpdateStatus.UpToDate => "Nenhuma atualização disponível.",
            _ => "",
        };
        if (state.Message is { } message) UpdateStatusText = message;

        var showNotice = state.Status is UpdateStatus.Available
            or UpdateStatus.Downloading or UpdateStatus.ReadyToApply or UpdateStatus.Applying;
        UpdateToastVisible = showNotice;

        // Badge da sidebar (F1.2): linha única, some quando nada está acontecendo.
        UpdateBadgeVisible = showNotice;
        UpdateBadgeText = state.Status switch
        {
            UpdateStatus.Downloading => state.DownloadProgressPercent is { } badgePercent
                ? $"Baixando: {badgePercent:F0} %"
                : "Baixando...",
            UpdateStatus.ReadyToApply => "Pronto para reiniciar",
            UpdateStatus.Applying => "Trocando versão...",
            UpdateStatus.Available when state.AvailableVersion is { } badgeVersion =>
                $"Nova versão: {badgeVersion}",
            _ => "",
        };
        UpdateToastText = state.Status switch
        {
            UpdateStatus.Downloading => state.DownloadProgressPercent is { } percent
                ? $"Baixando: {percent:F0} %"
                : "Baixando...",
            UpdateStatus.ReadyToApply => "Atualização pronta; reiniciando...",
            UpdateStatus.Applying => "Trocando arquivos e reiniciando a nova versão...",
            UpdateStatus.Available when state.AvailableVersion is { } version =>
                $"Phantom Monitor {version}" + (state.IsPreRelease ? " (pré-release, canal beta)" : "") + " disponível.",
            _ => "",
        };

        var canApply = showNotice;
        if (state.Status == UpdateStatus.Available && _updates is { } updates)
            canApply = updates.CanUpdateInApp;
        CanApplyUpdate = canApply;
        CanApplyUpdateEnabled = state.Status is not (UpdateStatus.Downloading or UpdateStatus.ReadyToApply or UpdateStatus.Applying);
        UpdateButtonText = state.Status switch
        {
            UpdateStatus.Downloading => state.DownloadProgressPercent is { } percent
                ? $"Baixando: {percent:F0} %"
                : "Baixando...",
            UpdateStatus.ReadyToApply => "Aplicando atualização...",
            UpdateStatus.Applying => "Reiniciando...",
            _ => "Atualizar e reiniciar",
        };

        // O one-shot assume a troca: encerra o app agora (configs/logs ficam fora da pasta trocada).
        if (state.Status == UpdateStatus.Applying) ShutdownNow();
    }

    private static void ShutdownNow()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.Shutdown();
    }
}
