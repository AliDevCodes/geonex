using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FastDbExplorer.Application.Connections;
using FastDbExplorer.Application.Diagnostics;
using FastDbExplorer.Application.Export;
using FastDbExplorer.Application.Map;
using FastDbExplorer.Application.Search;
using FastDbExplorer.Application.Filters;
using FastDbExplorer.Application.Monitoring;
using FastDbExplorer.Domain;
using FastDbExplorer.Domain.Connections;
using FastDbExplorer.Domain.Filters;
using FastDbExplorer.Domain.Map;
using FastDbExplorer.Domain.Search;
using FastDbExplorer.Wpf.Diagnostics;
using FastDbExplorer.Wpf.Localization;
using FastDbExplorer.Wpf.Services;

namespace FastDbExplorer.Wpf.ViewModels.Search;

/// <summary>
/// The table query screen: bindings, commands, messages and busy state. Everything about the query itself (the request, the
/// pages, the streams) lives in <see cref="TableQuerySession"/>, what the columns mean in <see cref="TableColumnProfile"/>,
/// and turning rows into map points in <see cref="GridMapProjection"/>.
/// </summary>
public sealed partial class TableQueryViewModel(
    IDatabaseMetadataService metadata, ConnectionSettings settings, string database, TableInfo table,
    IFileDialogService dialogs, IQueryReportWriter queryReportWriter, ISavedFilterStore savedFilters, IAppLogger logger) : ObservableObject
{
    private const int DefaultSelectedColumns = 20;
    private const int MaxAllMapPoints = 10_000;
    private const int MaxMapDetailColumns = MapPointFactory.MaxDetailColumns;

    private readonly TableQuerySession _session = new(metadata, settings);
    private readonly CancellationTokenSource _lifetime = new();
    private IReadOnlyList<string> _keyColumnNames = [];
    private IReadOnlyList<ColumnInfo> _columnMeta = [];
    private SearchPoint? _searchPoint;
    private CancellationTokenSource? _running;
    private CancellationTokenSource? _countCts;
    private bool _suppressMapDetailChange;

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

    public event Action<IReadOnlyList<string>>? ResultColumnsChanged;
    public event Action<string, IReadOnlyList<QueryMapPoint>>? ShowSelectedRowsOnMapRequested;

    public string TableName => table.FullName;
    public long ApproxRows => table.ApproxRows;
    public ObservableCollection<ColumnItem> Columns { get; } = [];
    /// <summary>All table columns are available here, independently of which columns the result grid displays.</summary>
    public ObservableCollection<MapDetailColumnItem> MapDetailColumns { get; } = [];
    public ObservableCollection<FilterRowViewModel> Filters { get; } = [];

    /// <summary>Saved filters of this exact server, database and table: the chips, the name box and the status line.</summary>
    public SavedFilterPanel SavedFilters { get; } =
        new(savedFilters, new SavedFilterScope(settings.Server, database, table.Schema, table.Name), logger);
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
            var meta = await metadata.GetColumnsAsync(settings, database, table.Schema, table.Name, _lifetime.Token);
            foreach (var column in meta)
                Columns.Add(new ColumnItem(column) { IsSelected = Columns.Count < DefaultSelectedColumns });

            _columnMeta = meta;
            _ = SavedFilters.LoadAsync(_lifetime.Token); // reports its own failures in its status line
            ApplyProfile(TableColumnProfile.From(
                meta, Columns.FirstOrDefault(column => column.IsSelected)?.Name, LatitudeColumn, LongitudeColumn));
        }
        catch (OperationCanceledException) { }
        catch (DatabaseAccessException ex) { ErrorMessage = Strings.Describe(ex); }
        // The caller does not await this method, so an unexpected failure must end up on screen, not in an unobserved task.
        catch (Exception ex) { ErrorMessage = logger.Describe(nameof(InitializeAsync), ex); }
        finally { IsLoadingColumns = false; }
        // A point chosen before the columns were ready runs now.
        if (_searchPoint is not null && Columns.Count > 0 && CanRun()) RunCommand.Execute(null);
    }

    private void ApplyProfile(TableColumnProfile profile)
    {
        _keyColumnNames = profile.KeyColumnNames;
        UsesPrimaryKey = profile.UsesPrimaryKey;
        KeyDescription = profile.KeyDescription;
        FilterableColumns = profile.FilterableColumns;
        CoordinateColumns = profile.FilterableColumns;
        OnPropertyChanged(nameof(CoordinateColumns));
        LatitudeColumn = profile.LatitudeColumn;
        LongitudeColumn = profile.LongitudeColumn;

        MapDetailColumns.Clear();
        foreach (var name in profile.FilterableColumns)
            MapDetailColumns.Add(new MapDetailColumnItem(name, profile.DefaultDetailColumns.Contains(name), OnMapDetailColumnChanged));
        OnPropertyChanged(nameof(MapDetailColumnCount));
    }

    /// <summary>
    /// Stops everything this screen started. The cancellation source is not disposed: a command that is still finishing
    /// may link to it, and a cancelled source holds no resource that needs releasing.
    /// </summary>
    public void Shutdown() => _lifetime.Cancel();

    private bool CanRun() => !IsBusy && !IsLoadingColumns && Columns.Count > 0;
    private bool CanGoNext() => HasNext && !IsBusy;
    private bool CanGoPrevious() => HasPrevious && !IsBusy;
    private bool CanExportExcel() => HasResult && _session.Request is not null && !IsBusy;
    private bool CanShowSelectedOnMap() => !IsBusy && SelectedRows.Count > 0 && HasValidCoordinateColumns;
    private bool CanShowAllOnMap() => !IsBusy && HasResult && _session.Request is not null && HasValidCoordinateColumns;
    private bool CanCancel() => IsBusy;

    /// <summary>True when the latitude and longitude columns are two different coordinate columns of this table.</summary>
    private bool HasValidCoordinateColumns => LatitudeColumn is not null && LongitudeColumn is not null
        && LatitudeColumn != LongitudeColumn
        && CoordinateColumns.Contains(LatitudeColumn) && CoordinateColumns.Contains(LongitudeColumn);

    private List<string> SelectedMapDetails() =>
        MapDetailColumns.Where(column => column.IsSelected).Select(column => column.Name).ToList();

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task RunAsync()
    {
        if (!TryBuildRequest(out var request, out var displayColumns)) return;
        var fetched = await FetchAsync(request, TablePager.First);
        if (fetched is null) return;

        _session.StartQuery(request, displayColumns, fetched.Value.Result);
        Show(fetched.Value.Result, fetched.Value.Ms);
        StartCount();
    }

    [RelayCommand(CanExecute = nameof(CanGoNext))]
    private async Task NextAsync()
    {
        if (!_session.HasQuery) return;
        var next = _session.NextCursor();
        var fetched = await FetchAsync(_session.Request!, next);
        if (fetched is null) return;
        _session.MoveNext(next);
        Show(fetched.Value.Result, fetched.Value.Ms);
    }

    [RelayCommand(CanExecute = nameof(CanGoPrevious))]
    private async Task PreviousAsync()
    {
        var previous = _session.PreviousCursor();
        var fetched = await FetchAsync(_session.Request!, previous);
        if (fetched is null) return;
        _session.MovePrevious();
        Show(fetched.Value.Result, fetched.Value.Ms);
    }

    // Stops the running query and the row count started with it. Esc in the query view calls this too.
    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void Cancel()
    {
        _running?.Cancel();
        _countCts?.Cancel();
    }

    [RelayCommand] private void ToggleConfig() => IsConfigVisible = !IsConfigVisible;

    /// <summary>
    /// Saves every row of the query. A normal result becomes the .xlsx the user chose; a very big one is written as CSV, split
    /// into several files, so no single file gets heavy (the writer decides, see <see cref="ExportLimits"/>).
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanExportExcel))]
    private async Task ExportExcelAsync()
    {
        if (!_session.HasQuery) return;
        var path = dialogs.PickExcelSaveFile();
        if (path is null) return;

        await RunBusyAsync(async ct =>
        {
            try
            {
                var result = await queryReportWriter.WriteQueryAsync(_session.DisplayColumns, _session.ExportRowsAsync(ct), path, ct);
                PageInfo = result.Format == QueryExportFormat.Xlsx
                    ? string.Format(CultureInfo.CurrentCulture, Strings.ExportCompleted, result.Files[0], result.Rows)
                    : ExportStrings.CsvCompleted(result);
            }
            catch (OperationCanceledException) { PageInfo = Strings.Cancelled; }
            catch (Exception ex) { ErrorMessage = Strings.ExportFailed + logger.Describe(nameof(ExportExcelAsync), ex); }
        });
    }

    /// <summary>
    /// Counts every row of the search in the background, page by page (no COUNT(*)), so the result line states the real total.
    /// A new search cancels the count of the previous one; the grid never waits for it.
    /// </summary>
    private void StartCount()
    {
        _countCts?.Cancel();
        _countCts?.Dispose();
        var cts = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _countCts = cts;
        _ = CountRowsAsync(_session.CountAsync(cts.Token));
    }

    private async Task CountRowsAsync(IAsyncEnumerable<RowCountProgress> progress)
    {
        ResultCountText = Strings.ResultCounting(0);
        try
        {
            await foreach (var step in progress)
                ResultCountText = step.Complete ? Strings.ResultFound(step.Total) : Strings.ResultCounting(step.Total);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            // The count is extra information: it never shows an error, but still worth a log line.
            logger.Suppress(nameof(CountRowsAsync), ex);
            ResultCountText = "";
        }
    }

    /// <summary>
    /// Sends every row of the search to the map (not only the rows of this page), up to <see cref="MaxAllMapPoints"/>.
    /// Rows are streamed page by page; only the coordinates, the name and the chosen detail columns are read.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanShowAllOnMap))]
    private async Task ShowAllOnMapAsync()
    {
        if (_session.Request is not { } baseRequest || !HasValidCoordinateColumns)
        {
            ErrorMessage = Strings.ErrPickCoordinateColumns;
            return;
        }
        var latitudeColumn = LatitudeColumn!;
        var longitudeColumn = LongitudeColumn!;
        var nameColumn = _session.DisplayColumns.FirstOrDefault();
        var details = SelectedMapDetails();
        var columns = MapColumnPlanner.ForAllRows(latitudeColumn, longitudeColumn, nameColumn, details, MaxMapDetailColumns);

        await RunBusyAsync(async ct =>
        {
            try
            {
                var collection = await MapCollector.CollectAsync(
                    _session.MapValuesAsync(baseRequest with { Columns = columns }, ct),
                    (values, rowNumber) => GridMapProjection.Project(
                        values, latitudeColumn, longitudeColumn, GridMapProjection.NameOf(values, nameColumn), rowNumber, details),
                    MaxAllMapPoints,
                    scanned => PageInfo = Strings.MapSendingProgress(scanned));

                if (collection.Points.Count == 0) { ErrorMessage = Strings.ErrNoValidCoordinates; return; }
                PageInfo = collection.Truncated
                    ? Strings.MapSentLimited(collection.Points.Count)
                    : Strings.MapSentAll(collection.Points.Count, collection.Skipped);
                ShowSelectedRowsOnMapRequested?.Invoke(TableName, collection.Points);
            }
            catch (OperationCanceledException) { PageInfo = Strings.Cancelled; }
            catch (Exception ex) { ErrorMessage = Strings.ErrSendingToMap + logger.Describe(nameof(ShowAllOnMapAsync), ex); }
        });
    }

    [RelayCommand(CanExecute = nameof(CanShowSelectedOnMap))]
    private async Task ShowSelectedOnMapAsync()
    {
        if (SelectedRows.Count == 0) { ErrorMessage = Strings.ErrSelectRows; return; }
        if (!HasValidCoordinateColumns) { ErrorMessage = Strings.ErrPickCoordinateColumns; return; }
        var latitudeColumn = LatitudeColumn!;
        var longitudeColumn = LongitudeColumn!;
        var details = SelectedMapDetails();
        if (!await EnsureMapValuesAvailableAsync()) return;

        var points = GridMapProjection.ProjectRows(SelectedRows, latitudeColumn, longitudeColumn, details);
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
            ErrorMessage = MonitoringStrings.MaxMapDetailColumnsReached;
        }
        OnPropertyChanged(nameof(MapDetailColumnCount));
    }

    private IReadOnlyList<string> GetMapRequiredColumns() => MapColumnPlanner.RequiredForGrid(
        CoordinateColumns, LatitudeColumn, LongitudeColumn, SelectedMapDetails(), UsesPrimaryKey, _keyColumnNames, OrderColumn);

    private bool SameRowsByOrder(PageResult refreshed) => RowIdentity.SameRows(
        Rows.Select(row => row.MapValues).ToList(), refreshed.Columns, refreshed.Rows,
        _session.DisplayColumns.Concat(_session.Request!.OrderBy).Distinct(StringComparer.Ordinal).ToList());

    /// <summary>
    /// The map needs the coordinate and detail values of the selected rows. When the grid query did not read them, the same
    /// page is read again with the extra columns; the rows are only reused when that page still holds the same rows.
    /// </summary>
    private async Task<bool> EnsureMapValuesAvailableAsync()
    {
        if (!_session.HasQuery) return false;
        var widened = _session.RequestWithColumns(GetMapRequiredColumns());
        if (widened is null) return true;

        var fetched = await FetchAsync(widened, _session.CurrentCursor);
        if (fetched is null) return false;

        var (result, milliseconds) = fetched.Value;
        _session.ReplaceRequest(widened);
        if (result.Rows.Count != Rows.Count || !SameRowsByOrder(result))
        {
            Show(result, milliseconds);
            ErrorMessage = Strings.ErrSelectRows;
            return false;
        }

        _session.ShowPage(result);
        for (var index = 0; index < Rows.Count; index++)
            Rows[index].UpdateMapValues(QueryMapProjection.Values(result.Columns, result.Rows[index]));
        HasNext = result.HasMore;
        return true;
    }

    private bool TryBuildRequest(out PageRequest request, out IReadOnlyList<string> displayColumns)
    {
        ErrorMessage = "";
        var filtersComplete = Filters.All(row => row.IsComplete);
        var filters = filtersComplete
            ? Filters.Select(row => row.ToCondition()).ToList()
            : new List<FilterCondition>();
        var plan = QueryRequestPlanner.Plan(new QueryPlanInput(
            Database: database, Schema: table.Schema, Table: table.Name, Meta: _columnMeta,
            SelectedColumns: Columns.Where(c => c.IsSelected).Select(c => c.Name).ToList(),
            FiltersComplete: filtersComplete, Filters: filters, UseOr: UseOr,
            UsesPrimaryKey: UsesPrimaryKey, KeyColumns: _keyColumnNames,
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

    /// <summary>
    /// Runs one long operation with its own cancellation. Marks the screen busy while it runs and clears that state at the end,
    /// even when the operation throws.
    /// </summary>
    private async Task RunBusyAsync(Func<CancellationToken, Task> work)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _running = cts;
        IsBusy = true;
        ErrorMessage = "";
        try
        {
            await work(cts.Token);
        }
        finally
        {
            IsBusy = false;
            _running = null;
        }
    }

    /// <summary>Reads one page for the screen: busy while it runs, and every expected failure becomes a message (null result).</summary>
    private async Task<(PageResult Result, long Ms)?> FetchAsync(PageRequest request, PageCursor cursor)
    {
        (PageResult Result, long Ms)? fetched = null;
        await RunBusyAsync(async ct =>
        {
            var watch = Stopwatch.StartNew();
            try
            {
                var result = await _session.FetchAsync(request, cursor, ct);
                fetched = (result, watch.ElapsedMilliseconds);
            }
            catch (OperationCanceledException) { PageInfo = Strings.Cancelled; }
            catch (DatabaseAccessException ex) { ErrorMessage = Strings.Describe(ex); }
            catch (Exception ex) when (ex is ArgumentException or FormatException or NotSupportedException)
            {
                ErrorMessage = Strings.ErrInvalidQuery + logger.Describe(nameof(FetchAsync), ex);
            }
        });
        return fetched;
    }

    private void Show(PageResult result, long milliseconds)
    {
        var firstResult = !HasResult;
        _session.ShowPage(result);
        ResultColumns = _session.DisplayColumns;
        OnPropertyChanged(nameof(ResultColumns));
        ResultColumnsChanged?.Invoke(ResultColumns);
        Rows = ResultGridProjection.ToRows(result, _session.DisplayColumns);
        SelectedRows = [];
        HasResult = true;
        // The settings card is tall. Once the first result is there, the table gets the room; the gear button brings the card back.
        if (firstResult) IsConfigVisible = false;
        HasNext = result.HasMore;
        HasPrevious = _session.HasPrevious;
        var offset = _session.CurrentCursor.Offset;
        PageInfo = result.Rows.Count == 0
            ? Strings.NoResults
            : $"{Strings.RowsRange(offset + 1, offset + result.Rows.Count)} · {milliseconds:N0} ms";
    }
}
