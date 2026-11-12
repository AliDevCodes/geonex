namespace FastDbExplorer.Application.Monitoring;

/// <summary>When a monitor raises an alert and when its records go to the live map. Pure: no UI, no scheduler.</summary>
public static class MonitoringAlertRules
{
    /// <summary>After the first failure, a failing monitor raises a new alert only every this many failures.</summary>
    public const int RepeatFailureEvery = 10;

    /// <summary>
    /// A monitor that silently stops working is worse than none: alert on the first failure of a streak and again on
    /// every <see cref="RepeatFailureEvery"/>th failure, not on every interval.
    /// </summary>
    public static bool ShouldAlertOnFailure(int consecutiveFailures) =>
        consecutiveFailures == 1 || consecutiveFailures % RepeatFailureEvery == 0;

    /// <summary>The live map is in use for a check: not the baseline run, and the monitor and its live map are both on.</summary>
    public static bool IsLiveMapActive(bool isBaseline, bool liveMapEnabled, bool monitorEnabled) =>
        !isBaseline && liveMapEnabled && monitorEnabled;

    /// <summary>Only records found by a real check, with at least one point, go to the live map.</summary>
    public static bool ShouldSendToLiveMap(bool isBaseline, bool liveMapEnabled, bool monitorEnabled, int pointCount) =>
        pointCount > 0 && IsLiveMapActive(isBaseline, liveMapEnabled, monitorEnabled);
}
