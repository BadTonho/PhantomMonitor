namespace PhantomMonitor.Services.Update;

/// <summary>Ciclo de vida da verificação/aplicação de atualização.</summary>
public enum UpdateStatus
{
    /// <summary>Nunca checou nesta sessão (ou checagem automática desligada).</summary>
    Idle,

    /// <summary>Consulta ao GitHub em curso.</summary>
    Checking,

    /// <summary>Verificado e nenhuma versão mais nova disponível.</summary>
    UpToDate,

    /// <summary>Existe versão mais nova (aguardando ação do usuário).</summary>
    Available,

    /// <summary>Baixando o arquivo da release para o staging.</summary>
    Downloading,

    /// <summary>Baixado e pronto para "Atualizar e reiniciar".</summary>
    ReadyToApply,

    /// <summary>Troca em curso (o app é encerrado como parte da troca).</summary>
    Applying,

    /// <summary>A última operação falhou; nova consulta é permitida.</summary>
    Failure,
}

/// <summary>
/// Foto imutável do estado. O evento chega no thread do responsável pela
/// mudança; quem consume (UI) faz o marshal para o thread de interface.
/// </summary>
public sealed record UpdateState
{
    public required UpdateStatus Status { get; init; }
    public required string CurrentVersion { get; init; }

    /// <summary>True quando o executável não tem versão comparável: nunca checa nem atualiza.</summary>
    public bool IsDevelopmentBuild { get; init; }

    public string? AvailableVersion { get; init; }
    public string? ReleaseNotes { get; init; }

    /// <summary>Release marcada como pré-release no GitHub (canal beta).</summary>
    public bool IsPreRelease { get; init; }

    public double? DownloadProgressPercent { get; init; }
    public DateTime? LastChecked { get; init; }

    /// <summary>Texto amigável opcional (aviso ou erro da última operação).</summary>
    public string? Message { get; init; }
}

/// <summary>
/// Serviço de verificação de atualizações via GitHub Releases, compartilhado
/// pelos dois frontends (WPF no Windows, Avalonia no Linux). Aqui ficam só a
/// consulta ao GitHub e o comparador de versões (nunca rebaixa); a aplicação
/// da atualização (download, staging e troca de arquivos) fica nos appliers
/// por plataforma, ligados nas etapas seguintes do recurso.
/// </summary>
public sealed class UpdateService
{
    /// <summary>Repositório padrão quando <c>update.repository</c> está vazio.</summary>
    public const string DefaultRepository = "BadTonho/PhantomMonitor";

    private readonly object _gate = new();
    private readonly string _repository;
    private readonly Func<bool>? _includePreRelease; // consultado por verificação (toggle beta vivo)
    private readonly Func<bool>? _autoCheck;         // consultado pelo agendador dos frontends
    private UpdateState _state;
    private bool _checking;

    /// <param name="repository">
    /// "owner/repo", URL do GitHub ou vazio (padrão embutido); inválido cai no
    /// padrão com nota no ErrorLog.
    /// </param>
    /// <param name="autoCheck">Retorno do toggle "checar automaticamente" (lido a cada checagem).</param>
    /// <param name="includePreRelease">Retorno do toggle de canal beta (lido a cada checagem).</param>
    public UpdateService(string? repository = null, Func<bool>? autoCheck = null, Func<bool>? includePreRelease = null)
    {
        var fixedRepository = NormalizeRepository(repository);
        if (fixedRepository is null)
        {
            fixedRepository = DefaultRepository;
            ErrorLog.Write("UpdateService", $"Repositório inválido '{repository}'; usando o padrão ({DefaultRepository}).");
        }
        _repository = fixedRepository;
        _autoCheck = autoCheck;
        _includePreRelease = includePreRelease;
        _state = new UpdateState
        {
            Status = UpdateStatus.Idle,
            CurrentVersion = UpdateVersion.Current,
            IsDevelopmentBuild = UpdateVersion.IsDevelopmentBuild,
        };
    }

    /// <summary>Foto atual do estado.</summary>
    public UpdateState State { get { lock (_gate) return _state; } }

    /// <summary>Disparado a cada mudança de estado, no thread do responsável (caller); a UI faz marshal.</summary>
    public event Action<UpdateState>? StateChanged;

    /// <summary>
    /// Aceita "owner/repo", URL do GitHub ("https://github.com/owner/repo.git")
    /// ou vazio/whitespace (padrão embutido); devolve null quando inválido.
    /// </summary>
    public static string? NormalizeRepository(string? configured)
    {
        var value = (configured ?? "").Trim();
        if (value.Length == 0) return DefaultRepository;

        var marker = value.IndexOf("github.com/", StringComparison.OrdinalIgnoreCase);
        if (marker >= 0) value = value[(marker + "github.com/".Length)..];
        value = value.Trim('/');
        if (value.EndsWith(".git", StringComparison.OrdinalIgnoreCase)) value = value[..^4];

        if (value.Split('/') is not [var owner, var name] || owner.Length == 0 || name.Length == 0)
            return null;

        return $"{owner}/{name}";
    }

    /// <summary>Toggle "checar automaticamente": falso (ou build dev) = o agendador não agenda.</summary>
    public bool ShouldAutoCheck => _autoCheck?.Invoke() is not false && !UpdateVersion.IsDevelopmentBuild;

    /// <summary>
    /// Consulta o GitHub, compara (nunca rebaixa) e publica o estado. Consultas
    /// concorrentes são ignoradas; falha de rede/limite loga no ErrorLog e vira
    /// estado Failure com mensagem curta para a UI. Em build de desenvolvimento,
    /// é no-op silencioso.
    /// </summary>
    public async Task CheckAsync(CancellationToken cancellation = default)
    {
        if (UpdateVersion.IsDevelopmentBuild) return;

        lock (_gate)
        {
            if (_checking) return;
            _checking = true;
        }

        try
        {
            Publish(new UpdateState
            {
                Status = UpdateStatus.Checking,
                CurrentVersion = UpdateVersion.Current,
            });

            var checkedAt = DateTime.Now;
            var release = await GitHubReleaseSource
                .GetLatestAsync(_repository, _includePreRelease?.Invoke() ?? false, cancellation)
                .ConfigureAwait(false);

            if (release is null)
            {
                Publish(new UpdateState
                {
                    Status = UpdateStatus.UpToDate,
                    CurrentVersion = UpdateVersion.Current,
                    LastChecked = checkedAt,
                    Message = "Nenhuma release publicada no repositório ainda.",
                });
                return;
            }

            if (!UpdateVersion.TryParse(release.Tag, out var availableVersion))
            {
                Publish(new UpdateState
                {
                    Status = UpdateStatus.UpToDate,
                    CurrentVersion = UpdateVersion.Current,
                    LastChecked = checkedAt,
                    Message = $"Tag '{release.Tag}' não contém uma versão válida; ignorada.",
                });
                return;
            }

            var current = UpdateVersion.CurrentParsed ?? default;
            if (availableVersion.CompareTo(current) <= 0)
            {
                Publish(new UpdateState
                {
                    Status = UpdateStatus.UpToDate,
                    CurrentVersion = UpdateVersion.Current,
                    LastChecked = checkedAt,
                });
                return;
            }

            Publish(new UpdateState
            {
                Status = UpdateStatus.Available,
                CurrentVersion = UpdateVersion.Current,
                AvailableVersion = availableVersion.ToString(),
                ReleaseNotes = release.Notes,
                IsPreRelease = release.PreRelease,
                LastChecked = checkedAt,
            });
        }
        catch (OperationCanceledException)
        {
            Publish(new UpdateState { Status = UpdateStatus.Idle, CurrentVersion = UpdateVersion.Current });
        }
        catch (Exception ex)
        {
            ErrorLog.Write("UpdateService.Check", ex);
            Publish(new UpdateState
            {
                Status = UpdateStatus.Failure,
                CurrentVersion = UpdateVersion.Current,
                Message = $"Falha ao consultar atualizações: {ex.Message}",
            });
        }
        finally
        {
            lock (_gate) _checking = false;
        }
    }

    private void Publish(UpdateState state)
    {
        lock (_gate) _state = state;
        StateChanged?.Invoke(state);
    }
}
