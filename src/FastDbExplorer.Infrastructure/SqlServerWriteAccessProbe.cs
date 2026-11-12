using FastDbExplorer.Application.Abstractions;
using FastDbExplorer.Domain;

namespace FastDbExplorer.Infrastructure;

/// <summary>Exposes <see cref="SqlServerAccessProbe"/> through the application abstraction, so view models do not reference Infrastructure.</summary>
public sealed class SqlServerWriteAccessProbe : ISqlWriteAccessProbe
{
    public Task<bool?> CanWriteAsync(ConnectionSettings settings, CancellationToken ct) =>
        SqlServerAccessProbe.CanWriteAsync(settings, ct);
}
