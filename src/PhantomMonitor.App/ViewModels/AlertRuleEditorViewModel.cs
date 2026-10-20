using System.ComponentModel;
using System.Runtime.CompilerServices;
using PhantomMonitor.Models;

namespace PhantomMonitor.App.ViewModels;

public sealed class AlertRuleEditorViewModel : INotifyPropertyChanged
{
    private readonly AlertRule _source;
    private IReadOnlyList<string> _sensorOptions;
    private string _sensorKey;
    private string _comparison;
    private string _thresholdText;
    private string _cooldownText;
    private bool _enabled;
    private bool _popup;
    private string _message;

    public event PropertyChangedEventHandler? PropertyChanged;

    public IReadOnlyList<string> SensorOptions
    {
        get => _sensorOptions;
        set => Set(ref _sensorOptions, value);
    }

    public string SensorKey
    {
        get => _sensorKey;
        set
        {
            if (!Set(ref _sensorKey, value)) return;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SensorUnit)));
        }
    }
    public string SensorUnit => PhantomMonitor.Models.SensorKey.TryParse(SensorKey, out var key)
        ? SensorUnits.Of(key.Type)
        : "";
    public IReadOnlyList<string> Comparisons { get; } = ["Above", "Below"];
    public string Comparison { get => _comparison; set => Set(ref _comparison, value); }
    public string ThresholdText { get => _thresholdText; set => Set(ref _thresholdText, value); }
    public string CooldownText { get => _cooldownText; set => Set(ref _cooldownText, value); }
    public bool Enabled { get => _enabled; set => Set(ref _enabled, value); }
    public bool Popup { get => _popup; set => Set(ref _popup, value); }
    public string Message { get => _message; set => Set(ref _message, value); }

    public AlertRuleEditorViewModel(AlertRule rule, IReadOnlyList<string> sensorOptions)
    {
        _source = Clone(rule);
        _sensorOptions = sensorOptions;
        _sensorKey = new PhantomMonitor.Models.SensorKey(rule.Sensor, rule.SensorIndex).ToString();
        _comparison = rule.Comparison.ToString();
        _thresholdText = rule.Threshold.ToString(System.Globalization.CultureInfo.CurrentCulture);
        _cooldownText = rule.CooldownSeconds.ToString(System.Globalization.CultureInfo.CurrentCulture);
        _enabled = rule.Enabled;
        _popup = rule.Popup;
        _message = rule.Message ?? "";
    }

    public AlertRule CreateRule(double threshold, int cooldownSeconds)
    {
        PhantomMonitor.Models.SensorKey.TryParse(SensorKey, out var key);
        var rule = Clone(_source);
        rule.Enabled = Enabled;
        rule.Sensor = key.Type;
        rule.SensorIndex = key.Index;
        rule.Comparison = Enum.TryParse<AlertComparison>(Comparison, out var comparison)
            ? comparison
            : AlertComparison.Above;
        rule.Threshold = threshold;
        rule.CooldownSeconds = cooldownSeconds;
        rule.Popup = Popup;
        rule.Message = Message ?? "";
        return rule;
    }

    public static AlertRule Clone(AlertRule rule) => new()
    {
        Enabled = rule.Enabled,
        Sensor = rule.Sensor,
        SensorIndex = rule.SensorIndex,
        Comparison = rule.Comparison,
        Threshold = rule.Threshold,
        CooldownSeconds = rule.CooldownSeconds,
        Popup = rule.Popup,
        Sound = rule.Sound,
        Script = rule.Script ?? "",
        ScriptArguments = rule.ScriptArguments ?? "",
        Shutdown = rule.Shutdown,
        Message = rule.Message ?? ""
    };

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }
}

public sealed record AlertHistoryEntryViewModel(string Time, string Sensor, string Message, string Value);
