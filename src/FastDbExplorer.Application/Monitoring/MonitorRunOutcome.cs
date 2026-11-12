using FastDbExplorer.Domain;

namespace FastDbExplorer.Application.Monitoring;

/// <summary>Outcome of one monitor run: the check result, or the error that stopped it.</summary>
public sealed record MonitorRunOutcome(MonitorDefinition Monitor, MonitorCheckResult? Result, Exception? Error);
