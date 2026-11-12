using System.Collections.Concurrent;
using System.Linq;
using FastDbExplorer.Application.Abstractions;
using FastDbExplorer.Domain;

namespace FastDbExplorer.Wpf.ViewModels;

/// <summary>
/// Every database connected right now, keyed by server. Monitoring reads it from its scheduler thread, so it is thread-safe.
/// Passwords live here only while their connection is open: removing a server drops its settings.
/// </summary>
public sealed class OpenConnections
{
    private readonly ConcurrentDictionary<string, (ConnectionSettings Settings, IReadOnlyList<DatabaseInfo> Databases)> _open =
        new(StringComparer.OrdinalIgnoreCase);
    private volatile string? _current;

    /// <summary>The server connected last: the default for the explorer and for new monitors.</summary>
    public ConnectionSettings? Current => _current is not null && _open.TryGetValue(_current, out var entry) ? entry.Settings : null;

    public bool IsEmpty => _open.IsEmpty;

    public IReadOnlyList<string> Servers => _open.Keys.ToList();

    public void Add(ConnectionSettings settings, IReadOnlyList<DatabaseInfo> databases)
    {
        _open[settings.Server] = (settings, databases);
        _current = settings.Server;
    }

    public void Remove(string server)
    {
        _open.TryRemove(server, out _);
        if (string.Equals(_current, server, StringComparison.OrdinalIgnoreCase))
            _current = _open.Keys.FirstOrDefault();
    }

    /// <summary>Settings of an open server, or null when that server is not connected right now.</summary>
    public ConnectionSettings? SettingsFor(string server) =>
        _open.TryGetValue(server, out var entry) ? entry.Settings : null;

    public IReadOnlyList<DatabaseInfo> DatabasesFor(string server) =>
        _open.TryGetValue(server, out var entry) ? entry.Databases : [];
}
