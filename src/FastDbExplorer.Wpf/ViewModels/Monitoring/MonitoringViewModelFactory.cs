using FastDbExplorer.Application.Connections;
using FastDbExplorer.Application.Diagnostics;
using FastDbExplorer.Application.Monitoring;
using FastDbExplorer.Application.CoordinateLayers;
using FastDbExplorer.Domain;
using FastDbExplorer.Wpf.Services;
using FastDbExplorer.Wpf.ViewModels.Shell;

namespace FastDbExplorer.Wpf.ViewModels.Monitoring;

/// <summary>Creates the monitoring screen for a freshly connected session (the connection is only known at run time).</summary>
public sealed class MonitoringViewModelFactory(
    IDatabaseMetadataService metadata, IMonitorStore store, IMonitorSchedulerFactory schedulers,
    IFolderPickerService folders, IAlertNotifier notifier, ICoordinateLayerStore coordinateLayers, IAppLogger logger)
{
    public MonitoringViewModel Create(OpenConnections connections) =>
        new(metadata, store, schedulers, folders, notifier, coordinateLayers, connections, logger);
}
