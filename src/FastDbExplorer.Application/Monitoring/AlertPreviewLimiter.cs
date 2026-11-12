using FastDbExplorer.Domain;

namespace FastDbExplorer.Application.Monitoring;

/// <summary>Bounds the rows and cell payload retained by an alert without changing the report result.</summary>
public static class AlertPreviewLimiter
{
    private const int MaxRows = 50;
    private const int MaxCellLength = 256;
    private const int MaxTextCharacters = 16 * 1024;
    private const int MaxBinaryBytes = 16 * 1024;

    public static MonitorCheckResult Limit(MonitorCheckResult result, out bool limited)
    {
        var remainingText = MaxTextCharacters;
        var remainingBinary = MaxBinaryBytes;
        var previewLimited = result.Rows.Count > MaxRows;
        var rows = new List<object?[]>(Math.Min(result.Rows.Count, MaxRows));

        foreach (var row in result.Rows.Take(MaxRows))
        {
            var copy = new object?[row.Length];
            for (var i = 0; i < row.Length; i++)
            {
                copy[i] = row[i] switch
                {
                    string text => LimitText(text, ref remainingText, ref previewLimited),
                    byte[] bytes => LimitBytes(bytes, ref remainingBinary, ref previewLimited),
                    var value => value
                };
            }
            rows.Add(copy);
        }

        limited = previewLimited;
        return result with { Rows = rows };
    }

    private static string LimitText(string text, ref int remaining, ref bool limited)
    {
        var length = Math.Min(text.Length, Math.Min(MaxCellLength, remaining));
        remaining -= length;
        if (length == text.Length) return text;
        limited = true;
        return length == 0 ? "" : text[..length] + "…";
    }

    private static byte[] LimitBytes(byte[] bytes, ref int remaining, ref bool limited)
    {
        var length = Math.Min(bytes.Length, remaining);
        remaining -= length;
        if (length == bytes.Length) return bytes;
        limited = true;
        return bytes.AsSpan(0, length).ToArray();
    }
}
