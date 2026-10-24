using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia.Media;
using PhantomMonitor.App.Services;
using PhantomMonitor.Models;

namespace PhantomMonitor.App.ViewModels;

public sealed class ThemePaletteEditorViewModel : INotifyPropertyChanged
{
    private readonly IReadOnlyDictionary<string, Color> _defaults;
    private IReadOnlyDictionary<string, Color> _effectiveColors;
    private ThemePaletteGroupItem? _selectedGroup;
    private ThemePaletteTokenItem? _selectedToken;
    private Color _selectedBaseColor;
    private Color _selectedTokenColor;
    private bool _syncingSelection;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string ThemeKey { get; }
    public string ThemeName => ThemeKey == "slate" ? "Slate" : "Neptune";
    public ThemePaletteConfig Draft { get; }
    public ObservableCollection<ThemePaletteGroupItem> Groups { get; }
    public ObservableCollection<ThemePaletteTokenItem> Tokens { get; }

    public ThemePaletteGroupItem? SelectedGroup
    {
        get => _selectedGroup;
        set
        {
            if (ReferenceEquals(_selectedGroup, value)) return;
            _selectedGroup = value;
            _syncingSelection = true;
            SelectedBaseColor = value?.Color ?? Colors.White;
            _syncingSelection = false;
            Raise();
            Raise(nameof(SelectedGroupName));
            Raise(nameof(HasSelectedGroup));
        }
    }

    public ThemePaletteTokenItem? SelectedToken
    {
        get => _selectedToken;
        set
        {
            if (ReferenceEquals(_selectedToken, value)) return;
            _selectedToken = value;
            _syncingSelection = true;
            SelectedTokenColor = value?.Color ?? Colors.White;
            _syncingSelection = false;
            Raise();
            Raise(nameof(SelectedTokenName));
            Raise(nameof(HasSelectedToken));
        }
    }

    public Color SelectedBaseColor
    {
        get => _selectedBaseColor;
        set
        {
            if (!Set(ref _selectedBaseColor, value)) return;
            if (!_syncingSelection && SelectedGroup is { } group)
            {
                Draft.BaseColors[group.Key] = ThemePaletteResolver.ToHex(value);
                group.Color = value;
                group.IsCustomized = true;
                RefreshResolvedColors();
            }
        }
    }

    public Color SelectedTokenColor
    {
        get => _selectedTokenColor;
        set
        {
            if (!Set(ref _selectedTokenColor, value)) return;
            if (!_syncingSelection && SelectedToken is { } token)
            {
                Draft.TokenOverrides[token.Key] = ThemePaletteResolver.ToHex(value);
                token.Color = value;
                token.IsCustomized = true;
                RefreshResolvedColors();
                _syncingSelection = true;
                SelectedTokenColor = value;
                _syncingSelection = false;
            }
        }
    }

    public string SelectedGroupName => SelectedGroup?.Name ?? "Selecione um grupo";
    public string SelectedTokenName => SelectedToken?.DisplayName ?? "Selecione uma cor";
    public bool HasSelectedGroup => SelectedGroup is not null;
    public bool HasSelectedToken => SelectedToken is not null;

    public ThemePaletteEditorViewModel(string theme, ThemePaletteConfig draft)
    {
        ThemeKey = theme == "slate" ? "slate" : "neptune";
        Draft = draft.Clone();
        var resources = ThemePaletteResolver.LoadThemeDictionary(ThemeKey);
        _defaults = ThemePaletteResolver.ReadDefaultColors(resources);
        _effectiveColors = ThemePaletteResolver.Resolve(Draft, _defaults);

        Groups = new ObservableCollection<ThemePaletteGroupItem>(ThemePaletteCatalog.Groups.Select(group =>
        {
            var color = Draft.BaseColors.TryGetValue(group.Key, out var hex)
                ? Color.Parse(hex)
                : _defaults[group.AnchorToken];
            return new ThemePaletteGroupItem(group.Key, group.Name, group.AnchorToken, group.Tokens, color,
                Draft.BaseColors.ContainsKey(group.Key));
        }));

        Tokens = new ObservableCollection<ThemePaletteTokenItem>(ThemePaletteCatalog.Groups
            .SelectMany(group => group.Tokens.Select(token => new ThemePaletteTokenItem(
                token, DisplayTokenName(token), group.Name, _effectiveColors[token], Draft.TokenOverrides.ContainsKey(token)))));

        SelectedGroup = Groups.FirstOrDefault();
        SelectedToken = Tokens.FirstOrDefault();
    }

    public void ResetSelectedGroup()
    {
        if (SelectedGroup is not { } group) return;
        Draft.BaseColors.Remove(group.Key);

        group.Color = _defaults[group.AnchorToken];
        group.IsCustomized = false;
        RefreshResolvedColors();
        _syncingSelection = true;
        SelectedBaseColor = group.Color;
        _syncingSelection = false;
    }

    public void ResetSelectedToken()
    {
        if (SelectedToken is not { } token) return;
        Draft.TokenOverrides.Remove(token.Key);
        token.IsCustomized = false;
        RefreshResolvedColors();
        _syncingSelection = true;
        SelectedTokenColor = token.Color;
        _syncingSelection = false;
    }

    public void ResetAll()
    {
        Draft.BaseColors.Clear();
        Draft.TokenOverrides.Clear();
        foreach (var group in Groups)
        {
            group.Color = _defaults[group.AnchorToken];
            group.IsCustomized = false;
        }
        RefreshResolvedColors();
        _syncingSelection = true;
        SelectedBaseColor = SelectedGroup?.Color ?? Colors.White;
        SelectedTokenColor = SelectedToken?.Color ?? Colors.White;
        _syncingSelection = false;
    }

    private void RefreshResolvedColors()
    {
        _effectiveColors = ThemePaletteResolver.Resolve(Draft, _defaults);
        foreach (var token in Tokens)
        {
            token.Color = _effectiveColors[token.Key];
            token.IsCustomized = Draft.TokenOverrides.ContainsKey(token.Key);
        }
        if (SelectedToken is { } selected)
        {
            _syncingSelection = true;
            SelectedTokenColor = selected.Color;
            _syncingSelection = false;
        }
        Raise(nameof(SelectedGroupName));
        Raise(nameof(SelectedTokenName));
    }

    private static string DisplayTokenName(string key)
    {
        var name = key.EndsWith("Brush", StringComparison.Ordinal) ? key[..^5] : key;
        var chars = new List<char>(name.Length + 8);
        for (var i = 0; i < name.Length; i++)
        {
            if (i > 0 && char.IsUpper(name[i]) && (char.IsLower(name[i - 1]) || char.IsDigit(name[i - 1])))
                chars.Add(' ');
            chars.Add(name[i]);
        }
        return new string(chars.ToArray());
    }

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        Raise(propertyName);
        return true;
    }

    private void Raise([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public sealed class ThemePaletteGroupItem : INotifyPropertyChanged
{
    private Color _color;
    private bool _isCustomized;
    public event PropertyChangedEventHandler? PropertyChanged;

    public string Key { get; }
    public string Name { get; }
    public string AnchorToken { get; }
    public IReadOnlyList<string> TokenKeys { get; }
    public string Hex => ThemePaletteResolver.ToHex(Color);
    public Color Color
    {
        get => _color;
        set
        {
            if (_color == value) return;
            _color = value;
            Raise();
            Raise(nameof(Hex));
        }
    }
    public bool IsCustomized
    {
        get => _isCustomized;
        set
        {
            if (_isCustomized == value) return;
            _isCustomized = value;
            Raise();
        }
    }

    public ThemePaletteGroupItem(string key, string name, string anchorToken, IReadOnlyList<string> tokenKeys,
        Color color, bool isCustomized)
    {
        Key = key;
        Name = name;
        AnchorToken = anchorToken;
        TokenKeys = tokenKeys;
        _color = color;
        _isCustomized = isCustomized;
    }

    private void Raise([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public sealed class ThemePaletteTokenItem : INotifyPropertyChanged
{
    private Color _color;
    private bool _isCustomized;
    public event PropertyChangedEventHandler? PropertyChanged;

    public string Key { get; }
    public string DisplayName { get; }
    public string GroupName { get; }
    public string Hex => ThemePaletteResolver.ToHex(Color);
    public Color Color
    {
        get => _color;
        set
        {
            if (_color == value) return;
            _color = value;
            Raise();
            Raise(nameof(Hex));
        }
    }
    public bool IsCustomized
    {
        get => _isCustomized;
        set
        {
            if (_isCustomized == value) return;
            _isCustomized = value;
            Raise();
        }
    }

    public ThemePaletteTokenItem(string key, string displayName, string groupName, Color color, bool isCustomized)
    {
        Key = key;
        DisplayName = displayName;
        GroupName = groupName;
        _color = color;
        _isCustomized = isCustomized;
    }

    private void Raise([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
