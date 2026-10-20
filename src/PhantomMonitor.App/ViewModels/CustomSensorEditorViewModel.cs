using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace PhantomMonitor.App.ViewModels;

public sealed class CustomSensorEditorViewModel : INotifyPropertyChanged
{
    private string _name;
    private string _formula;
    private string _unit;
    private int _position;
    private bool _canMoveDown;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Name { get => _name; set => Set(ref _name, value); }
    public string Formula { get => _formula; set => Set(ref _formula, value); }
    public string Unit { get => _unit; set => Set(ref _unit, value); }
    public int Position
    {
        get => _position;
        private set
        {
            if (!Set(ref _position, value)) return;
            Raise(nameof(SeriesKey));
            Raise(nameof(CanMoveUp));
        }
    }

    public string SeriesKey => $"Custom#{Position + 1}";
    public bool CanMoveUp => Position > 0;
    public bool CanMoveDown { get => _canMoveDown; private set => Set(ref _canMoveDown, value); }

    public CustomSensorEditorViewModel(string name, string formula, string unit, int position)
    {
        _name = name;
        _formula = formula;
        _unit = unit;
        _position = position;
    }

    internal void SetPosition(int position, int count)
    {
        Position = position;
        CanMoveDown = position < count - 1;
    }

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        Raise(propertyName);
        return true;
    }

    private void Raise(string? propertyName) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
