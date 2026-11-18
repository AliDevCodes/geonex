using FastDbExplorer.Application.Diagnostics;

namespace FastDbExplorer.Infrastructure.Diagnostics;

/// <summary>
/// Appends timestamped error entries to %LocalAppData%\FastDbExplorer\logs\app.log — the same
/// logs folder <c>App.WriteCrashLog</c> uses for unhandled exceptions (there as crash.log), so every failure
/// the app records, caught or not, ends up in one place. The file is never rotated or trimmed here; it is
/// meant to be read occasionally while diagnosing a report, not watched live. Every write is best-effort: a
/// failure to log (locked file, missing permissions, full disk, ...) is swallowed, because logging must never
/// throw and must never hide the original error.
/// </summary>
public sealed class FileAppLogger : IAppLogger
{
    private static readonly object Gate = new();
    private readonly string _path;

    public FileAppLogger(string? path = null)
        => _path = path ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FastDbExplorer", "logs", "app.log");

    public void LogError(string context, Exception exception)
    {
        try
        {
            var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{context}] {exception}{Environment.NewLine}";
            lock (Gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                File.AppendAllText(_path, line);
            }
        }
        catch
        {
            // Best-effort: a logging failure must never throw or hide the original error.
        }
    }
}
