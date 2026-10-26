using System.Text.Json;

namespace PhantomMonitor.Services.Update;

/// <summary>Arquivo anexado à release (asset), com nome e URL de download.</summary>
public sealed record GitHubReleaseAsset(string Name, string Url, long Size, string? Digest = null);

/// <summary>
/// Uma release do GitHub já analisada (tag, título, notas e assets). O parser
/// é tolerante a campos ausentes: release sem tag é descartada e rascunho
/// (draft) nunca vira atualização disponível.
/// </summary>
public sealed record GitHubReleaseInfo(
    string Tag,
    string Name,
    string? Notes,
    bool PreRelease,
    IReadOnlyList<GitHubReleaseAsset> Assets)
{
    public static GitHubReleaseInfo? FromJson(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object) return null;
        if (element.TryGetProperty("draft", out var draft) && draft.ValueKind == JsonValueKind.True) return null;

        var tag = GetText(element, "tag_name");
        if (string.IsNullOrWhiteSpace(tag)) return null;

        var assets = new List<GitHubReleaseAsset>();
        if (element.TryGetProperty("assets", out var assetsElement) && assetsElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var asset in assetsElement.EnumerateArray())
            {
                var name = GetText(asset, "name");
                var url = GetText(asset, "browser_download_url");
                if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(url)) continue;
                var size = asset.TryGetProperty("size", out var sizeElement)
                           && sizeElement.ValueKind == JsonValueKind.Number
                           && sizeElement.TryGetInt64(out var parsedSize) ? parsedSize : 0;
                assets.Add(new GitHubReleaseAsset(name, url, size, GetText(asset, "digest")));
            }
        }

        var notes = GetText(element, "body");
        return new GitHubReleaseInfo(
            tag,
            GetText(element, "name") ?? tag,
            string.IsNullOrWhiteSpace(notes) ? null : notes,
            element.TryGetProperty("prerelease", out var preRelease) && preRelease.ValueKind == JsonValueKind.True,
            assets);
    }

    private static string? GetText(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
