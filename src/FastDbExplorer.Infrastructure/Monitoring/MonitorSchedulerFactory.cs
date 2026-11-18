using FastDbExplorer.Application.Connections;
using FastDbExplorer.Application.Monitoring;
using FastDbExplorer.Domain;
using FastDbExplorer.Domain.Connections;
using FastDbExplorer.Domain.Monitoring;

namespace FastDbExplorer.Infrastructure.Monitoring;

/// <summary>Builds the in-process <see cref="MonitorScheduler"/> with the application's check service.</summary>
public sealed class MonitorSchedulerFactory(IMonitorCheckService checks) : IMonitorSchedulerFactory
{
    public IMonitorScheduler Create(Func<string, ConnectionSettings?> settingsFor, Func<IReadOnlyList<MonitorDefinition>> monitors) =>
        new MonitorScheduler(checks, settingsFor, monitors);
}
