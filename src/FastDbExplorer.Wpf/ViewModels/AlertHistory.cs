using System.Collections.ObjectModel;
using FastDbExplorer.Application.Monitoring;

namespace FastDbExplorer.Wpf.ViewModels;

/// <summary>
/// The alert list of one session: every alert (newest first, capped at <see cref="MaxAlerts"/>) and the part that passes
/// the monitor and time filters. The view model binds to both collections.
/// </summary>
public sealed class AlertHistory
{
    public const int MaxAlerts = 100;

    public ObservableCollection<AlertItem> All { get; } = [];

    /// <summary>The alerts that pass the filter; the list on screen shows this, <see cref="All"/> keeps everything.</summary>
    public ObservableCollection<AlertItem> Visible { get; } = [];

    public void Add(AlertItem alert)
    {
        All.Insert(0, alert);
        while (All.Count > MaxAlerts) All.RemoveAt(All.Count - 1);
    }

    /// <summary>Rebuilds <see cref="Visible"/> from <see cref="All"/> for the selected monitor and the local time range.</summary>
    public void Filter(Guid? monitorId, DateTime? from, DateTime? to)
    {
        Visible.Clear();
        foreach (var alert in All)
            if (AlertFilterRules.Matches(monitorId, alert.MonitorId, alert.At.ToLocalTime(), from, to))
                Visible.Add(alert);
    }
}
