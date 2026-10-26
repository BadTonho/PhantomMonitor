using System.ComponentModel;
using System.Runtime.CompilerServices;
using PhantomMonitor.Models;

namespace PhantomMonitor.App.ViewModels;

/// <summary>
/// Row do overlay como ViewModel persistente. Antes: objeto imutável criado por
/// snapshot e a ObservableCollection inteira era Clear+re-add a cada tick (até
/// 10x/s) — o ItemsControl re-media e re-renderiza tudo. Agora a row é reusada e
/// só a string que realmente mudou dispara PropertyChanged. <see cref="Identity"/>
/// é a chave estável usada pela reconciliação.
/// </summary>
public sealed class OverlayMetricViewModel : INotifyPropertyChanged
{
    private string _name;
    private string _value;
    private bool _showFrametimeGraph;
    private string _overrideIdentity;
    private SensorKey? _seriesKey;
    private double? _numericValue;
    private bool? _showNameOverride;
    private bool _isIndividualSeries;
    private string? _deviceLabel;

    public OverlayMetricViewModel(string identity, string name, string? overrideIdentity = null,
        SensorKey? seriesKey = null, double? numericValue = null)
    {
        Identity = identity;
        _overrideIdentity = string.IsNullOrWhiteSpace(overrideIdentity) ? identity : overrideIdentity;
        _seriesKey = seriesKey;
        _numericValue = numericValue;
        _name = name;
        _value = "—";
    }

    /// <summary>Chave estável da row: rótulo das linhas fixas ("CPU", "JOGO") ou
    /// a chave da série ("GpuCore#1") para as por instância.</summary>
    public string Identity { get; }
    public string OverrideIdentity => _overrideIdentity;
    public SensorKey? SeriesKey => _seriesKey;
    public double? NumericValue => _numericValue;
    public bool IsIndividualSeries => _isIndividualSeries;
    public string? DeviceLabel => _deviceLabel;

    public bool ShowName => _showNameOverride
        ?? (Identity.ToUpperInvariant() is not ("CPU" or "GPU" or "RAM" or "DISCO" or "REDE" or "PLACA" or "JOGO" or "VENTOINHA"));

    public string Name
    {
        get => _name;
        set
        {
            if (_name == value) return;
            _name = value;
            Raise();
        }
    }

    public string Value
    {
        get => _value;
        set
        {
            if (_value == value) return;
            _value = value;
            Raise();
        }
    }

    public bool ShowFrametimeGraph
    {
        get => _showFrametimeGraph;
        set
        {
            if (_showFrametimeGraph == value) return;
            _showFrametimeGraph = value;
            Raise();
        }
    }

    public void UpdateSensorFormatting(string? overrideIdentity, SensorKey? seriesKey, double? numericValue,
        bool? showNameOverride, bool isIndividualSeries, string? deviceLabel = null)
    {
        var newIdentity = string.IsNullOrWhiteSpace(overrideIdentity) ? Identity : overrideIdentity;
        if (_overrideIdentity == newIdentity && _seriesKey == seriesKey && _numericValue == numericValue
            && _showNameOverride == showNameOverride && _isIndividualSeries == isIndividualSeries
            && _deviceLabel == deviceLabel) return;
        _overrideIdentity = newIdentity;
        _seriesKey = seriesKey;
        _numericValue = numericValue;
        _showNameOverride = showNameOverride;
        _isIndividualSeries = isIndividualSeries;
        _deviceLabel = deviceLabel;
        Raise(nameof(OverrideIdentity));
        Raise(nameof(SeriesKey));
        Raise(nameof(NumericValue));
        Raise(nameof(ShowName));
        Raise(nameof(IsIndividualSeries));
        Raise(nameof(DeviceLabel));
    }

    private void Raise([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    public event PropertyChangedEventHandler? PropertyChanged;
}
