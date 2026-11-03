namespace PhantomMonitor.App.ViewModels;

public sealed partial class OverlayBlockContentDialogViewModel
{
    public OverlayBlockItemChoiceViewModel? SelectedContentItem
    {
        get => _selectedContentItem;
        set
        {
            if (!Set(ref _selectedContentItem, value)) return;
            Raise(nameof(CanMoveContentUp));
            Raise(nameof(CanMoveContentDown));
            Raise(nameof(ShowMetricEditorHint));
            RefreshMetricChoices();
        }
    }

    public OverlayBlockOrderRowViewModel? SelectedOrderRow
    {
        get => _selectedOrderRow;
        set
        {
            if (!Set(ref _selectedOrderRow, value)) return;
            SelectedContentItem = value?.Group;
            if (value?.Metric is { } metric)
            {
                if (!MetricChoices.Any(choice => choice.Identity.Equals(metric.Identity, StringComparison.OrdinalIgnoreCase)))
                    RefreshMetricChoicesFor(value.Group);
                SelectedMetric = MetricChoices.FirstOrDefault(choice => choice.Identity.Equals(metric.Identity, StringComparison.OrdinalIgnoreCase));
            }
            else if (value is { IsGroup: true, Group.Kind: "category" or "series" })
            {
                RefreshMetricChoicesFor(value.Group);
                SelectedMetric = MetricChoices.Count == 1 ? MetricChoices[0] : null;
            }
            else
                SelectedMetric = null;
            Raise(nameof(CanMoveContentUp));
            Raise(nameof(CanMoveContentDown));
            Raise(nameof(CanRemoveSelectedContent));
            Raise(nameof(RemoveSelectedContentText));
            Raise(nameof(HasSelectedMetric));
            Raise(nameof(HasSelectedCustomLine));
            Raise(nameof(ShowMetricEditorHint));
            LoadCustomLineEditor();
        }
    }

    public bool HasSelectedMetric => SelectedMetric is not null;
    public bool ShowMetricEditorHint => !HasSelectedMetric && !HasSelectedCustomLine;
    public bool CanRemoveSelectedContent => SelectedOrderRow is not null;
    public string RemoveSelectedContentText => SelectedOrderRow?.IsMetric == true
        ? "Ocultar em todos os blocos"
        : "Remover da lista";
    public bool CanMoveContentUp => CanMoveSelectedOrderRow(-1);
    public bool CanMoveContentDown => CanMoveSelectedOrderRow(1);

    public void MoveSelectedContent(int offset)
    {
        if (SelectedOrderRow is null) return;
        if (!SelectedOrderRow.IsMetric)
        {
            var currentGroup = OrderedContentItems.IndexOf(SelectedOrderRow.Group);
            MoveContent(currentGroup, currentGroup + offset);
            SelectedOrderRow = OrderRows.FirstOrDefault(row => !row.IsMetric && ReferenceEquals(row.Group, SelectedContentItem));
            return;
        }
        var index = OrderRows.IndexOf(SelectedOrderRow);
        MoveOrderRow(index, index + offset);
    }

    public void MoveContent(int sourceIndex, int targetIndex)
    {
        if (sourceIndex < 0 || sourceIndex >= OrderedContentItems.Count
            || targetIndex < 0 || targetIndex >= OrderedContentItems.Count
            || sourceIndex == targetIndex) return;
        var item = OrderedContentItems[sourceIndex];
        OrderedContentItems.Move(sourceIndex, targetIndex);
        SelectedContentItem = item;
        Raise(nameof(CanMoveContentUp));
        Raise(nameof(CanMoveContentDown));
        RebuildOrderRows();
        PublishDraftToCanvas();
    }

    public void MoveOrderRow(int sourceIndex, int targetIndex)
    {
        if (sourceIndex < 0 || sourceIndex >= OrderRows.Count || targetIndex < 0 || targetIndex >= OrderRows.Count
            || sourceIndex == targetIndex) return;
        var row = OrderRows[sourceIndex];
        if (!row.IsMetric)
        {
            var sourceGroupIndex = OrderedContentItems.IndexOf(row.Group);
            var targetGroupIndex = OrderedContentItems.IndexOf(OrderRows[targetIndex].Group);
            if (sourceGroupIndex < 0 || targetGroupIndex < 0) return;
            MoveContent(sourceGroupIndex, targetGroupIndex);
            SelectedOrderRow = OrderRows.FirstOrDefault(candidate => !candidate.IsMetric && ReferenceEquals(candidate.Group, row.Group));
            return;
        }

        var targetRow = OrderRows[targetIndex];
        if (!targetRow.IsMetric || !targetRow.Group.Kind.Equals(row.Group.Kind, StringComparison.OrdinalIgnoreCase)
            || !targetRow.Group.Value.Equals(row.Group.Value, StringComparison.OrdinalIgnoreCase)) return;
        var groupKey = row.GroupKey;
        var siblings = OrderRows.Where(candidate => candidate.IsMetric && candidate.GroupKey.Equals(groupKey, StringComparison.OrdinalIgnoreCase)).ToList();
        var from = siblings.IndexOf(row);
        var to = siblings.IndexOf(targetRow);
        if (from < 0 || to < 0) return;
        siblings.RemoveAt(from);
        siblings.Insert(to, row);
        _metricOrder[groupKey] = siblings.Select(candidate => candidate.Identity).ToList();
        _customMetricOrderGroups.Add(groupKey);
        RebuildOrderRows();
        SelectedOrderRow = OrderRows.FirstOrDefault(candidate => candidate.IsMetric
            && candidate.GroupKey.Equals(groupKey, StringComparison.OrdinalIgnoreCase)
            && candidate.Identity.Equals(row.Identity, StringComparison.OrdinalIgnoreCase));
        PublishDraftToCanvas();
    }

    public void MoveOrderRowToInsertion(int sourceIndex, int insertionIndex)
    {
        if (sourceIndex < 0 || sourceIndex >= OrderRows.Count) return;
        insertionIndex = Math.Clamp(insertionIndex, 0, OrderRows.Count);
        var row = OrderRows[sourceIndex];
        if (!row.IsMetric)
        {
            var sourceGroupIndex = OrderedContentItems.IndexOf(row.Group);
            if (sourceGroupIndex < 0) return;
            var targetGroupIndex = OrderRows.Take(insertionIndex).Count(candidate => !candidate.IsMetric);
            if (sourceGroupIndex < targetGroupIndex) targetGroupIndex--;
            targetGroupIndex = Math.Clamp(targetGroupIndex, 0, OrderedContentItems.Count - 1);
            MoveContent(sourceGroupIndex, targetGroupIndex);
            SelectedOrderRow = OrderRows.FirstOrDefault(candidate => !candidate.IsMetric && ReferenceEquals(candidate.Group, row.Group));
            return;
        }

        var groupKey = row.GroupKey;
        var siblings = OrderRows.Where(candidate => candidate.IsMetric
            && candidate.GroupKey.Equals(groupKey, StringComparison.OrdinalIgnoreCase)).ToList();
        var from = siblings.IndexOf(row);
        if (from < 0) return;
        var to = OrderRows.Take(insertionIndex).Count(candidate => candidate.IsMetric
            && candidate.GroupKey.Equals(groupKey, StringComparison.OrdinalIgnoreCase));
        if (from < to) to--;
        to = Math.Clamp(to, 0, siblings.Count - 1);
        if (from == to) return;
        siblings.RemoveAt(from);
        siblings.Insert(to, row);
        _metricOrder[groupKey] = siblings.Select(candidate => candidate.Identity).ToList();
        _customMetricOrderGroups.Add(groupKey);
        RebuildOrderRows();
        SelectedOrderRow = OrderRows.FirstOrDefault(candidate => candidate.IsMetric
            && candidate.GroupKey.Equals(groupKey, StringComparison.OrdinalIgnoreCase)
            && candidate.Identity.Equals(row.Identity, StringComparison.OrdinalIgnoreCase));
        PublishDraftToCanvas();
    }

    public OverlayBlockOrderRowViewModel? FindCurrentOrderRow(OverlayBlockOrderRowViewModel row)
        => OrderRows.FirstOrDefault(candidate => candidate.IsMetric == row.IsMetric
            && candidate.GroupKey.Equals(row.GroupKey, StringComparison.OrdinalIgnoreCase)
            && candidate.Identity.Equals(row.Identity, StringComparison.OrdinalIgnoreCase));

    private bool CanMoveSelectedOrderRow(int offset)
    {
        if (SelectedOrderRow is null) return false;
        if (!SelectedOrderRow.IsMetric)
        {
            var groupIndex = OrderedContentItems.IndexOf(SelectedOrderRow.Group);
            return groupIndex + offset >= 0 && groupIndex + offset < OrderedContentItems.Count;
        }
        var current = OrderRows.IndexOf(SelectedOrderRow);
        var target = current + offset;
        if (target < 0 || target >= OrderRows.Count) return false;
        var targetRow = OrderRows[target];
        return !SelectedOrderRow.IsMetric || targetRow.IsMetric
            && targetRow.GroupKey.Equals(SelectedOrderRow.GroupKey, StringComparison.OrdinalIgnoreCase);
    }

    public void RemoveSelectedContent()
    {
        if (SelectedOrderRow?.Metric is { } metric)
        {
            if (_owner.HideOverlayMetricDraft(metric.Identity))
            {
                RefreshDynamicChoices();
                RebuildOrderRows();
            }
            return;
        }

        if (SelectedContentItem is not { } item) return;
        if (item.Kind == "customline") _customLines.RemoveAll(line => line.Id == item.Value);
        var choice = FindChoice(item.Kind, item.Value);
        if (choice is not null)
            choice.IsSelected = false;
        else
            SetSelected(item.Kind, item.Value, false);
    }

    public static string GetMetricOrderKey(string kind, string value) => $"{kind.ToLowerInvariant()}:{value}";

    private void RebuildOrderRows()
    {
        var selected = _selectedOrderRow;
        foreach (var row in OrderRows) row.Dispose();
        OrderRows.Clear();
        var seenMetrics = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var group in OrderedContentItems)
        {
            OrderRows.Add(new OverlayBlockOrderRowViewModel(group));
            var metrics = GetMetricsForItem(group).ToList();
            if (_metricOrder.TryGetValue(GetMetricOrderKey(group.Kind, group.Value), out var order))
            {
                var positions = order.Select((identity, index) => (identity, index))
                    .ToDictionary(pair => pair.identity, pair => pair.index, StringComparer.OrdinalIgnoreCase);
                metrics = metrics.OrderBy(metric => positions.TryGetValue(metric.Identity, out var index) ? index : int.MaxValue).ToList();
            }
            foreach (var metric in metrics)
                if (seenMetrics.Add(metric.Identity))
                    OrderRows.Add(new OverlayBlockOrderRowViewModel(group, metric));
        }
        if (selected is not null)
            SelectedOrderRow = OrderRows.FirstOrDefault(row => row.IsMetric == selected.IsMetric
                && row.GroupKey.Equals(selected.GroupKey, StringComparison.OrdinalIgnoreCase)
                && row.Identity.Equals(selected.Identity, StringComparison.OrdinalIgnoreCase));
        Raise(nameof(CanMoveContentUp));
        Raise(nameof(CanMoveContentDown));
        Raise(nameof(CanRemoveSelectedContent));
        Raise(nameof(RemoveSelectedContentText));
    }
}

