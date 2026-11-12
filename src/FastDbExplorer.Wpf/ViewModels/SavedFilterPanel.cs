using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using FastDbExplorer.Application.Abstractions;
using FastDbExplorer.Application.Filters;
using FastDbExplorer.Domain;
using FastDbExplorer.Wpf.Localization;

namespace FastDbExplorer.Wpf.ViewModels;

/// <summary>
/// The saved filters of one table: the chips, the name box and the status line. Storage problems become status text,
/// never exceptions. It does not touch the grid; the table view model applies a chosen filter to its rows.
/// </summary>
public sealed partial class SavedFilterPanel : ObservableObject
{
    private readonly ISavedFilterStore _store;
    private readonly SavedFilterScope _scope;

    public SavedFilterPanel(ISavedFilterStore store, SavedFilterScope scope)
    {
        _store = store;
        _scope = scope;
    }

    /// <summary>Saved filters that belong to this exact server, database and table.</summary>
    public ObservableCollection<SavedFilter> Items { get; } = [];

    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string _status = "";

    public async Task LoadAsync(CancellationToken ct)
    {
        try
        {
            var all = await _store.LoadAsync(ct);
            Items.Clear();
            foreach (var filter in all.Where(filter => SavedFilterBook.BelongsTo(filter, _scope))) Items.Add(filter);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Status = SavedFilterStrings.LoadFailed(ex.Message);
        }
    }

    /// <summary>
    /// Saves the rows under the typed name. Saving under an existing name replaces that filter instead of adding a second one.
    /// </summary>
    public async Task SaveAsync(IReadOnlyList<FilterRowViewModel> rows, bool useOr, CancellationToken ct)
    {
        var name = Name.Trim();
        if (name.Length == 0) { Status = SavedFilterStrings.NameRequired; return; }
        if (rows.Count == 0) { Status = SavedFilterStrings.NoFilters; return; }
        if (!rows.All(row => row.IsComplete)) { Status = Strings.ErrIncompleteFilter; return; }

        var filter = SavedFilterBook.Prepare(name, _scope, Items, useOr,
            rows.Select(row => row.ToCondition()).ToList(), DateTime.UtcNow);
        try
        {
            await _store.SaveAsync(filter, ct);
            await LoadAsync(ct);
            Status = SavedFilterStrings.Saved(name);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Status = SavedFilterStrings.SaveFailed(ex.Message);
        }
    }

    public async Task DeleteAsync(SavedFilter saved, CancellationToken ct)
    {
        try
        {
            await _store.RemoveAsync(saved.Id, ct);
            await LoadAsync(ct);
            Status = SavedFilterStrings.Deleted(saved.Name);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Status = SavedFilterStrings.DeleteFailed(ex.Message);
        }
    }

    /// <summary>Shows the name of a filter the user just put back into the form.</summary>
    public void ShowApplied(SavedFilter saved)
    {
        Name = saved.Name;
        Status = SavedFilterStrings.Applied(saved.Name);
    }

}
