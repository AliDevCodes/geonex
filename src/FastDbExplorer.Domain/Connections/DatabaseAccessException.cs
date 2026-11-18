namespace FastDbExplorer.Domain.Connections;

public enum DatabaseErrorKind
{
    LoginFailed,
    ServerUnreachable,
    Timeout,
    PagingNotSupported,
    PagingDetectionFailed,
    PagingFallbackFailed,
    Other
}

public sealed class DatabaseAccessException(DatabaseErrorKind kind, string message, Exception? inner = null)
    : Exception(message, inner)
{
    public DatabaseErrorKind Kind { get; } = kind;
}
