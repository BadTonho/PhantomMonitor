using System.Globalization;
using System.Reflection;

namespace PhantomMonitor.Services.Update;

/// <summary>
/// Versão SemVer simplificada (major.minor.patch + sufixo de pré-release).
/// Suficiente para comparar tags <c>v1.2.3</c> com a versão compilada no
/// executável; não implementa precedência completa do SemVer por segmentos
/// separados por ponto no sufixo (ex.: "rc.1" vs "rc.2" é textual).
/// </summary>
public readonly record struct SemanticVersion(int Major, int Minor, int Patch, string PreRelease)
    : IComparable<SemanticVersion>
{
    /// <summary>Versão 0.0.0 — marcador de builds de desenvolvimento (nunca atualiza).</summary>
    public bool IsZero => Major == 0 && Minor == 0 && Patch == 0;

    public int CompareTo(SemanticVersion other)
    {
        if (Major != other.Major) return Major.CompareTo(other.Major);
        if (Minor != other.Minor) return Minor.CompareTo(other.Minor);
        if (Patch != other.Patch) return Patch.CompareTo(other.Patch);
        return ComparePreRelease(PreRelease, other.PreRelease);
    }

    /// <summary>
    /// Empate nos números: pré-release é sempre MENOR que a estável ("1.2.3-rc"
    /// &lt; "1.2.3"); entre duas pré-releases, comparação textual simples.
    /// </summary>
    private static int ComparePreRelease(string left, string right)
    {
        bool hasLeft = left.Length > 0;
        bool hasRight = right.Length > 0;
        if (hasLeft != hasRight) return hasLeft ? -1 : 1;
        return string.Compare(left, right, StringComparison.OrdinalIgnoreCase);
    }

    public override string ToString() =>
        $"{Major}.{Minor}.{Patch}" + (PreRelease.Length > 0 ? $"-{PreRelease}" : "");
}

/// <summary>Resolve e compara a versão do executável com tags de release.</summary>
public static class UpdateVersion
{
    private static string? _current;
    private static SemanticVersion? _currentParsed;

    /// <summary>
    /// Versão do executável em execução, tomada da InformationalVersion (definida
    /// pelo <c>&lt;Version&gt;</c> do csproj no publish) sem o sufixo "+metadados".
    /// Builds de desenvolvimento trazem o marcador "0.0.0-dev".
    /// </summary>
    public static string Current => _current ??= ResolveCurrent();

    /// <summary>Versão atual analisada; null quando não é SemVer (build de desenvolvimento).</summary>
    public static SemanticVersion? CurrentParsed =>
        _currentParsed ??= TryParse(Current, out var version) ? version : null;

    /// <summary>
    /// Build de desenvolvimento não participa de atualização: versão não-SemVer
    /// ou 0.0.0 (marcador "-dev") nunca é comparada com tags de release.
    /// </summary>
    public static bool IsDevelopmentBuild => _currentParsed is null || _currentParsed.Value.IsZero;

    /// <summary>
    /// Aceita "1.2.3", "v1.2.3", "1.2.3-beta" e "1.2.3+build"; rejeita texto
    /// sem números ou com mais de 3 componentes (strict: desvia para dev).
    /// </summary>
    public static bool TryParse(string? text, out SemanticVersion version)
    {
        version = default;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var value = text.Trim();
        if (value.Length > 0 && value[0] is 'v' or 'V') value = value[1..];

        // "+build" é metadado e não participa da comparação.
        var plus = value.IndexOf('+');
        if (plus >= 0) value = value[..plus];

        var dash = value.IndexOf('-');
        var preRelease = dash >= 0 ? value[(dash + 1)..] : "";
        if (dash >= 0) value = value[..dash];

        var parts = value.Split('.');
        if (parts.Length is < 1 or > 3) return false;

        var numbers = new int[3];
        for (var i = 0; i < parts.Length; i++)
        {
            if (!int.TryParse(parts[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) || number < 0)
                return false;
            numbers[i] = number;
        }

        version = new SemanticVersion(numbers[0], numbers[1], numbers[2], preRelease);
        return true;
    }

    private static string ResolveCurrent()
    {
        var assembly = Assembly.GetEntryAssembly() ?? typeof(UpdateVersion).Assembly;
        var raw = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (string.IsNullOrWhiteSpace(raw)) return "";
        var plus = raw.IndexOf('+'); // ex.: "1.2.3+abc123" -> "1.2.3"
        return (plus >= 0 ? raw[..plus] : raw).Trim();
    }
}
