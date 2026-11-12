using FastDbExplorer.Application.Monitoring;
using FastDbExplorer.Domain;

namespace FastDbExplorer.Tests;

public sealed class MonitorDefinitionFactoryTests
{
    private static MonitorDraft Draft(string name = "Monitor", string watermark = "id") => new(
        Name: name, Database: "sales", Schema: "dbo", Table: "orders", Watermark: watermark,
        Conditions: [new FilterCondition("amount", FilterOperator.GreaterThan, "10")], Logic: FilterLogic.And,
        IntervalMinutes: 60, OutputFolder: @"C:\reports", PlaySound: true,
        LatitudeColumn: null, LongitudeColumn: null, LayerIds: null, DistanceMeters: null,
        LiveMapEnabled: false, LiveMapShowAll: false, LiveMapMinutes: 0,
        LiveMapIconKey: null, LiveMapColor: null, LiveMapDetailColumns: null);

    [Fact]
    public void A_new_monitor_gets_a_new_id_and_its_target_and_server()
    {
        var created = MonitorDefinitionFactory.Create(Draft(), "sql-prod", null);

        Assert.NotEqual(Guid.Empty, created.Id);
        Assert.Equal("Monitor", created.Name);
        Assert.Equal("orders", created.Table);
        Assert.Equal("id", created.WatermarkColumn);
    }

    [Fact]
    public void Two_new_monitors_never_share_an_id()
    {
        var first = MonitorDefinitionFactory.Create(Draft(), "sql-prod", null);
        var second = MonitorDefinitionFactory.Create(Draft(), "sql-prod", null);

        Assert.NotEqual(first.Id, second.Id);
    }

    [Fact]
    public void An_edited_monitor_keeps_its_id_and_watermark_and_takes_the_draft_values()
    {
        var existing = MonitorDefinitionFactory.Create(Draft("Old"), "sql-prod", null);

        var edited = MonitorDefinitionFactory.Create(Draft("New", watermark: "other") with { IntervalMinutes = 5 }, "sql-prod", existing);

        Assert.Equal(existing.Id, edited.Id);
        Assert.Equal("id", edited.WatermarkColumn);
        Assert.Equal("New", edited.Name);
        Assert.Equal(5, edited.IntervalMinutes);
    }

    [Fact]
    public void The_live_map_lifetime_is_normalized_to_a_known_choice()
    {
        var created = MonitorDefinitionFactory.Create(Draft() with { LiveMapMinutes = 7_777 }, "sql-prod", null);

        Assert.Equal(LiveResultLifetime.Normalize(7_777), created.LiveMapMinutes);
    }
}
