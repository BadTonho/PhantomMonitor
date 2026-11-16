using System;
using System.Collections.Generic;

namespace PhantomMonitor.App.ViewModels;

/// <summary>
/// Aplica busca textual e filtro de disponibilidade sobre os grupos e linhas do inventário
/// sem recriar as coleções observáveis.
/// </summary>
public static class InventoryFilter
{
    /// <summary>
    /// Atualiza <see cref="InventoryGroupViewModel.IsVisible"/> e <see cref="InventoryRowViewModel.IsVisible"/>
    /// em toda a árvore e retorna a quantidade de grupos que permaneceram visíveis.
    /// </summary>
    public static int Apply(
        IEnumerable<InventoryGroupViewModel> groups,
        string? searchText,
        bool onlyAvailable)
    {
        var query = searchText?.Trim() ?? "";
        var hasQuery = query.Length > 0;
        var visibleGroupCount = 0;

        foreach (var group in groups)
        {
            var groupMatchesQuery = hasQuery && group.Title.Contains(query, StringComparison.OrdinalIgnoreCase);
            var groupHasVisibleRow = false;

            foreach (var row in group.Rows)
            {
                var rowVisible = ApplyRow(row, query, hasQuery, groupMatchesQuery, onlyAvailable);
                if (rowVisible)
                    groupHasVisibleRow = true;
            }

            group.IsVisible = groupHasVisibleRow;
            if (groupHasVisibleRow)
                visibleGroupCount++;
        }

        return visibleGroupCount;
    }

    private static bool ApplyRow(
        InventoryRowViewModel row,
        string query,
        bool hasQuery,
        bool groupMatchesQuery,
        bool onlyAvailable)
    {
        var selfAvailable = !onlyAvailable || row.IsAvailable;
        var selfMatchesQuery = !hasQuery || groupMatchesQuery || MatchesQuery(row, query);

        if (row.Children.Count == 0)
        {
            var visible = selfAvailable && selfMatchesQuery;
            row.IsVisible = visible;
            return visible;
        }

        var anyChildVisible = false;
        foreach (var child in row.Children)
        {
            var childAvailable = !onlyAvailable || child.IsAvailable;
            var childMatchesQuery = !hasQuery || groupMatchesQuery || selfMatchesQuery || MatchesQuery(child, query);
            var childVisible = childAvailable && childMatchesQuery;
            child.IsVisible = childVisible;
            if (childVisible)
                anyChildVisible = true;
        }

        var parentVisible = (selfAvailable && selfMatchesQuery) || anyChildVisible;
        row.IsVisible = parentVisible;
        return parentVisible;
    }

    private static bool MatchesQuery(InventoryRowViewModel row, string query) =>
        row.SensorName.Contains(query, StringComparison.OrdinalIgnoreCase)
        || row.Key.Contains(query, StringComparison.OrdinalIgnoreCase)
        || row.Source.Contains(query, StringComparison.OrdinalIgnoreCase);
}
