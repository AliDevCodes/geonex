using FastDbExplorer.Application.Monitoring;

namespace FastDbExplorer.Tests;

public class AlertFilterRulesTests
{
    private static readonly Guid First = Guid.NewGuid();
    private static readonly Guid Second = Guid.NewGuid();
    private static readonly DateTime Noon = new(2026, 11, 12, 12, 0, 0);

    [Fact]
    public void With_no_filters_every_alert_passes()
    {
        Assert.True(AlertFilterRules.Matches(null, null, Noon, null, null));
        Assert.True(AlertFilterRules.Matches(null, First, Noon, null, null));
    }

    [Fact]
    public void A_selected_monitor_hides_the_alerts_of_other_monitors_and_of_unknown_monitors()
    {
        Assert.True(AlertFilterRules.Matches(First, First, Noon, null, null));
        Assert.False(AlertFilterRules.Matches(First, Second, Noon, null, null));
        Assert.False(AlertFilterRules.Matches(First, null, Noon, null, null));
    }

    [Fact]
    public void The_lower_bound_is_inclusive()
    {
        Assert.True(AlertFilterRules.Matches(null, First, Noon, Noon, null));
        Assert.False(AlertFilterRules.Matches(null, First, Noon.AddSeconds(-1), Noon, null));
    }

    [Fact]
    public void The_upper_bound_is_inclusive()
    {
        Assert.True(AlertFilterRules.Matches(null, First, Noon, null, Noon));
        Assert.False(AlertFilterRules.Matches(null, First, Noon.AddSeconds(1), null, Noon));
    }

    [Fact]
    public void Monitor_and_time_filters_must_both_pass()
    {
        Assert.False(AlertFilterRules.Matches(First, Second, Noon, Noon.AddHours(-1), Noon.AddHours(1)));
        Assert.False(AlertFilterRules.Matches(First, First, Noon, Noon.AddHours(1), null));
        Assert.True(AlertFilterRules.Matches(First, First, Noon, Noon.AddHours(-1), Noon.AddHours(1)));
    }
}
