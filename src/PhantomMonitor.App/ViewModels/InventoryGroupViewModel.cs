using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace PhantomMonitor.App.ViewModels;

/// <summary>
/// Grupo do inventário persistente: as 8 cascas fixas são criadas uma única vez
/// e as rows são reconciliadas por <see cref="InventoryRowViewModel.Key"/> a cada
/// snapshot. Antes, o grupo inteiro era recriado por snapshot e o ItemsControl
/// re-media/re-renderizava toda a árvore (até 10x/s) mesmo com a aba fora de vista.
/// </summary>
public sealed class InventoryGroupViewModel(string title, ObservableCollection<InventoryRowViewModel> rows) : INotifyPropertyChanged
{
    private bool _isExpanded = true;

    public string Title { get; } = title;
    public ObservableCollection<InventoryRowViewModel> Rows { get; } = rows;

    /// <summary>Estado visual do grupo, preservado nos snapshots durante a execução.</summary>
    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (_isExpanded == value) return;
            _isExpanded = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsExpanded)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

/// <summary>
/// Row do inventário persistente (antes: recriada por snapshot, re-layout do
/// ItemsControl mesmo com a aba invisível). Só as strings que mudaram disparam
/// PropertyChanged; <see cref="Key"/> é a identidade da reconciliação.
/// </summary>
public sealed class InventoryRowViewModel : INotifyPropertyChanged
{
    private string _source;
    private string _value;
    private string _unit;
    private bool _isAvailable;
    private string _min;
    private string _max;
    private string _avg;

    public InventoryRowViewModel(string key)
    {
        Key = key;
        _source = key;
        _value = "—";
        _unit = "";
        _isAvailable = false;
        _min = "--";
        _max = "--";
        _avg = "--";
    }

    public string Key { get; }

    /// <summary>Nome legível da origem; para séries indisponíveis, usa a chave da categoria.</summary>
    public string SensorName => IsAvailable && !string.IsNullOrWhiteSpace(Source) && Source != "—" ? Source : Key;

    /// <summary>Valor atual com unidade na mesma célula, como na lista do HWiNFO.</summary>
    public string Current
    {
        get
        {
            if (!IsAvailable || string.IsNullOrWhiteSpace(Unit) || Value is "Conectada" or "Desconectada") return Value;
            return $"{Value} {Unit}";
        }
    }

    public string Source
    {
        get => _source;
        private set
        {
            if (_source == value) return;
            _source = value;
            Raise();
            Raise(nameof(SensorName));
        }
    }

    public string Value
    {
        get => _value;
        private set
        {
            if (_value == value) return;
            _value = value;
            Raise();
            Raise(nameof(Current));
        }
    }

    public string Unit
    {
        get => _unit;
        private set
        {
            if (_unit == value) return;
            _unit = value;
            Raise();
            Raise(nameof(Current));
        }
    }

    public bool IsAvailable
    {
        get => _isAvailable;
        private set
        {
            if (_isAvailable == value) return;
            _isAvailable = value;
            Raise();
            Raise(nameof(SensorName));
            Raise(nameof(Current));
        }
    }

    /// <summary>Mínimo da sessão com unidade ("--" sem amostra).</summary>
    public string Min
    {
        get => _min;
        private set
        {
            if (_min == value) return;
            _min = value;
            Raise();
        }
    }

    /// <summary>Máximo da sessão com unidade ("--" sem amostra).</summary>
    public string Max
    {
        get => _max;
        private set
        {
            if (_max == value) return;
            _max = value;
            Raise();
        }
    }

    /// <summary>Média da sessão com unidade ("--" sem amostra).</summary>
    public string Avg
    {
        get => _avg;
        private set
        {
            if (_avg == value) return;
            _avg = value;
            Raise();
        }
    }

    private void Raise([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Aplica um plano do builder sem disparar PropertyChanged afirmativamente.</summary>
    public void Apply(string source, string value, string unit, bool available, string min, string max, string avg)
    {
        Source = source;
        Value = value;
        Unit = unit;
        IsAvailable = available;
        Min = min;
        Max = max;
        Avg = avg;
    }
}
