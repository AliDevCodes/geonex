using FastDbExplorer.Application.Monitoring;

namespace FastDbExplorer.Tests;

public class MonitoringAlertRulesTests
{
    [Fact]
    public void The_first_failure_of_a_streak_raises_an_alert()
    {
        Assert.True(MonitoringAlertRules.ShouldAlertOnFailure(1));
    }

    [Theory]
    [InlineData(2)]
    [InlineData(9)]
    [InlineData(11)]
    [InlineData(19)]
    public void Failures_between_the_repeat_points_raise_no_alert(int failures)
    {
        Assert.False(MonitoringAlertRules.ShouldAlertOnFailure(failures));
    }

    [Theory]
    [InlineData(10)]
    [InlineData(20)]
    [InlineData(30)]
    public void Every_tenth_failure_raises_an_alert_again(int failures)
    {
        Assert.True(MonitoringAlertRules.ShouldAlertOnFailure(failures));
    }

    [Fact]
    public void The_live_map_is_active_only_for_a_real_check_of_an_enabled_monitor_with_live_map_on()
    {
        Assert.True(MonitoringAlertRules.IsLiveMapActive(isBaseline: false, liveMapEnabled: true, monitorEnabled: true));
        Assert.False(MonitoringAlertRules.IsLiveMapActive(isBaseline: true, liveMapEnabled: true, monitorEnabled: true));
        Assert.False(MonitoringAlertRules.IsLiveMapActive(isBaseline: false, liveMapEnabled: false, monitorEnabled: true));
        Assert.False(MonitoringAlertRules.IsLiveMapActive(isBaseline: false, liveMapEnabled: true, monitorEnabled: false));
    }

    [Fact]
    public void Points_go_to_the_live_map_only_when_there_is_at_least_one_and_the_map_is_active()
    {
        Assert.True(MonitoringAlertRules.ShouldSendToLiveMap(false, true, true, pointCount: 1));
        Assert.False(MonitoringAlertRules.ShouldSendToLiveMap(false, true, true, pointCount: 0));
        Assert.False(MonitoringAlertRules.ShouldSendToLiveMap(true, true, true, pointCount: 5));
        Assert.False(MonitoringAlertRules.ShouldSendToLiveMap(false, false, true, pointCount: 5));
    }
}
