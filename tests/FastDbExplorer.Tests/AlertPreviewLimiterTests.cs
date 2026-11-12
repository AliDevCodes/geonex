using FastDbExplorer.Application.Monitoring;
using FastDbExplorer.Domain;

namespace FastDbExplorer.Tests;

public sealed class AlertPreviewLimiterTests
{
    private static MonitorCheckResult Result(IReadOnlyList<object?[]> rows) =>
        new(Guid.NewGuid(), new DateTime(2026, 11, 12), ["Value"], rows, true, "42", "report.xlsx", false,
            [new LiveMapPoint("key", "place", 35, 51, new Dictionary<string, string>())], 3);

    [Fact]
    public void Preview_keeps_fifty_rows_without_mutating_the_report()
    {
        var rows = Enumerable.Range(0, 51).Select(index => new object?[] { index }).ToArray();
        var report = Result(rows);

        var preview = AlertPreviewLimiter.Limit(report, out var limited);

        Assert.True(limited);
        Assert.Equal(50, preview.Rows.Count);
        Assert.Equal(51, report.Rows.Count);
        Assert.NotSame(report.Rows[0], preview.Rows[0]);
        Assert.Equal(49, preview.Rows[49][0]);
        Assert.Equal(report.MonitorId, preview.MonitorId);
        Assert.Equal(report.NewWatermark, preview.NewWatermark);
        Assert.Equal(report.ReportPath, preview.ReportPath);
        Assert.Same(report.MapPoints, preview.MapPoints);
        Assert.Equal(report.MapSkippedRows, preview.MapSkippedRows);
    }

    [Fact]
    public void Preview_limits_each_text_cell_and_preserves_the_original_text()
    {
        var original = new string('a', 257);
        var report = Result([new object?[] { original, 17 }]);

        var preview = AlertPreviewLimiter.Limit(report, out var limited);

        Assert.True(limited);
        Assert.Equal(new string('a', 256) + "…", preview.Rows[0][0]);
        Assert.Equal(17, preview.Rows[0][1]);
        Assert.Equal(original, report.Rows[0][0]);
    }

    [Fact]
    public void Preview_shares_text_budget_across_cells_and_empties_it_at_zero()
    {
        var cells = Enumerable.Repeat<object?>(new string('x', 256), 65).ToArray();
        var report = Result([cells]);

        var preview = AlertPreviewLimiter.Limit(report, out var limited);

        Assert.True(limited);
        Assert.Equal(new string('x', 256), preview.Rows[0][63]);
        Assert.Equal("", preview.Rows[0][64]);
        Assert.Equal(new string('x', 256), report.Rows[0][64]);
    }

    [Fact]
    public void Preview_shares_binary_budget_without_changing_source_arrays()
    {
        var first = new byte[10_000];
        var second = new byte[10_000];
        var third = new byte[] { 1 };
        var report = Result([new object?[] { first, second, third }]);

        var preview = AlertPreviewLimiter.Limit(report, out var limited);

        Assert.True(limited);
        Assert.Same(first, preview.Rows[0][0]);
        Assert.Equal(6_384, Assert.IsType<byte[]>(preview.Rows[0][1]).Length);
        Assert.Empty(Assert.IsType<byte[]>(preview.Rows[0][2]));
        Assert.Same(second, report.Rows[0][1]);
        Assert.Equal(10_000, second.Length);
    }

    [Fact]
    public void Under_budget_preview_is_not_marked_limited()
    {
        var report = Result([new object?[] { "short", new byte[] { 1, 2 }, null, 4 }]);

        var preview = AlertPreviewLimiter.Limit(report, out var limited);

        Assert.False(limited);
        Assert.Equal("short", preview.Rows[0][0]);
        Assert.Same(report.Rows[0][1], preview.Rows[0][1]);
        Assert.Equal(4, preview.Rows[0][3]);
    }
}
