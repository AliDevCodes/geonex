using FastDbExplorer.Application.Diagnostics;

namespace FastDbExplorer.Wpf.Diagnostics;

/// <summary>
/// The one repeated shape behind most view-model catch blocks in this app: either "log it, then show
/// ex.Message to the user" or "log it, but intentionally show nothing" (a best-effort operation whose failure
/// must not interrupt the user). These two helpers replace that boilerplate with one call, so every catch logs
/// instead of silently disappearing, while leaving each call site free to choose its own displayed message.
/// </summary>
public static class ViewModelErrorExtensions
{
    /// <summary>Logs the exception under <paramref name="context"/> and returns its message, for assignment to
    /// an ErrorMessage-style property: <c>ErrorMessage = _logger.Describe(nameof(DoThing), ex);</c></summary>
    public static string Describe(this IAppLogger logger, string context, Exception exception)
    {
        logger.LogError(context, exception);
        return exception.Message;
    }

    /// <summary>Logs an exception that is deliberately not surfaced to the user, so it is still visible when
    /// diagnosing a report instead of vanishing in an empty catch block.</summary>
    public static void Suppress(this IAppLogger logger, string context, Exception exception)
        => logger.LogError(context, exception);
}
