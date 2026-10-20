using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using PhantomMonitor.Services;

namespace PhantomMonitor.App.ViewModels;

public sealed class SensorTuningEditorViewModel : INotifyPropertyChanged
{
    private string _series;
    private string _intervalText;
    private string _smoothingText;
    private IReadOnlyList<string> _seriesOptions;
    private string? _selectedSeriesOption;
    private int _position;
    private bool _canMoveDown;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Series
    {
        get => _series;
        set
        {
            if (!Set(ref _series, value)) return;
            var selected = SeriesOptions.FirstOrDefault(option => string.Equals(option, value.Trim(), StringComparison.OrdinalIgnoreCase));
            Set(ref _selectedSeriesOption, selected, nameof(SelectedSeriesOption));
        }
    }

    public string IntervalText { get => _intervalText; set => Set(ref _intervalText, value); }
    public string SmoothingText { get => _smoothingText; set => Set(ref _smoothingText, value); }
    public IReadOnlyList<string> SeriesOptions
    {
        get => _seriesOptions;
        set
        {
            if (!Set(ref _seriesOptions, value)) return;
            SelectCurrentSeries();
        }
    }

    public string? SelectedSeriesOption
    {
        get => _selectedSeriesOption;
        set
        {
            if (!Set(ref _selectedSeriesOption, value)) return;
            if (!string.IsNullOrWhiteSpace(value))
                Series = value;
        }
    }

    public int Position
    {
        get => _position;
        private set
        {
            if (!Set(ref _position, value)) return;
            Raise(nameof(SeriesLabel));
            Raise(nameof(CanMoveUp));
        }
    }

    public string SeriesLabel => $"Regra {Position + 1}";
    public bool CanMoveUp => Position > 0;
    public bool CanMoveDown { get => _canMoveDown; private set => Set(ref _canMoveDown, value); }

    public SensorTuningEditorViewModel(SensorTuningConfig tuning, IReadOnlyList<string> seriesOptions, int position)
    {
        _series = tuning.Series;
        _intervalText = tuning.IntervalMs.ToString(CultureInfo.InvariantCulture);
        _smoothingText = tuning.Smoothing.ToString(CultureInfo.InvariantCulture);
        _seriesOptions = seriesOptions;
        _position = position;
        SelectCurrentSeries();
    }

    internal void SetPosition(int position, int count)
    {
        Position = position;
        CanMoveDown = position < count - 1;
    }

    private void SelectCurrentSeries()
    {
        var selected = SeriesOptions.FirstOrDefault(option => string.Equals(option, Series.Trim(), StringComparison.OrdinalIgnoreCase));
        Set(ref _selectedSeriesOption, selected, nameof(SelectedSeriesOption));
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
