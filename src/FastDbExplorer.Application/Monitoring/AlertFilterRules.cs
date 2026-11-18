namespace FastDbExplorer.Application.Monitoring;

/// <summary>Decides whether one alert passes the monitor and time filters of the alert list. Pure: no UI.</summary>
public static class AlertFilterRules
{
    /// <summary>
    /// True when the alert belongs to the selected monitor (or no monitor is selected) and its local time lies inside the
    /// range. Both bounds are inclusive; a null bound means no limit.
    /// </summary>
    public static bool Matches(Guid? selectedMonitorId, Guid? alertMonitorId, DateTime localAt, DateTime? from, DateTime? to)
    {
        if (selectedMonitorId is { } id && alertMonitorId != id) return false;
        if (from is { } start && localAt < start) return false;
        if (to is { } end && localAt > end) return false;
        return true;
    }
}
