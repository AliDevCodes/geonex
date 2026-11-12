using FastDbExplorer.Application.Abstractions;
using FastDbExplorer.Application.Monitoring;
using FastDbExplorer.Domain;
using FastDbExplorer.Wpf.Services;

namespace FastDbExplorer.Wpf.ViewModels;

/// <summary>Creates the monitoring screen for a freshly connected session (the connection is only known at run time).</summary>
public sealed class MonitoringViewModelFactory(
    IDatabaseMetadataService metadata, IMonitorStore store, IMonitorSchedulerFactory schedulers,
    IFolderPickerService folders, IAlertNotifier notifier, ICoordinateLayerStore coordinateLayers)
{
    public MonitoringViewModel Create(OpenConnections connections) =>
        new(metadata, store, schedulers, folders, notifier, coordinateLayers, connections);
}
