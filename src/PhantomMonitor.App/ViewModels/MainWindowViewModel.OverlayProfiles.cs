using System.Diagnostics;
using Avalonia;
using Avalonia.Media;
using PhantomMonitor.Models;
using PhantomMonitor.Services;

namespace PhantomMonitor.App.ViewModels;

// Motor de perfis por aplicativo do OSD (P3 do WPF, portado na F1.3): o processo
// em foco casa com o AppMatch do perfil (ou o perfil fixado — pin chega com a
// bandeja em F1.5) e aplica SIN os overrides definidos — offsets, largura, fonte,
// opacidade, whitelist de séries, acento e visibilidade; tudo com fonte no config
// global quando o perfil não definiu (nulo/ausente = usa o global). A cor do
// acento é dado do config (hex), não token; os tints são derivados dela.
public sealed partial class MainWindowViewModel
{
    private string? _pinnedProfileName; // bandeja (F1.5): fixa um perfil na sessão do usuário
    private OverlayProfile? _activeProfile;
    private readonly Dictionary<int, string> _foregroundNameCache = [];

    /// <summary>Id base dos itens dinâmicos do menu da bandeja (paridade do MenuDynamicBase do WPF: 0x500).</summary>
    public const int TrayMenuDynamicBase = 0x500;

    /// <summary>Entrada da seção dinâmica da bandeja: id (base WPF), título e estado do check.</summary>
    public sealed record TrayProfileMenuEntry(int Id, string Title, bool Checked);

    public string? PinnedProfileName => _pinnedProfileName;

    /// <summary>
    /// Entradas da seção dinâmica da bandeja: "Automático (por aplicativo)" + perfis,
    /// avaliadas a cada reconstrução do menu (paridade do provedor do WPF).
    /// </summary>
    public List<TrayProfileMenuEntry> BuildTrayProfileEntries()
    {
        var entries = new List<TrayProfileMenuEntry>
        {
            new(TrayMenuDynamicBase, "Automático (por aplicativo)", _pinnedProfileName is null)
        };
        for (var index = 0; index < _config.Profiles.Count; index++)
        {
            var profile = _config.Profiles[index];
            entries.Add(new(
                TrayMenuDynamicBase + index + 1,
                profile.Name,
                string.Equals(_pinnedProfileName, profile.Name, StringComparison.OrdinalIgnoreCase)));
        }
        return entries;
    }

    /// <summary>Chegada de clique da bandeja: id da base desfixa (automático), demais fixam por índice.</summary>
    public void ApplyTrayProfileClick(int id)
    {
        if (id == TrayMenuDynamicBase)
        {
            SetPinnedProfile(null);
            return;
        }

        var index = id - TrayMenuDynamicBase - 1;
        if (index < 0 || index >= _config.Profiles.Count) return;
        SetPinnedProfile(_config.Profiles[index].Name);
    }

    /// <summary>
    /// Pin da bandeja: fixa o perfil na sessão (não persiste no config, como no WPF).
    /// Reaplica os overrides imediatamente — o próximo tick de posição já usaria o pin.
    /// </summary>
    public void SetPinnedProfile(string? profileName)
    {
        var pinned = string.IsNullOrWhiteSpace(profileName) ? null : profileName.Trim();
        if (string.Equals(_pinnedProfileName, pinned, StringComparison.OrdinalIgnoreCase)) return;
        _pinnedProfileName = pinned;

        var target = pinned is null
            ? null
            : _config.Profiles.FirstOrDefault(p => string.Equals(p.Name, pinned, StringComparison.OrdinalIgnoreCase));
        if (string.Equals(_activeProfile?.Name, target?.Name, StringComparison.OrdinalIgnoreCase)) return;

        _activeProfile = target;
        ApplyOverlayProfile(target);
    }

    // ---- Aparência efetiva (config ⊕ perfil) — fonte de verdade para os props da view ----
    private double _overlayFontEffective = 11.5;
    private double _overlayWidthEffective = 220;
    private double _overlayOpacityEffective = 0.85;

    public IBrush OverlayAccentBrush { get => _overlayAccentBrush; private set => Set(ref _overlayAccentBrush, value); }
    private IBrush _overlayAccentBrush = ResolveBrush("AccentLightBrush");

    public IBrush OverlayPanelBorderBrush { get => _overlayPanelBorderBrush; private set => Set(ref _overlayPanelBorderBrush, value); }
    private IBrush _overlayPanelBorderBrush = ResolveBrush("OverlayBorderBrush");

    public string OverlayProfileTagText { get => _overlayProfileTagText; private set => Set(ref _overlayProfileTagText, value); }
    private string _overlayProfileTagText = "";

    public bool HasOverlayProfileTag { get => _hasOverlayProfileTag; private set => Set(ref _hasOverlayProfileTag, value); }
    private bool _hasOverlayProfileTag;

    public bool OverlayPanelVisible { get => _overlayPanelVisible; private set => Set(ref _overlayPanelVisible, value); }
    private bool _overlayPanelVisible = true;

    /// <summary>Offsets do follow (config ⊕ perfil): base da posição pelo app em foco (Windows).</summary>
    public double FollowOffsetX { get; private set; }
    public double FollowOffsetY { get; private set; }

    /// <summary>Estado do matcher consultado a cada tick de posição pelo overlay Windows.</summary>
    public OverlayProfileState? GetOverlayProfileState() =>
        new() { Profiles = _config.Profiles, Pinned = string.IsNullOrEmpty(_pinnedProfileName) ? null : _pinnedProfileName };

    /// <summary>
    /// Falha segura: config em reconstrução não derruba o overlay (WPF fazia o mesmo).
    /// Resolve o perfil alvo (pin &gt; match por processo) e reaplica só quando muda.
    /// </summary>
    public void UpdateProfileForForeground(uint pid)
    {
        OverlayProfileState? state;
        try { state = GetOverlayProfileState(); }
        catch { return; }
        if (state is null) return;

        OverlayProfile? target;
        if (state.Pinned is { Length: > 0 } pinned)
        {
            // Fixado: aplica mesmo em outro app, até o usuário desfixar (bandeja).
            target = state.Profiles.FirstOrDefault(p =>
                string.Equals(p.Name, pinned, StringComparison.OrdinalIgnoreCase));
        }
        else
        {
            var processName = ResolveForegroundProcessName(pid);
            target = state.Profiles.FirstOrDefault(p => MatchesApp(p.AppMatch, processName));
        }

        if (string.Equals(_activeProfile?.Name, target?.Name, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(_pinnedProfileName, state.Pinned, StringComparison.OrdinalIgnoreCase))
            return;

        _pinnedProfileName = state.Pinned;
        _activeProfile = target;
        ApplyOverlayProfile(target);
    }

    /// <summary>Resolve o nome do processo em foco (sem .exe, sem caminho) com cache curto.</summary>
    private string ResolveForegroundProcessName(uint pid)
    {
        var key = (int)pid;
        if (_foregroundNameCache.TryGetValue(key, out var cached)) return cached;

        string name = "";
        try
        {
            using var process = Process.GetProcessById(key);
            name = process.ProcessName;
        }
        catch
        {
            // Processo saiu entre GetForegroundWindow e a busca: sem match neste tick.
        }

        if (_foregroundNameCache.Count > 64) _foregroundNameCache.Clear();
        _foregroundNameCache[key] = name;
        return name;
    }

    /// <summary>Case-insensitive sem caminho/"*.exe"; "" ou "*" casa com qualquer app (WPF verbatim).</summary>
    private static bool MatchesApp(string appMatch, string? processName)
    {
        var pattern = appMatch?.Trim();
        if (string.IsNullOrEmpty(pattern) || pattern == "*") return true;
        if (string.IsNullOrEmpty(processName)) return false;

        if (processName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            processName = processName[..^4];

        return processName.StartsWith(pattern, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Whitelist de séries por instância em vigor: perfil &gt; global.</summary>
    public IReadOnlyList<string>? EffectiveOverlayShownSeries() =>
        _activeProfile?.ShownSeries is { Count: > 0 } series ? series : _config.Overlay.ShownSeries;

    /// <summary>Aplica os overrides do perfil aos campos efetivos (nulo = aplica o base).</summary>
    public void ApplyOverlayProfile(OverlayProfile? profile)
    {
        var config = _config;

        var width = profile?.Width is int w && w > 0 ? w : config.Overlay.Width;
        var font = Math.Clamp(profile?.FontSize is { } fontOverride ? fontOverride : config.Overlay.FontSize, 8, 20);
        var fontChanged = Math.Abs(font - _overlayFontEffective) > 0.05;

        _overlayWidthEffective = width;
        _overlayFontEffective = font;
        _overlayOpacityEffective = profile?.Opacity is { } opacity
            ? Math.Clamp(opacity, 0.1, 1.0)
            : Math.Clamp(config.OverlayOpacity <= 0 ? 0.85 : config.OverlayOpacity, 0.1, 1.0);
        FollowOffsetX = profile?.OffsetX ?? config.OverlayPosition.X;
        FollowOffsetY = profile?.OffsetY ?? config.OverlayPosition.Y;
        OverlayPanelVisible = profile?.Visible != false;

        var accentHex = string.IsNullOrWhiteSpace(profile?.AccentColor)
            ? config.Overlay.AccentColor
            : profile!.AccentColor;
        _overlayAccentBrush = OverlayBrushHelper.Resolve(accentHex, _overlayAccentBrush);
        if (OverlayBrushHelper.TryParseColor(accentHex, out var accentColor))
            _overlayPanelBorderBrush = OverlayBrushHelper.Tint(accentColor);

        OverlayProfileTagText = string.IsNullOrWhiteSpace(profile?.Name) ? "" : $"◉ perfil: {profile!.Name}";
        HasOverlayProfileTag = OverlayProfileTagText.Length > 0;

        Raise(nameof(OverlayAccentBrush));
        Raise(nameof(OverlayPanelBorderBrush));
        Raise(nameof(OverlayWidth));
        Raise(nameof(OverlayOpacity));
        if (fontChanged) RaiseOverlayAppearanceProperties();

        // Whitelist vigente pode mudar o conjunto de séries por instância.
        UpdateOverlayRows(_latestSensorData);
    }

    /// <summary>Base do config mudou (SaveSettings): reanula effettivos quando não há perfil ativo.</summary>
    public void OnOverlayBaseConfigChanged() =>
        ApplyOverlayProfile(_activeProfile);
}
