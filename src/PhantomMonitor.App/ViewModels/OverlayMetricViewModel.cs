using System.ComponentModel;
using System.Runtime.CompilerServices;

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

    public OverlayMetricViewModel(string identity, string name)
    {
        Identity = identity;
        _name = name;
        _value = "—";
    }

    /// <summary>Chave estável da row: rótulo das linhas fixas ("CPU", "JOGO") ou
    /// a chave da série ("GpuCore#1") para as por instância.</summary>
    public string Identity { get; }

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

    private void Raise([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    public event PropertyChangedEventHandler? PropertyChanged;
}
