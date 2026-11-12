using FastDbExplorer.Domain;

namespace FastDbExplorer.Application.Abstractions;

/// <summary>Asks whether a login can change data. Implementations only read; they never modify anything.</summary>
public interface ISqlWriteAccessProbe
{
    /// <summary>True when the login can change data, false when it cannot, null when the answer is unknown.</summary>
    Task<bool?> CanWriteAsync(ConnectionSettings settings, CancellationToken ct);
}
