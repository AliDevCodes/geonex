using FastDbExplorer.Domain;

namespace FastDbExplorer.Application.Monitoring;

/// <summary>Runs monitors on their own intervals and reports each run. Events come back on the thread that created the scheduler.</summary>
public interface IMonitorScheduler : IDisposable
{
    event Action<Guid>? Started;
    event Action<MonitorRunOutcome>? Completed;

    void Start();

    /// <summary>Runs one monitor now, even if it is paused.</summary>
    Task CheckNowAsync(Guid id);
}

/// <summary>Creates the scheduler for one monitoring session, so view models do not construct Infrastructure services.</summary>
public interface IMonitorSchedulerFactory
{
    IMonitorScheduler Create(Func<string, ConnectionSettings?> settingsFor, Func<IReadOnlyList<MonitorDefinition>> monitors);
}
