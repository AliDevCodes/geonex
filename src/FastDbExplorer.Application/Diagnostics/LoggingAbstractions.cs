namespace FastDbExplorer.Application.Diagnostics;

/// <summary>
/// Minimal diagnostic logging for failures that reach a view model's catch block — both the ones shown to the
/// user (so a bug report has more than just "ex.Message") and the ones deliberately swallowed from the UI
/// (so they are not completely invisible when diagnosing an issue). An implementation must never throw: a
/// logging failure must never mask or replace the real error it was trying to record.
/// </summary>
public interface IAppLogger
{
    /// <summary>Records a failure. <paramref name="context"/> is normally the member where it was caught
    /// (e.g. via <c>nameof(SomeMethod)</c>), so the log says where to look, not just what happened.</summary>
    void LogError(string context, Exception exception);
}
