using System.Net;
using System.Text.Json;

namespace PhantomMonitor.Services.Update;

/// <summary>
/// Consulta releases na API pública do GitHub (sem autenticação: 60
/// requisições/hora por IP, folgado para a cadência de arranque + 24h).
/// Resposta vazia ou 404 (nenhuma release publicada) devolve null — "nada
/// disponível"; erros de rede/limite viram exceção e chegam ao serviço como
/// falha registrada no ErrorLog. Com canal beta, devolve a release mais nova
/// não-rascunho a partir de <c>/releases</c> (inclui pré-releases).
/// </summary>
public static class GitHubReleaseSource
{
    private const string ApiBase = "https://api.github.com/repos";

    // Cliente compartilhado: criar HttpClient por consulta esgota sockets.
    private static readonly HttpClient SharedClient = CreateSharedClient();

    public static async Task<GitHubReleaseInfo?> GetLatestAsync(
        string repository, bool includePreRelease, CancellationToken cancellation = default)
    {
        var url = includePreRelease
            ? $"{ApiBase}/{repository}/releases?per_page=10"
            : $"{ApiBase}/{repository}/releases/latest";

        using var response = await SharedClient
            .GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellation)
            .ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellation).ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellation).ConfigureAwait(false);
        var root = document.RootElement;

        if (!includePreRelease)
            return root.ValueKind == JsonValueKind.Object ? GitHubReleaseInfo.FromJson(root) : null;

        // Canal beta: a API devolve as mais novas primeiro, inclusive pré-releases.
        if (root.ValueKind != JsonValueKind.Array) return null;
        foreach (var item in root.EnumerateArray())
        {
            var release = GitHubReleaseInfo.FromJson(item);
            if (release is not null) return release;
        }

        return null;
    }

    /// <summary>
    /// Baixa um anexo da release direto para o arquivo de destino, reportando
    /// progresso 0..100 quando o Content-Length é conhecido. Falha com
    /// IOException quando o arquivo chega truncado (download interrompido).
    /// </summary>
    public static async Task DownloadFileAsync(
        GitHubReleaseAsset asset, string destinationPath, Action<double>? progress = null, CancellationToken cancellation = default)
    {
        using var response = await SharedClient
            .GetAsync(asset.Url, HttpCompletionOption.ResponseHeadersRead, cancellation)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        await using var source = await response.Content.ReadAsStreamAsync(cancellation).ConfigureAwait(false);
        var total = response.Content.Headers.ContentLength ?? (asset.Size > 0 ? asset.Size : 0);
        await using var destination = File.Create(destinationPath);

        var buffer = new byte[64 * 1024];
        long written = 0;
        int read;

        while ((read = await source.ReadAsync(buffer, cancellation).ConfigureAwait(false)) > 0)
        {
            await destination.WriteAsync(buffer.AsMemory(0, read), cancellation).ConfigureAwait(false);
            written += read;
            if (progress is not null && total > 0)
                progress(Math.Clamp(written * 100.0 / total, 0, 100));
        }

        if (total > 0 && written != total)
            throw new IOException($"Download truncado: {written} de {total} bytes recebidos.");
    }

    private static HttpClient CreateSharedClient()
    {
        var handler = new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(10),
            AutomaticDecompression = DecompressionMethods.All,
        };
        var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(20) };
        // A API do GitHub exige User-Agent; identificamos o app para não ser bloqueado.
        client.DefaultRequestHeaders.UserAgent.ParseAdd("PhantomMonitor-Updater");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }
}
