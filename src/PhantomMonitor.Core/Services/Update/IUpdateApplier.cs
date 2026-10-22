namespace PhantomMonitor.Services.Update;

/// <summary>
/// Estratégia de aplicação de atualização por plataforma. O fluxo completo
/// "Atualizar e reiniciar" é: <see cref="PrepareAsync"/> baixa o asset e monta
/// o staging; <see cref="Apply"/> lança o updater one-shot que espera o app
/// encerrar, troca os arquivos e reabre. Instalações que não podem ser
/// trocadas pelo app (ex.: instalada por pacote do sistema) devolvem
/// <c>CanApply = false</c> e ficam apenas no modo notificação com link.
/// </summary>
public interface IUpdateApplier
{
    /// <summary>Esta instalação pode ser atualizada pela troca de arquivos local?</summary>
    bool CanApply { get; }

    /// <summary>Nome EXATO do asset esperado na release (convenção documentada em docs/atualizações.md).</summary>
    string AssetNameForVersion(string version);

    /// <summary>
    /// Baixa o asset e monta o staging da versão; <paramref name="progress"/>
    /// recebe 0..100 (download e desempacotamento). Devolve a versão preparada.
    /// </summary>
    Task<string> PrepareAsync(GitHubReleaseAsset asset, string version, Action<double>? progress, CancellationToken cancellation);

    /// <summary>
    /// Aplica o staging preparado: lança o updater one-shot e devolve imediatamente.
    /// O app será encerrado pela própria troca (a UI chama Shutdown em seguida) e
    /// o script reabre o executável. Lança exceção se a troca não puder iniciar.
    /// </summary>
    void Apply(string version);
}
