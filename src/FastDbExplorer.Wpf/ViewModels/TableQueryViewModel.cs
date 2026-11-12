using System.IO;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FastDbExplorer.Application.Abstractions;
using FastDbExplorer.Application.Search;
using FastDbExplorer.Application.Map;
using FastDbExplorer.Domain;
using FastDbExplorer.Wpf.Localization;
using FastDbExplorer.Wpf.Services;

namespace FastDbExplorer.Wpf.ViewModels;

public sealed partial class MapDetailColumnItem(string name, bool isSelected, Action<MapDetailColumnItem, bool> onChanged)
    : ObservableObject
{
    public string Name => name;

    [ObservableProperty] private bool _isSelected = isSelected;

    partial void OnIsSelectedChanged(bool value) => onChanged(this, value);
}

public sealed partial class ColumnItem(ColumnInfo info) : ObservableObject
{
    public string Name => info.Name;
    public string TypeName => info.TypeName;
    public bool IsKey => info.IsPrimaryKey;

    [ObservableProperty] private bool _isSelected;
}

/// <summary>A nearby-search point chosen by the user: the centre and the radius in metres.</summary>
public sealed record SearchPoint(double Latitude, double Longitude, double RadiusMeters);

public sealed partial class TableQueryViewModel : ObservableObject
{
    private const int StreamPageSize = 500;
    private const int DefaultSelectedColumns = 20;
    private const int MaxMapDetailColumns = MapPointFactory.MaxDetailColumns;

    private sealed record PageCursor(long Offset, object?[]? AfterKey);

    private readonly IDatabaseMetadataService _metadata;
    private readonly ConnectionSettings _settings;
    private readonly IFileDialogService _dialogs;
    private readonly IQueryReportWriter _queryReportWriter;
    private readonly string _database;
    private readonly TableInfo _table;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Stack<PageCursor> _history = new();
    private List<ColumnInfo> _keyColumns = [];
    private IReadOnlyList<ColumnInfo> _columnMeta = [];
    private SearchPoint? _searchPoint;

    /// <summary>The nearby-search point in effect for this table, or null.</summary>
    public SearchPoint? CurrentSearchPoint => _searchPoint;

    /// <summary>
    /// Sets or clears the nearby point. Runs the query when the table is ready, or when a result is on screen
    /// (so clearing the point refreshes it). Before the columns are loaded, InitializeAsync runs it.
    /// </summary>
    public void SetSearchPoint(SearchPoint? point)
    {
        _searchPoint = point;
        if (Columns.Count > 0 && (point is not null || HasResult) && CanRun())
            RunCommand.Execute(null);
    }

    private IReadOnlyList<string> _displayColumns = [];
    private PageCursor _current = new(0, null);
    private PageRequest? _base;
    private PageResult? _lastResult;
    private CancellationTokenSource? _running;
    private long _exportedRows;
    private bool _suppressMapDetailChange;

    public TableQueryViewModel(
        IDatabaseMetadataService metadata, ConnectionSettings settings, string database, TableInfo table,
        IFileDialogService dialogs, IQueryReportWriter queryReportWriter, ISavedFilterStore savedFilters)
    {
        _metadata = metadata;
        _settings = settings;
        _database = database;
        _table = table;
        _dialogs = dialogs;
        _queryReportWriter = queryReportWriter;
        SavedFilters = new SavedFilterPanel(savedFilters, new SavedFilterScope(settings.Server, database, table.Schema, table.Name));
    }

    public event Action<IReadOnlyList<string>>? ResultColumnsChanged;
    public event Action<string, IReadOnlyList<QueryMapPoint>>? ShowSelectedRowsOnMapRequested;

    public string TableName => _table.FullName;
    public long ApproxRows => _table.ApproxRows;
    public ObservableCollection<ColumnItem> Columns { get; } = [];
    /// <summary>All table columns are available here, independently of which columns the result grid displays.</summary>
    public ObservableCollection<MapDetailColumnItem> MapDetailColumns { get; } = [];
    public ObservableCollection<FilterRowViewModel> Filters { get; } = [];

    /// <summary>Saved filters of this exact server, database and table: the chips, the name box and the status line.</summary>
    public SavedFilterPanel SavedFilters { get; }
    public IReadOnlyList<OperatorOption> Operators => OperatorOption.All;
    public IReadOnlyList<int> PageSizes { get; } = [50, 100, 250, 500];
    public IReadOnlyList<string> ResultColumns { get; private set; } = [];
    /// <summary>Coordinate settings use all table columns, not only the visible result-grid columns.</summary>
    public IReadOnlyList<string> CoordinateColumns { get; private set; } = [];
    public int MapDetailColumnCount => MapDetailColumns.Count(column => column.IsSelected);
    public int SelectedRowCount => SelectedRows.Count;
    public bool NeedsOrderColumn => !UsesPrimaryKey;

    [ObservableProperty] private IReadOnlyList<string> _filterableColumns = [];
    [ObservableProperty] private IReadOnlyList<GridRow> _rows = [];
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedRowCount))]
    [NotifyCanExecuteChangedFor(nameof(ShowSelectedOnMapCommand))]
    private IReadOnlyList<GridRow> _selectedRows = [];
    [ObservableProperty][NotifyPropertyChangedFor(nameof(MatchAll))] private bool _useOr;

    public bool MatchAll { get => !UseOr; set => UseOr = !value; }

    [ObservableProperty] private int _pageSize = 100;
    [ObservableProperty] private string? _orderColumn;
    [ObservableProperty][NotifyCanExecuteChangedFor(nameof(ShowSelectedOnMapCommand))][NotifyCanExecuteChangedFor(nameof(ShowAllOnMapCommand))] private string? _latitudeColumn;
    [ObservableProperty][NotifyCanExecuteChangedFor(nameof(ShowSelectedOnMapCommand))][NotifyCanExecuteChangedFor(nameof(ShowAllOnMapCommand))] private string? _longitudeColumn;
    [ObservableProperty] private string _keyDescription = "";
    [ObservableProperty] private string _errorMessage = "";
    [ObservableProperty] private string _pageInfo = "";
    [ObservableProperty] private string _resultCountText = "";
    [ObservableProperty][NotifyCanExecuteChangedFor(nameof(ExportExcelCommand))][NotifyCanExecuteChangedFor(nameof(ShowAllOnMapCommand))] private bool _hasResult;
    [ObservableProperty] private bool _isConfigVisible = true;
    [ObservableProperty][NotifyPropertyChangedFor(nameof(NeedsOrderColumn))] private bool _usesPrimaryKey;
    [ObservableProperty][NotifyCanExecuteChangedFor(nameof(RunCommand))] private bool _isLoadingColumns;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    [NotifyCanExecuteChangedFor(nameof(RunCommand))]
    [NotifyCanExecuteChangedFor(nameof(NextCommand))]
    [NotifyCanExecuteChangedFor(nameof(PreviousCommand))]
    [NotifyCanExecuteChangedFor(nameof(ExportExcelCommand))]
    [NotifyCanExecuteChangedFor(nameof(ShowSelectedOnMapCommand))]
    [NotifyCanExecuteChangedFor(nameof(ShowAllOnMapCommand))]
    private bool _isBusy;

    [ObservableProperty][NotifyCanExecuteChangedFor(nameof(NextCommand))] private bool _hasNext;
    [ObservableProperty][NotifyCanExecuteChangedFor(nameof(PreviousCommand))] private bool _hasPrevious;

    public async Task InitializeAsync()
    {
        IsLoadingColumns = true;
        try
        {
            var meta = await _metadata.GetColumnsAsync(_settings, _database, _table.Schema, _table.Name, _lifetime.Token);
            foreach (var column in meta)
                Columns.Add(new ColumnItem(column) { IsSelected = Columns.Count < DefaultSelectedColumns });

            _columnMeta = meta;
            _ = SavedFilters.LoadAsync(_lifetime.Token);
            _keyColumns = meta.Where(c => c.IsPrimaryKey).OrderBy(c => c.KeyOrdinal).ToList();
            UsesPrimaryKey = _keyColumns.Count > 0 && _keyColumns.All(c => c.IsFilterable);
            KeyDescription = string.Join(", ", _keyColumns.Select(c => c.Name));
            FilterableColumns = meta.Where(c => c.IsFilterable).Select(c => c.Name).ToList();
            CoordinateColumns = meta.Where(c => c.IsFilterable).Select(c => c.Name).ToList();
            OnPropertyChanged(nameof(CoordinateColumns));
            LatitudeColumn ??= QueryMapProjection.DetectCoordinateColumn(_columnMeta, true);
            LongitudeColumn ??= QueryMapProjection.DetectCoordinateColumn(_columnMeta, false);

            var displayName = Columns.FirstOrDefault(column => column.IsSelected)?.Name;
            var defaultDetails = meta.Where(column => column.IsFilterable).Select(column => column.Name)
                .Where(name => name != displayName && name != LatitudeColumn && name != LongitudeColumn)
                .Take(2)
                .ToHashSet(StringComparer.Ordinal);
            MapDetailColumns.Clear();
            foreach (var column in meta.Where(column => column.IsFilterable))
                MapDetailColumns.Add(new MapDetailColumnItem(column.Name, defaultDetails.Contains(column.Name), OnMapDetailColumnChanged));
            OnPropertyChanged(nameof(MapDetailColumnCount));
        }
        catch (OperationCanceledException) { }
        catch (DatabaseAccessException ex) { ErrorMessage = Strings.Describe(ex); }
        finally { IsLoadingColumns = false; }
        // A point chosen before the columns were ready runs now.
        if (_searchPoint is not null && Columns.Count > 0 && CanRun()) RunCommand.Execute(null);
    }

    public void Shutdown() => _lifetime.Cancel();

    private bool CanRun() => !IsBusy && !IsLoadingColumns && Columns.Count > 0;
    private bool CanGoNext() => HasNext && !IsBusy;
    private bool CanGoPrevious() => HasPrevious && !IsBusy;
    private bool CanExportExcel() => HasResult && _base is not null && !IsBusy;
    private bool CanShowSelectedOnMap() => !IsBusy && SelectedRows.Count > 0
        && LatitudeColumn is not null && LongitudeColumn is not null
        && LatitudeColumn != LongitudeColumn
        && CoordinateColumns.Contains(LatitudeColumn) && CoordinateColumns.Contains(LongitudeColumn);

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task RunAsync()
    {
        if (!TryBuildRequest(out var request, out var displayColumns)) return;
        var previousBase = _base;
        _base = request;
        var first = new PageCursor(0, null);
        var fetched = await FetchAsync(first);
        if (fetched is null)
        {
            _base = previousBase;
            ExportExcelCommand.NotifyCanExecuteChanged();
            return;
        }
        _displayColumns = displayColumns;
        _history.Clear();
        _current = first;
        Show(fetched.Value.Result, fetched.Value.Ms);
        StartCount();
    }

    [RelayCommand(CanExecute = nameof(CanGoNext))]
    private async Task NextAsync()
    {
        if (_base is null || _lastResult is null) return;
        var offset = _current.Offset + _base.PageSize;
        var next = new PageCursor(offset, PageStreamer.AfterKeyFor(_base, offset, _lastResult.LastKey));
        var fetched = await FetchAsync(next);
        if (fetched is null) return;
        _history.Push(_current);
        _current = next;
        Show(fetched.Value.Result, fetched.Value.Ms);
    }

    [RelayCommand(CanExecute = nameof(CanGoPrevious))]
    private async Task PreviousAsync()
    {
        var previous = _history.Peek();
        var fetched = await FetchAsync(previous);
        if (fetched is null) return;
        _history.Pop();
        _current = previous;
        Show(fetched.Value.Result, fetched.Value.Ms);
    }

    // Stops the running query and the row count started with it. Esc in the query view calls this too.
    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void Cancel()
    {
        _running?.Cancel();
        _countCts?.Cancel();
    }

    private bool CanCancel() => IsBusy;
    [RelayCommand] private void ToggleConfig() => IsConfigVisible = !IsConfigVisible;

    /// <summary>
    /// Saves every row of the query. A normal result becomes the .xlsx the user chose; a very big one is written as CSV, split
    /// into several files, so no single file gets heavy (the writer decides, see <see cref="ExportLimits"/>).
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanExportExcel))]
    private async Task ExportExcelAsync()
    {
        var path = _dialogs.PickExcelSaveFile();
        if (path is null || _base is null) return;

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _running = cts;
        _exportedRows = 0;
        IsBusy = true;
        ErrorMessage = "";
        try
        {
            var result = await _queryReportWriter.WriteQueryAsync(_displayColumns, ReadExportRowsAsync(cts.Token), path, cts.Token);
            PageInfo = result.Format == QueryExportFormat.Xlsx
                ? string.Format(CultureInfo.CurrentCulture, Strings.ExportCompleted, result.Files[0], result.Rows)
                : ExportStrings.CsvCompleted(result);
        }
        catch (OperationCanceledException) { PageInfo = Strings.Cancelled; }
        catch (Exception ex) { ErrorMessage = Strings.ExportFailed + ex.Message; }
        finally
        {
            IsBusy = false;
            _running = null;
        }
    }

    private const int CountPageSize = 500;
    private const int MaxAllMapPoints = 10_000;
    private CancellationTokenSource? _countCts;

    /// <summary>
    /// Counts every row of the search in the background, page by page (no COUNT(*)), so the result line states the real total.
    /// A new search cancels the count of the previous one; the grid never waits for it.
    /// </summary>
    private void StartCount()
    {
        _countCts?.Cancel();
        var cts = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _countCts = cts;
        if (_base is null) return;
        _ = CountRowsAsync(_base with { Columns = QueryColumnsOf(_displayColumns) }, cts.Token);
    }

    private Task<PageResult> FetchPage(PageRequest request, CancellationToken ct) =>
        _metadata.GetPageAsync(_settings, request, ct);

    private async Task CountRowsAsync(PageRequest request, CancellationToken ct)
    {
        ResultCountText = Strings.ResultCounting(0);
        long total = 0;
        try
        {
            await foreach (var page in PageStreamer.ReadPagesAsync(FetchPage, request, CountPageSize, ct))
            {
                total += page.Rows.Count;
                ResultCountText = page.HasMore ? Strings.ResultCounting(total) : Strings.ResultFound(total);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception) { ResultCountText = ""; } // the count is extra information: it never shows an error
    }

    private bool CanShowAllOnMap() => !IsBusy && HasResult && _base is not null
        && LatitudeColumn is not null && LongitudeColumn is not null
        && LatitudeColumn != LongitudeColumn;

    /// <summary>
    /// Sends every row of the search to the map (not only the rows of this page), up to <see cref="MaxAllMapPoints"/>.
    /// Rows are streamed page by page; only the coordinates, the name and the chosen detail columns are read.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanShowAllOnMap))]
    private async Task ShowAllOnMapAsync()
    {
        var latitudeColumn = LatitudeColumn;
        var longitudeColumn = LongitudeColumn;
        if (_base is null || latitudeColumn is null || longitudeColumn is null || latitudeColumn == longitudeColumn
            || !CoordinateColumns.Contains(latitudeColumn) || !CoordinateColumns.Contains(longitudeColumn))
        {
            ErrorMessage = Strings.ErrPickCoordinateColumns;
            return;
        }

        var nameColumn = _displayColumns.FirstOrDefault();
        var detailColumns = MapDetailColumns.Where(item => item.IsSelected).Select(item => item.Name).ToList();
        var columns = MapColumnPlanner.ForAllRows(latitudeColumn, longitudeColumn, nameColumn, detailColumns, MaxMapDetailColumns);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _running = cts;
        IsBusy = true;
        ErrorMessage = "";
        var points = new List<QueryMapPoint>();
        var scanned = 0;
        var skipped = 0;
        var truncated = false;
        try
        {
            await foreach (var values in ReadMapValuesAsync(_base with { Columns = columns }, cts.Token))
            {
                scanned++;
                var name = nameColumn is not null && values.TryGetValue(nameColumn, out var nameValue)
                    ? GridCellFactory.Create(nameValue).Display
                    : null;
                if (!TryProjectPoint(values, latitudeColumn, longitudeColumn, name, scanned, out var point))
                {
                    skipped++;
                    continue;
                }
                if (points.Count == MaxAllMapPoints) { truncated = true; break; }
                points.Add(point);
                if (scanned % 1000 == 0) PageInfo = Strings.MapSendingProgress(scanned);
            }

            if (points.Count == 0) { ErrorMessage = Strings.ErrNoValidCoordinates; return; }
            PageInfo = truncated ? Strings.MapSentLimited(points.Count) : Strings.MapSentAll(points.Count, skipped);
            ShowSelectedRowsOnMapRequested?.Invoke(TableName, points);
        }
        catch (OperationCanceledException) { PageInfo = Strings.Cancelled; }
        catch (Exception ex) { ErrorMessage = Strings.ErrSendingToMap + ex.Message; }
        finally
        {
            IsBusy = false;
            _running = null;
        }
    }

    private async IAsyncEnumerable<IReadOnlyDictionary<string, object?>> ReadMapValuesAsync(
        PageRequest request, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        await foreach (var row in PageStreamer.ReadRowsAsync(FetchPage, request, StreamPageSize, ct))
            yield return QueryMapProjection.Values(row.Columns, row.Values);
    }

    private bool TryProjectPoint(IReadOnlyDictionary<string, object?> values, string latitudeColumn, string longitudeColumn,
        string? name, int rowNumber, out QueryMapPoint point) =>
        QueryMapProjection.TryCreatePoint(values, latitudeColumn, longitudeColumn, name, rowNumber,
            MapDetailColumns.Where(item => item.IsSelected).Select(item => item.Name),
            value => GridCellFactory.Create(value).FullValue, out point);

    [RelayCommand(CanExecute = nameof(CanShowSelectedOnMap))]
    private async Task ShowSelectedOnMapAsync()
    {
        if (SelectedRows.Count == 0) { ErrorMessage = Strings.ErrSelectRows; return; }
        var latitudeColumn = LatitudeColumn;
        var longitudeColumn = LongitudeColumn;
        if (latitudeColumn is null || longitudeColumn is null) { ErrorMessage = Strings.ErrPickCoordinateColumns; return; }
        if (latitudeColumn == longitudeColumn
            || !CoordinateColumns.Contains(latitudeColumn) || !CoordinateColumns.Contains(longitudeColumn))
        {
            ErrorMessage = Strings.ErrPickCoordinateColumns;
            return;
        }
        if (!await EnsureMapValuesAvailableAsync()) return;

        var points = new List<QueryMapPoint>(SelectedRows.Count);
        for (var index = 0; index < SelectedRows.Count; index++)
        {
            var row = SelectedRows[index];
            if (TryProjectPoint(row.MapValues, latitudeColumn, longitudeColumn,
                    row.Cells.FirstOrDefault()?.Display, index + 1, out var point))
                points.Add(point);
        }

        if (points.Count == 0) { ErrorMessage = Strings.ErrNoValidCoordinates; return; }
        ErrorMessage = "";
        ShowSelectedRowsOnMapRequested?.Invoke(TableName, points);
    }

    public void SetSelectedRows(IReadOnlyList<GridRow> rows) => SelectedRows = rows;

    [RelayCommand]
    private void AddFilter()
        => Filters.Add(new FilterRowViewModel(FilterableColumns.FirstOrDefault()) { ColumnMeta = _columnMeta });

    [RelayCommand] private void RemoveFilter(FilterRowViewModel row) => Filters.Remove(row);

    [RelayCommand]
    private Task SaveFilterAsync() => SavedFilters.SaveAsync(Filters, UseOr, _lifetime.Token);

    /// <summary>Puts a saved filter's conditions back into the form (a chip click). Conditions can still be changed after.</summary>
    [RelayCommand]
    private void ApplySavedFilter(SavedFilter? saved)
    {
        if (saved is null) return;

        Filters.Clear();
        foreach (var condition in saved.Conditions)
        {
            var operatorOption = OperatorOption.All.FirstOrDefault(o => o.Operator == condition.Operator) ?? OperatorOption.All[0];
            var row = new FilterRowViewModel(condition.Column) { ColumnMeta = _columnMeta, SelectedOperator = operatorOption };
            row.Value = condition.Value ?? "";
            row.Value2 = condition.Value2 ?? "";
            Filters.Add(row);
        }
        UseOr = saved.UseOr;
        SavedFilters.ShowApplied(saved);
    }

    [RelayCommand]
    private Task DeleteSavedFilterAsync(SavedFilter? saved) =>
        saved is null ? Task.CompletedTask : SavedFilters.DeleteAsync(saved, _lifetime.Token);
    [RelayCommand] private void SelectAllColumns() { foreach (var c in Columns) c.IsSelected = true; }
    [RelayCommand] private void SelectNoColumns() { foreach (var c in Columns) c.IsSelected = false; }

    private void OnMapDetailColumnChanged(MapDetailColumnItem item, bool selected)
    {
        if (_suppressMapDetailChange) return;
        if (selected && MapDetailColumnCount > MaxMapDetailColumns)
        {
            _suppressMapDetailChange = true;
            item.IsSelected = false;
            _suppressMapDetailChange = false;
        }
        OnPropertyChanged(nameof(MapDetailColumnCount));
    }

    private IReadOnlyList<string> GetMapRequiredColumns() => MapColumnPlanner.RequiredForGrid(
        CoordinateColumns, LatitudeColumn, LongitudeColumn,
        MapDetailColumns.Where(column => column.IsSelected).Select(column => column.Name).ToList(),
        UsesPrimaryKey, _keyColumns.Select(column => column.Name).ToList(), OrderColumn);

    private bool SameRowsByOrder(PageResult refreshed) => RowIdentity.SameRows(
        Rows.Select(row => row.MapValues).ToList(), refreshed.Columns, refreshed.Rows,
        _displayColumns.Concat(_base!.OrderBy).Distinct(StringComparer.Ordinal).ToList());

    private async Task<bool> EnsureMapValuesAvailableAsync()
    {
        if (_base is null || _lastResult is null) return false;
        var requiredColumns = QueryColumnsOf(_displayColumns).Concat(GetMapRequiredColumns()).Distinct(StringComparer.Ordinal).ToList();
        if (_base.Columns.SequenceEqual(requiredColumns, StringComparer.Ordinal)) return true;

        var previousBase = _base;
        _base = _base with { Columns = requiredColumns };
        var fetched = await FetchAsync(_current);
        if (fetched is null)
        {
            _base = previousBase;
            return false;
        }

        if (fetched.Value.Result.Rows.Count != Rows.Count || !SameRowsByOrder(fetched.Value.Result))
        {
            Show(fetched.Value.Result, fetched.Value.Ms);
            ErrorMessage = Strings.ErrSelectRows;
            return false;
        }

        _lastResult = fetched.Value.Result;
        for (var index = 0; index < Rows.Count; index++)
            Rows[index].UpdateMapValues(QueryMapProjection.Values(fetched.Value.Result.Columns, fetched.Value.Result.Rows[index]));
        HasNext = fetched.Value.Result.HasMore;
        return true;
    }

    // The nearby distance is a computed output, not a table column: requests only ask for real columns.
    private static IReadOnlyList<string> QueryColumnsOf(IEnumerable<string> displayColumns) =>
        displayColumns.Where(c => c != SpatialSearch.DistanceAlias).ToList();

    private bool TryBuildRequest(out PageRequest request, out IReadOnlyList<string> displayColumns)
    {
        ErrorMessage = "";
        var filtersComplete = Filters.All(row => row.IsComplete);
        var filters = filtersComplete
            ? Filters.Select(row => row.ToCondition()).ToList()
            : new List<FilterCondition>();
        var plan = QueryRequestPlanner.Plan(new QueryPlanInput(
            Database: _database, Schema: _table.Schema, Table: _table.Name, Meta: _columnMeta,
            SelectedColumns: Columns.Where(c => c.IsSelected).Select(c => c.Name).ToList(),
            FiltersComplete: filtersComplete, Filters: filters, UseOr: UseOr,
            UsesPrimaryKey: UsesPrimaryKey, KeyColumns: _keyColumns.Select(c => c.Name).ToList(),
            OrderColumn: OrderColumn, MapRequiredColumns: GetMapRequiredColumns(),
            LatitudeColumn: LatitudeColumn, LongitudeColumn: LongitudeColumn,
            Nearby: _searchPoint is { } point ? new NearbyPoint(point.Latitude, point.Longitude, point.RadiusMeters) : null,
            PageSize: PageSize));

        displayColumns = plan.DisplayColumns;
        if (plan.Request is { } planned)
        {
            request = planned;
            return true;
        }

        request = null!;
        ErrorMessage = plan.Error switch
        {
            QueryPlanError.NoColumns => Strings.ErrNoColumns,
            QueryPlanError.IncompleteFilter => Strings.ErrIncompleteFilter,
            QueryPlanError.NoCoordinateColumns => Strings.ErrNoCoordinateColumns,
            QueryPlanError.NoOrder => Strings.ErrPickOrder,
            _ => throw new InvalidOperationException("A query plan without a request must name its error.")
        };
        return false;
    }

    private async Task<(PageResult Result, long Ms)?> FetchAsync(PageCursor cursor)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _running = cts;
        IsBusy = true;
        ErrorMessage = "";
        var watch = Stopwatch.StartNew();
        try
        {
            var request = _base! with { Offset = cursor.Offset, AfterKey = cursor.AfterKey };
            var result = await _metadata.GetPageAsync(_settings, request, cts.Token);
            return (result, watch.ElapsedMilliseconds);
        }
        catch (OperationCanceledException) { PageInfo = Strings.Cancelled; return null; }
        catch (DatabaseAccessException ex) { ErrorMessage = Strings.Describe(ex); return null; }
        catch (Exception ex) when (ex is ArgumentException or FormatException or NotSupportedException)
        {
            ErrorMessage = Strings.ErrInvalidQuery + ex.Message;
            return null;
        }
        finally { IsBusy = false; _running = null; }
    }

    private void Show(PageResult result, long milliseconds)
    {
        var firstResult = !HasResult;
        _lastResult = result;
        ResultColumns = _displayColumns;
        OnPropertyChanged(nameof(ResultColumns));
        ResultColumnsChanged?.Invoke(ResultColumns);
        if (LatitudeColumn is null) LatitudeColumn = QueryMapProjection.DetectCoordinateColumn(_columnMeta, true);
        if (LongitudeColumn is null) LongitudeColumn = QueryMapProjection.DetectCoordinateColumn(_columnMeta, false);
        var resultIndexes = result.Columns.Select((name, index) => (name, index))
            .ToDictionary(pair => pair.name, pair => pair.index, StringComparer.Ordinal);
        var displayIndexes = _displayColumns.Select(name => resultIndexes[name]).ToArray();
        Rows = result.Rows.Select(row => new GridRow(
            displayIndexes.Select(index => GridCellFactory.Create(row[index])).ToArray(),
            QueryMapProjection.Values(result.Columns, row))).ToList();
        SelectedRows = [];
        HasResult = true;
        // The settings card is tall. Once the first result is there, the table gets the room; the gear button brings the card back.
        if (firstResult) IsConfigVisible = false;
        HasNext = result.HasMore;
        HasPrevious = _history.Count > 0;
        PageInfo = result.Rows.Count == 0
            ? Strings.NoResults
            : $"{Strings.RowsRange(_current.Offset + 1, _current.Offset + result.Rows.Count)} · {milliseconds:N0} ms";
    }

    private async IAsyncEnumerable<object?[]> ReadExportRowsAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        var request = _base! with { Columns = QueryColumnsOf(_displayColumns) };
        await foreach (var row in PageStreamer.ReadRowsAsync(FetchPage, request, StreamPageSize, ct))
        {
            _exportedRows++;
            yield return row.Values;
        }
    }
}
