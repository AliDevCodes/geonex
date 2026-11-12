using FastDbExplorer.Application.Monitoring;
using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FastDbExplorer.Application.Abstractions;
using FastDbExplorer.Application.Map;
using FastDbExplorer.Domain;
using FastDbExplorer.Wpf.Localization;
using FastDbExplorer.Wpf.Services;

namespace FastDbExplorer.Wpf.ViewModels;

public sealed record MonitorChoice(Guid? Id, string Name)
{
    public override string ToString() => Name;
}

public enum AlertKind { Match, CheckFailed }

public sealed record AlertItem(
    string MonitorName,
    string TimeText,
    string Summary,
    string? ReportPath,
    MonitorCheckResult? Result = null,
    MonitorDefinition? Definition = null,
    int? TotalRowCount = null,
    bool PreviewLimited = false,
    DateTime? AtUtc = null,
    AlertKind Kind = AlertKind.Match)
{
    /// <summary>When the check that raised this alert ran (UTC). Used by the date and time filter.</summary>
    public DateTime At => AtUtc ?? Result?.CheckedAtUtc ?? DateTime.UtcNow;

    /// <summary>The monitor that raised it (null when unknown). Used by the monitor filter.</summary>
    public Guid? MonitorId => Definition?.Id;

    public string DisplayName => Definition?.Name ?? MonitorName;
    public bool HasReport => ReportPath is not null;
    public bool HasResult => Result is { Rows.Count: > 0 };

    public void Deconstruct(out string monitorName, out string timeText, out string summary, out string? reportPath)
    {
        monitorName = MonitorName;
        timeText = TimeText;
        summary = Summary;
        reportPath = ReportPath;
    }
}

/// <summary>Display text is preformatted, while Values keeps the original database types for coordinates and map details.</summary>
public sealed class MonitoringResultRow(object?[] values)
{
    public IReadOnlyList<object?> Values { get; } = values;
    public IReadOnlyList<string> Cells { get; } = values.Select(FormatValue).ToArray();

    private static string FormatValue(object? value) => value switch
    {
        null or DBNull => MonitoringStrings.NullValue,
        DateTime date => date.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture),
        DateTimeOffset date => date.ToString("yyyy-MM-dd HH:mm:ss.fff zzz", CultureInfo.InvariantCulture),
        DateOnly date => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        TimeOnly time => time.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture),
        byte[] bytes => $"({bytes.Length:N0} bytes)",
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture) ?? "",
        _ => value.ToString() ?? ""
    };
}

public sealed partial class MonitoringMapDetailColumnItem(
    string name, bool isSelected, Action<MonitoringMapDetailColumnItem, bool> onChanged) : ObservableObject
{
    public string Name { get; } = name;

    [ObservableProperty] private bool _isSelected = isSelected;

    partial void OnIsSelectedChanged(bool value) => onChanged(this, value);
}

/// <summary>
/// Monitor list + alert history + the in-process scheduler for one connected session. It lives as long as the
/// connection (the password stays in memory only) and keeps checking while the user works in other screens.
/// </summary>
public sealed partial class MonitoringViewModel : ObservableObject, IDisposable
{
    private const int MaxAlerts = 100;
    private const int RepeatFailureAlertEvery = 10;
    private const int MaxMapDetailColumns = MapPointFactory.MaxDetailColumns;

    private readonly IDatabaseMetadataService _metadata;
    private readonly IMonitorStore _store;
    private readonly IMonitorSchedulerFactory _schedulers;
    private readonly ICoordinateLayerStore _coordinateLayers;
    private readonly IFolderPickerService _folders;
    private readonly IAlertNotifier _notifier;
    private readonly OpenConnections _connections;
    private readonly List<MonitorDefinition> _otherServers = []; // monitors of other servers are kept untouched in the file
    private readonly SemaphoreSlim _saveGate = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private volatile IReadOnlyList<MonitorDefinition> _snapshot = []; // what the scheduler thread reads
    private IMonitorScheduler? _scheduler;
    private IReadOnlyList<MonitoringResultRow> _selectedResultRows = [];

    public MonitoringViewModel(
        IDatabaseMetadataService metadata, IMonitorStore store, IMonitorSchedulerFactory schedulers, IFolderPickerService folders,
        IAlertNotifier notifier, ICoordinateLayerStore coordinateLayers, OpenConnections connections)
    {
        _metadata = metadata;
        _store = store;
        _schedulers = schedulers;
        _coordinateLayers = coordinateLayers;
        _folders = folders;
        _notifier = notifier;
        _connections = connections;
        Monitors.CollectionChanged += (_, _) => { OnPropertyChanged(nameof(HasNoMonitors)); RebuildMonitorChoices(); };
        Alerts.CollectionChanged += (_, _) => { OnPropertyChanged(nameof(HasNoAlerts)); RefreshVisibleAlerts(); };
    }

    public event Action? BackRequested;
    public event Action<string, IReadOnlyList<QueryMapPoint>>? ShowSelectedRowsOnMapRequested;

    /// <summary>Matching records of a live-map monitor (monitor id, monitor name, layer style, points) for the map.</summary>
    public event Action<Guid, string, LiveMapLayerStyle, IReadOnlyList<LiveMapPoint>>? LiveMapPointsRequested;

    /// <summary>The monitor's live layer must leave the map (monitor deleted, disabled, or live map turned off).</summary>
    public event Action<Guid>? LiveMapRemoveRequested;

    public ObservableCollection<MonitorItemViewModel> Monitors { get; } = [];
    public ObservableCollection<AlertItem> Alerts { get; } = [];
    public ObservableCollection<MonitoringMapDetailColumnItem> MapDetailColumns { get; } = [];
    public bool HasNoMonitors => Monitors.Count == 0;
    public bool HasNoAlerts => Alerts.Count == 0;

    // ---- Output filter (left column): which monitor's outputs, and between which moments --------------------------

    private static readonly MonitorChoice AllMonitorsChoice = new(null, "همه مانیتورها");

    public ObservableCollection<MonitorChoice> MonitorFilterChoices { get; } = [AllMonitorsChoice];

    /// <summary>The alerts that pass the filter; the list on screen shows this, <see cref="Alerts"/> keeps everything.</summary>
    public ObservableCollection<AlertItem> VisibleAlerts { get; } = [];

    [ObservableProperty] private MonitorChoice? _selectedMonitorFilter = AllMonitorsChoice;
    /// <summary>Range bounds as the Persian date-time box stores them (database format). Empty = no bound.</summary>
    [ObservableProperty] private string _alertFrom = "";
    [ObservableProperty] private string _alertTo = "";
    [ObservableProperty] private string _alertFilterSummary = "";

    public bool HasNoVisibleAlerts => VisibleAlerts.Count == 0;

    /// <summary>Text for the empty list: nothing recorded yet, or nothing matches the filter.</summary>
    public string AlertEmptyText => Alerts.Count == 0 ? MonitoringStrings.NoAlerts : "با این فیلتر خروجی‌ای نیست.";

    partial void OnSelectedMonitorFilterChanged(MonitorChoice? value) => RefreshVisibleAlerts();
    partial void OnAlertFromChanged(string value) => RefreshVisibleAlerts();
    partial void OnAlertToChanged(string value) => RefreshVisibleAlerts();

    /// <summary>Quick ranges: 0 = any time, 1 = today, 7 = the last seven days, 30 = the last thirty days (from midnight).</summary>
    [RelayCommand]
    private void SetAlertRange(string? days)
    {
        var count = int.TryParse(days, out var parsed) ? parsed : 0;
        if (count <= 0)
        {
            AlertFrom = ""; AlertTo = "";
            return;
        }
        AlertFrom = PersianDateTimeCodec.ToDatabaseValue(DateTime.Now.Date.AddDays(-(count - 1)));
        AlertTo = "";
    }

    [RelayCommand]
    private void ClearAlertFilters()
    {
        SelectedMonitorFilter = AllMonitorsChoice;
        AlertFrom = "";
        AlertTo = "";
    }

    private void RebuildMonitorChoices()
    {
        var selectedId = SelectedMonitorFilter?.Id;
        MonitorFilterChoices.Clear();
        MonitorFilterChoices.Add(AllMonitorsChoice);
        foreach (var monitor in Monitors) MonitorFilterChoices.Add(new MonitorChoice(monitor.Definition.Id, monitor.Name));
        // A deleted monitor leaves the filter: the list falls back to all monitors.
        SelectedMonitorFilter = MonitorFilterChoices.FirstOrDefault(c => c.Id == selectedId) ?? AllMonitorsChoice;
    }

    private void RefreshVisibleAlerts()
    {
        var from = ParseBound(AlertFrom);
        var to = ParseBound(AlertTo);

        var monitorId = SelectedMonitorFilter?.Id;
        VisibleAlerts.Clear();
        foreach (var alert in Alerts)
        {
            if (monitorId is { } id && alert.MonitorId != id) continue;
            var local = alert.At.ToLocalTime();
            if (from is { } start && local < start) continue;
            if (to is { } end && local > end) continue;
            VisibleAlerts.Add(alert);
        }

        AlertFilterSummary = VisibleAlerts.Count == Alerts.Count
            ? $"{Alerts.Count} خروجی"
            : $"{VisibleAlerts.Count} از {Alerts.Count} خروجی";
        OnPropertyChanged(nameof(HasNoVisibleAlerts));
        OnPropertyChanged(nameof(AlertEmptyText));
    }

    /// <summary>Reads a bound stored by the Persian date-time box. Empty or unreadable means no bound.</summary>
    private static DateTime? ParseBound(string value) =>
        PersianDateTimeCodec.TryParseDatabaseValue(value, out var moment) ? moment : null;
    public int MapDetailColumnCount => MapDetailColumns.Count(column => column.IsSelected);
    public bool IsMapSendEnabled => HasValidSelectedCoordinates();

    [ObservableProperty] private MonitorEditorViewModel? _editor;
    [ObservableProperty] private string _errorMessage = "";
    [ObservableProperty] private string _previewTitle = "";
    [ObservableProperty] private string _previewTarget = "";
    [ObservableProperty] private string _previewRowCountText = "";
    [ObservableProperty] private string _previewStatus = "";
    [ObservableProperty] private IReadOnlyList<string> _previewColumns = [];
    [ObservableProperty] private IReadOnlyList<MonitoringResultRow> _previewRows = [];
    [ObservableProperty] private string? _latitudeColumn;
    [ObservableProperty] private string? _longitudeColumn;
    [ObservableProperty] private bool _isResultPreviewVisible;

    partial void OnLatitudeColumnChanged(string? value) => RefreshMapCommandState();
    partial void OnLongitudeColumnChanged(string? value) => RefreshMapCommandState();

    public async Task InitializeAsync()
    {
        try
        {
            var all = await _store.LoadAsync(_lifetime.Token);
            foreach (var monitor in all)
            {
                Monitors.Add(new MonitorItemViewModel(monitor)); // every monitor is listed; one whose server is not connected waits
            }
            RefreshSnapshot();

            // Created here (UI thread) so its events come back on the UI thread.
            _scheduler = _schedulers.Create(_connections.SettingsFor, () => _snapshot);
            _scheduler.Started += OnStarted;
            _scheduler.Completed += OnCompleted;
            _scheduler.Start();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { ErrorMessage = ex.Message; } // UI boundary
    }

    [RelayCommand]
    private void Back() => BackRequested?.Invoke();

    [RelayCommand]
    private void NewMonitor() => OpenEditor(null);

    [RelayCommand]
    private void EditMonitor(MonitorItemViewModel item) => OpenEditor(item.Definition);

    [RelayCommand]
    private void DeleteMonitor(MonitorItemViewModel item)
    {
        Monitors.Remove(item);
        LiveMapRemoveRequested?.Invoke(item.Definition.Id);
        RefreshSnapshot();
        _ = SaveAsync();
    }

    [RelayCommand]
    private void ToggleEnabled(MonitorItemViewModel item)
    {
        item.Definition = item.Definition with { Enabled = !item.Definition.Enabled };
        if (!item.Definition.Enabled) LiveMapRemoveRequested?.Invoke(item.Definition.Id);
        RefreshSnapshot();
        _ = SaveAsync();
    }

    [RelayCommand]
    private Task CheckNowAsync(MonitorItemViewModel item) =>
        _scheduler?.CheckNowAsync(item.Definition.Id) ?? Task.CompletedTask;

    [RelayCommand]
    private void OpenReport(AlertItem alert)
    {
        if (alert.ReportPath is null) return;
        var error = ShellOpen.Open(alert.ReportPath);
        ErrorMessage = error is null ? "" : MonitoringStrings.ErrOpenFile + error;
    }

    [RelayCommand]
    private void ShowReportInFolder(AlertItem alert)
    {
        if (alert.ReportPath is null) return;
        var error = ShellOpen.Reveal(alert.ReportPath);
        ErrorMessage = error is null ? "" : MonitoringStrings.ErrOpenFile + error;
    }

    [RelayCommand]
    private void ShowResults(AlertItem alert)
    {
        if (alert.Result is not { Rows.Count: > 0 } result) return;

        _selectedResultRows = [];
        PreviewTitle = alert.MonitorName;
        PreviewTarget = alert.Definition is { } definition
            ? $"{definition.Database}.{definition.Schema}.{definition.Table}"
            : alert.MonitorName;
        PreviewColumns = result.Columns.ToList();
        PreviewRowCountText = MonitoringStrings.PreviewRowCount(alert.TotalRowCount ?? result.Rows.Count);
        PreviewStatus = result.Truncated
            ? MonitoringStrings.PreviewTruncated
            : alert.PreviewLimited ? MonitoringStrings.PreviewLimited : "";
        PreviewRows = result.Rows.Select(row => new MonitoringResultRow(row)).ToList();

        LatitudeColumn = FindCoordinateColumn(PreviewColumns, alert.Definition?.LatitudeColumn, isLatitude: true);
        LongitudeColumn = FindCoordinateColumn(PreviewColumns, alert.Definition?.LongitudeColumn, isLatitude: false);
        ConfigureMapDetailColumns();
        RefreshMapCommandState();
        IsResultPreviewVisible = true;
    }

    [RelayCommand]
    private void CloseResults()
    {
        IsResultPreviewVisible = false;
        _selectedResultRows = [];
        PreviewRows = [];
        PreviewColumns = [];
        MapDetailColumns.Clear();
        OnPropertyChanged(nameof(MapDetailColumnCount));
        PreviewTitle = "";
        PreviewTarget = "";
        PreviewRowCountText = "";
        PreviewStatus = "";
        LatitudeColumn = null;
        LongitudeColumn = null;
        RefreshMapCommandState();
    }

    public void SetSelectedResultRows(IReadOnlyList<MonitoringResultRow> rows)
    {
        _selectedResultRows = rows;
        RefreshMapCommandState();
    }

    [RelayCommand(CanExecute = nameof(CanSendSelectedRowsToMap))]
    private void SendSelectedRowsToMap()
    {
        if (!TryGetCoordinateIndexes(out var latitudeIndex, out var longitudeIndex)) return;

        var points = new List<QueryMapPoint>(_selectedResultRows.Count);
        var skipped = 0;
        foreach (var (row, rowIndex) in _selectedResultRows.Select((row, index) => (row, index)))
        {
            if (!TryGetCoordinates(row, latitudeIndex, longitudeIndex, out var latitude, out var longitude))
            {
                skipped++;
                continue;
            }

            var name = GetPointName(row, latitudeIndex, longitudeIndex);
            points.Add(MapPointFactory.Create(name, rowIndex + 1, latitude, longitude, BuildMapDetails(row)));
        }

        if (points.Count == 0)
        {
            PreviewStatus = MonitoringStrings.NoValidSelectedCoordinates;
            return;
        }

        PreviewStatus = skipped > 0
            ? MonitoringStrings.RowsSkippedForMap(skipped, points.Count)
            : MonitoringStrings.RowsSentToMap(points.Count);
        ShowSelectedRowsOnMapRequested?.Invoke(PreviewTarget, points);
    }

    private void OpenEditor(MonitorDefinition? existing)
    {
        var settings = existing is null ? _connections.Current : _connections.SettingsFor(existing.Server);
        if (settings is null)
        {
            ErrorMessage = "اتصال این دیتابیس باز نیست. ابتدا به آن وصل شوید، سپس ویرایش کنید.";
            return;
        }
        if (IsResultPreviewVisible) CloseResults();
        Editor?.Dispose();
        var editor = new MonitorEditorViewModel(_metadata, settings, _connections.DatabasesFor(settings.Server), _folders, existing, _coordinateLayers);
        editor.Saved += OnEditorSaved;
        editor.Cancelled += CloseEditor;
        Editor = editor;
        _ = editor.InitializeAsync();
    }

    private void CloseEditor()
    {
        Editor?.Dispose();
        Editor = null;
    }

    private void OnEditorSaved(MonitorDefinition definition)
    {
        var item = Monitors.FirstOrDefault(m => m.Definition.Id == definition.Id);
        if (item is null)
        {
            Monitors.Add(new MonitorItemViewModel(definition));
        }
        else
        {
            // A check may have finished while the editor was open: keep the newest check state, take only the user's edits.
            item.Definition = definition with
            {
                LastWatermark = item.Definition.LastWatermark,
                LastCheckedUtc = item.Definition.LastCheckedUtc
            };
        }

        if (!definition.LiveMapEnabled || !definition.Enabled) LiveMapRemoveRequested?.Invoke(definition.Id);
        RefreshSnapshot();
        CloseEditor();
        _ = SaveAsync();
    }

    private void OnStarted(Guid id)
    {
        var item = Monitors.FirstOrDefault(m => m.Definition.Id == id);
        if (item is null) return;
        item.IsChecking = true;
        item.HasProblem = false;
        item.StatusText = MonitoringStrings.Checking;
    }

    private void OnCompleted(MonitorRunOutcome outcome)
    {
        var item = Monitors.FirstOrDefault(m => m.Definition.Id == outcome.Monitor.Id);
        if (item is null) return; // deleted while it was running
        item.IsChecking = false;

        if (outcome.Error is not null)
        {
            OnCheckFailed(item, outcome.Error);
            return;
        }

        item.ConsecutiveFailures = 0;
        var result = outcome.Result!;
        // Copy only the check state: the user may have edited the monitor while the check was running.
        item.Definition = item.Definition with
        {
            LastWatermark = outcome.Monitor.LastWatermark,
            LastCheckedUtc = outcome.Monitor.LastCheckedUtc
        };
        RefreshSnapshot();

        if (result.IsBaseline)
        {
            item.StatusText = MonitoringStrings.BaselineSet;
        }
        else if (result.Rows.Count == 0)
        {
            item.StatusText = MonitoringStrings.NoNewMatches;
        }
        else
        {
            item.StatusText = MonitoringStrings.Matched(result.Rows.Count);
            // The live map part of the notification: exactly the new points sent to the map for this check.
            var liveOn = !result.IsBaseline && item.Definition.LiveMapEnabled && item.Definition.Enabled;
            var summary = MonitoringStrings.AlertSummary(result.Rows.Count, result.Truncated,
                liveOn ? (int?)(result.MapPoints?.Count ?? 0) : null, result.MapSkippedRows);
            var previewResult = AlertPreviewLimiter.Limit(result, out var previewLimited);
            AddAlert(new AlertItem(item.Name, MonitoringStrings.TimeText(result.CheckedAtUtc), summary,
                result.ReportPath, previewResult, outcome.Monitor, result.Rows.Count, previewLimited, AtUtc: result.CheckedAtUtc));
            _notifier.Notify(MonitoringStrings.ToastTitle(item.Name), summary, result.ReportPath, item.Definition.PlaySound);
        }

        // Only records found by a real check go to the map; the baseline run just records the starting point.
        // A failure while drawing must not stay silent (the alert above has already been raised): show it on the monitor.
        if (!result.IsBaseline && result.MapPoints is { Count: > 0 } livePoints
            && item.Definition.LiveMapEnabled && item.Definition.Enabled)
        {
            try
            {
                LiveMapPointsRequested?.Invoke(item.Definition.Id, item.Name,
                    new LiveMapLayerStyle(item.Definition.LiveMapMinutes, item.Definition.LiveMapIconKey, item.Definition.LiveMapColor),
                    livePoints);
            }
            catch (Exception ex)
            {
                item.StatusText = "نمایش روی نقشه ممکن نشد: " + ex.Message;
                item.HasProblem = true;
            }
        }

        _ = SaveAsync();
    }

    /// <summary>
    /// A monitor that silently stops working is worse than none: tell the user on the first failure of a streak
    /// (server down, column dropped, folder not writable ...) and again every 10th failure, not on every interval.
    /// </summary>
    private void OnCheckFailed(MonitorItemViewModel item, Exception error)
    {
        item.HasProblem = true;
        item.ConsecutiveFailures++;
        var message = MonitoringStrings.CheckFailed(Describe(error));
        item.StatusText = message;

        if (item.ConsecutiveFailures == 1 || item.ConsecutiveFailures % RepeatFailureAlertEvery == 0)
        {
            AddAlert(new AlertItem(MonitoringStrings.ErrorPrefix + item.Name, MonitoringStrings.TimeText(DateTime.UtcNow), message, null,
                Definition: item.Definition, AtUtc: DateTime.UtcNow, Kind: AlertKind.CheckFailed));
            _notifier.Notify(MonitoringStrings.ToastErrorTitle(item.Name), message, null, item.Definition.PlaySound);
        }
    }

    private void AddAlert(AlertItem alert)
    {
        Alerts.Insert(0, alert);
        while (Alerts.Count > MaxAlerts) Alerts.RemoveAt(Alerts.Count - 1);
    }

    private void ConfigureMapDetailColumns()
    {
        MapDetailColumns.Clear();
        var coordinateNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (LatitudeColumn is not null) coordinateNames.Add(LatitudeColumn);
        if (LongitudeColumn is not null) coordinateNames.Add(LongitudeColumn);

        var columns = PreviewColumns.Select((name, index) => (name, index)).ToList();
        var defaults = columns.Where(item => !coordinateNames.Contains(item.name))
            .OrderByDescending(item => DetailUsefulness(item.name))
            .ThenBy(item => item.index).Take(2).Select(item => item.name)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var column in columns)
            MapDetailColumns.Add(new MonitoringMapDetailColumnItem(
                column.name, defaults.Contains(column.name), OnMapDetailColumnChanged));
        OnPropertyChanged(nameof(MapDetailColumnCount));
    }

    private void OnMapDetailColumnChanged(MonitoringMapDetailColumnItem item, bool selected)
    {
        if (selected && MapDetailColumnCount > MaxMapDetailColumns)
        {
            item.IsSelected = false;
            PreviewStatus = MonitoringStrings.MaxMapDetailColumnsReached;
            return;
        }
        OnPropertyChanged(nameof(MapDetailColumnCount));
    }

    private void RefreshMapCommandState()
    {
        OnPropertyChanged(nameof(IsMapSendEnabled));
        SendSelectedRowsToMapCommand.NotifyCanExecuteChanged();
    }

    private bool CanSendSelectedRowsToMap() => IsMapSendEnabled;

    private bool HasValidSelectedCoordinates()
    {
        if (!TryGetCoordinateIndexes(out var latitudeIndex, out var longitudeIndex)) return false;
        return _selectedResultRows.Any(row => TryGetCoordinates(row, latitudeIndex, longitudeIndex, out _, out _));
    }

    private bool TryGetCoordinateIndexes(out int latitudeIndex, out int longitudeIndex)
    {
        latitudeIndex = FindColumnIndex(LatitudeColumn);
        longitudeIndex = FindColumnIndex(LongitudeColumn);
        return latitudeIndex >= 0 && longitudeIndex >= 0 && latitudeIndex != longitudeIndex;
    }

    private int FindColumnIndex(string? name)
    {
        if (name is null) return -1;
        for (var index = 0; index < PreviewColumns.Count; index++)
            if (string.Equals(PreviewColumns[index], name, StringComparison.Ordinal)) return index;
        return -1;
    }

    private static string? FindCoordinateColumn(IReadOnlyList<string> columns, string? preferred, bool isLatitude)
    {
        if (preferred is not null)
            foreach (var column in columns)
                if (string.Equals(column, preferred, StringComparison.OrdinalIgnoreCase)) return column;

        var ranked = columns.Select((name, index) => (name, index, score: CoordinateNameScore(name, isLatitude)))
            .Where(item => item.score > 0).OrderByDescending(item => item.score).ThenBy(item => item.index).FirstOrDefault();
        return ranked.score > 0 ? ranked.name : null;
    }

    private static int CoordinateNameScore(string name, bool isLatitude)
    {
        var normalized = new string(name.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
        if (isLatitude)
        {
            if (normalized is "lat" or "latitude" or "y") return 5;
            if (normalized.Contains("latitude", StringComparison.Ordinal)) return 4;
            if (normalized.StartsWith("lat", StringComparison.Ordinal) || normalized.EndsWith("lat", StringComparison.Ordinal)) return 2;
        }
        else
        {
            if (normalized is "lon" or "lng" or "long" or "longitude" or "x") return 5;
            if (normalized.Contains("longitude", StringComparison.Ordinal)) return 4;
            if (normalized.StartsWith("lon", StringComparison.Ordinal) || normalized.EndsWith("lon", StringComparison.Ordinal)
                || normalized.StartsWith("lng", StringComparison.Ordinal) || normalized.EndsWith("lng", StringComparison.Ordinal)) return 2;
        }
        return 0;
    }

    private static int DetailUsefulness(string name)
    {
        var normalized = new string(name.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
        if (normalized.Contains("name", StringComparison.Ordinal) || normalized.Contains("title", StringComparison.Ordinal)
            || normalized.Contains("address", StringComparison.Ordinal) || normalized.Contains("nearestpoint", StringComparison.Ordinal)) return 4;
        if (normalized.Contains("distance", StringComparison.Ordinal) || normalized.Contains("description", StringComparison.Ordinal)
            || normalized.Contains("type", StringComparison.Ordinal)) return 3;
        if (normalized.Contains("layer", StringComparison.Ordinal) || normalized.Contains("category", StringComparison.Ordinal)
            || normalized.Contains("status", StringComparison.Ordinal)) return 2;
        return 0;
    }

    private static bool TryGetCoordinates(
        MonitoringResultRow row, int latitudeIndex, int longitudeIndex, out double latitude, out double longitude)
    {
        latitude = longitude = 0;
        if (latitudeIndex >= row.Values.Count || longitudeIndex >= row.Values.Count) return false;
        return MapPointFactory.TryReadCoordinates(row.Values[latitudeIndex], row.Values[longitudeIndex], out latitude, out longitude);
    }

    /// <summary>The first text cell that is not a coordinate. Null when the row has none; the factory then names it by row number.</summary>
    private string? GetPointName(MonitoringResultRow row, int latitudeIndex, int longitudeIndex)
    {
        for (var index = 0; index < row.Values.Count; index++)
        {
            if (index == latitudeIndex || index == longitudeIndex || row.Values[index] is null or DBNull) continue;
            var value = row.Cells[index];
            if (!string.IsNullOrWhiteSpace(value)) return value;
        }
        return null;
    }

    private IReadOnlyDictionary<string, string>? BuildMapDetails(MonitoringResultRow row) =>
        MapPointFactory.BuildDetails(
            MapDetailColumns.Where(item => item.IsSelected).Select(item => item.Name),
            column =>
            {
                var index = FindColumnIndex(column);
                return index >= 0 && index < row.Cells.Count ? row.Cells[index] : null;
            });

    private void RefreshSnapshot() => _snapshot = Monitors.Select(m => m.Definition).ToList();

    private async Task SaveAsync()
    {
        await _saveGate.WaitAsync();
        try
        {
            var all = _otherServers.Concat(Monitors.Select(m => m.Definition)).ToList();
            await _store.SaveAsync(all);
        }
        catch (Exception ex) { ErrorMessage = ex.Message; } // UI boundary
        finally { _saveGate.Release(); }
    }

    private static string Describe(Exception ex) => ex switch
    {
        DatabaseAccessException db => Strings.Describe(db),
        ArgumentException or FormatException or NotSupportedException => Strings.ErrInvalidQuery + ex.Message,
        _ => ex.Message
    };

    public void Dispose()
    {
        _lifetime.Cancel();
        _scheduler?.Dispose();
        Editor?.Dispose();
    }
}
