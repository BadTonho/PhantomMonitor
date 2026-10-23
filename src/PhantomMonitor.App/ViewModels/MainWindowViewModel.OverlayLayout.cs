using PhantomMonitor.Models;
using PhantomMonitor.Services;

namespace PhantomMonitor.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    private static readonly string[] OverlayBlockTitles =
        ["CPU", "GPU", "RAM", "DISCO", "REDE", "VENTOINHAS", "PLACA-MÃE", "FPS / FRAMETIME", "GRÁFICOS E TEMPLATES"];

    private void InitializeOverlayBlocks()
    {
        _overlayBlocks.Clear();
        for (var index = 0; index < OverlayBlockKeys.Ordered.Length; index++)
        {
            var key = OverlayBlockKeys.Ordered[index];
            var block = new OverlayCanvasBlockViewModel(key, OverlayBlockTitles[index], this, SetOverlayBlockPosition);
            var position = GetOverlayBlockPosition(key);
            block.SetNormalizedPosition(position.X, position.Y);
            block.SetCanvasSize(OverlayEditorCanvasWidth, OverlayEditorCanvasHeight);
            _overlayBlocks.Add(block);
        }
    }

    private static OverlayBlockPosition DefaultOverlayBlockPosition(string key)
    {
        var index = Array.IndexOf(OverlayBlockKeys.Ordered, key);
        if (index < 0) index = OverlayBlockKeys.Ordered.Length - 1;
        return new OverlayBlockPosition
        {
            X = 0.02,
            Y = 0.035 + index * 0.105
        };
    }

    private OverlayBlockPosition GetOverlayBlockPosition(string key)
    {
        if (_overlayLayoutDraft.TryGetValue(key, out var draft)) return draft;
        return DefaultOverlayBlockPosition(key);
    }

    private void SetOverlayBlockPosition(string key, double x, double y)
    {
        // Materializa todos os padrões ao primeiro movimento para manter o layout
        // atual estável se os padrões da aplicação mudarem em versões futuras.
        foreach (var item in OverlayBlockKeys.Ordered)
        {
            if (_overlayLayoutDraft.ContainsKey(item)) continue;
            var position = DefaultOverlayBlockPosition(item);
            _overlayLayoutDraft[item] = position;
        }

        _overlayLayoutDraft[key] = new OverlayBlockPosition { X = x, Y = y };
        var block = _overlayBlocks.FirstOrDefault(item => item.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
        block?.SetNormalizedPosition(x, y);
    }

    public bool OverlayLayoutHasChanges()
    {
        foreach (var (key, position) in _overlayLayoutDraft)
        {
            if (!_config.Overlay.Layout.TryGetValue(key, out var current)
                || Math.Abs(current.X - position.X) > 0.0001
                || Math.Abs(current.Y - position.Y) > 0.0001)
                return true;
        }
        return false;
    }

    public void ApplyOverlayBlockPosition(string key, double x, double y) => SetOverlayBlockPosition(key, x, y);

    public void SetOverlayCanvasSize(double width, double height)
    {
        width = Math.Clamp(width, 240, 10000);
        height = Math.Clamp(height, 120, 10000);
        if (Math.Abs(OverlayCanvasWidth - width) < 0.5 && Math.Abs(OverlayCanvasHeight - height) < 0.5) return;
        OverlayCanvasWidth = width;
        OverlayCanvasHeight = height;
        foreach (var block in _overlayBlocks)
            block.SetCanvasSize(width, height);
    }

    private void ReconcileOverlayBlocks()
    {
        var rowPlans = _overlayBlocks.ToDictionary(block => block.Key, _ => new List<OverlayMetricViewModel>(), StringComparer.OrdinalIgnoreCase);
        foreach (var row in OverlayRows)
        {
            var key = OverlayBlockKeys.ForIdentity(row.Identity);
            rowPlans[key].Add(row);
        }

        foreach (var block in _overlayBlocks)
            ReconcileCollection(block.Rows, rowPlans[block.Key]);

        var extras = _overlayBlocks.First(item => item.Key == OverlayBlockKeys.Extras);
        ReconcileCollection(extras.Extras, OverlayExtraRows.Cast<object>().ToList());

        foreach (var block in _overlayBlocks)
            block.NotifyRowsChanged();
    }

    private static void ReconcileCollection<T>(System.Collections.ObjectModel.ObservableCollection<T> current, IReadOnlyList<T> desired)
        where T : class
    {
        var position = 0;
        for (var i = 0; i < desired.Count; i++)
        {
            var item = desired[i];
            if (position < current.Count && ReferenceEquals(current[position], item))
            {
                position++;
                continue;
            }

            var found = -1;
            for (var scan = position + 1; scan < current.Count; scan++)
            {
                if (!ReferenceEquals(current[scan], item)) continue;
                found = scan;
                break;
            }

            if (found >= 0)
                current.Move(found, position);
            else
                current.Insert(position, item);
            position++;
        }

        while (current.Count > position)
            current.RemoveAt(current.Count - 1);
    }

    private void CopyOverlayLayoutToConfig()
    {
        _config.Overlay.Layout = _overlayLayoutDraft.ToDictionary(
            pair => pair.Key,
            pair => new OverlayBlockPosition { X = pair.Value.X, Y = pair.Value.Y },
            StringComparer.OrdinalIgnoreCase);
    }

    private void ReloadOverlayLayoutDraft()
    {
        _overlayLayoutDraft.Clear();
        foreach (var (key, position) in _config.Overlay.Layout)
            _overlayLayoutDraft[key] = new OverlayBlockPosition { X = position.X, Y = position.Y };
        foreach (var block in _overlayBlocks)
        {
            var position = GetOverlayBlockPosition(block.Key);
            block.SetNormalizedPosition(position.X, position.Y);
        }
    }
}
