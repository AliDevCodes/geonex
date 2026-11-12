using System.Text.Json;
using FastDbExplorer.Application.CoordinateLayers;
using FastDbExplorer.Domain;

namespace FastDbExplorer.Tests;

public sealed class CoordinateLayerMessageFactoryTests
{
    private static MapLayer Layer(double radius = 0, bool visible = true, bool withPoint = true)
    {
        var id = Guid.NewGuid();
        var points = withPoint ? new[] { new MapPoint(Guid.NewGuid(), "Tehran", 35.7, 51.4, id) } : [];
        return new MapLayer(id, "Places", "places.xlsx", points, visible, radius);
    }

    [Fact]
    public void Excel_payload_preserves_style_visibility_radius_and_geojson_contract()
    {
        var layer = Layer(radius: 500, visible: false);
        layer.Color = "#a1b2c3";
        layer.IconKey = "star";

        object message = CoordinateLayerMessageFactory.Upsert(layer, MapLayerKind.Excel);
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(message,
            new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        var payload = doc.RootElement;

        Assert.Equal("coordUpsert", payload.GetProperty("type").GetString());
        Assert.Equal(layer.Id.ToString(), payload.GetProperty("id").GetString());
        Assert.Equal("Places", payload.GetProperty("name").GetString());
        Assert.False(payload.GetProperty("visible").GetBoolean());
        Assert.Equal(500, payload.GetProperty("radiusMeters").GetDouble());
        Assert.Equal("excel", payload.GetProperty("kind").GetString());
        Assert.Equal("circle", payload.GetProperty("shape").GetString());
        Assert.Equal("#A1B2C3", payload.GetProperty("color").GetString());
        Assert.Equal("star", payload.GetProperty("iconKey").GetString());
        Assert.StartsWith("M12 2 l3.1 6.3", payload.GetProperty("iconPath").GetString());
        var feature = payload.GetProperty("points").GetProperty("features")[0];
        Assert.Equal(51.4, feature.GetProperty("geometry").GetProperty("coordinates")[0].GetDouble());
        Assert.Equal(35.7, feature.GetProperty("geometry").GetProperty("coordinates")[1].GetDouble());
        Assert.Single(payload.GetProperty("circles").GetProperty("features").EnumerateArray());
        Assert.Equal(4, payload.GetProperty("bounds").GetArrayLength());
    }

    [Fact]
    public void Manual_pin_uses_default_dot_glyph_without_an_icon_key()
    {
        var layer = Layer();
        layer.Color = "#0ea5e9";

        var payload = CoordinateLayerMessageFactory.Upsert(layer, MapLayerKind.Manual);

        Assert.Equal("manual", payload["kind"]!.GetValue<string>());
        Assert.Equal("pin", payload["shape"]!.GetValue<string>());
        Assert.Equal("#0EA5E9", payload["color"]!.GetValue<string>());
        Assert.Null(payload["iconKey"]);
        Assert.Equal("M12 6 a6 6 0 1 0 0 12 a6 6 0 1 0 0 -12 z", payload["iconPath"]!.GetValue<string>());
    }

    [Fact]
    public void Explorer_ignores_user_color_and_icon_but_keeps_original_icon_key()
    {
        var layer = Layer();
        layer.Color = "#123456";
        layer.IconKey = "star";

        var payload = CoordinateLayerMessageFactory.Upsert(layer, MapLayerKind.Explorer);

        Assert.Equal("explorer", payload["kind"]!.GetValue<string>());
        Assert.Equal("diamond", payload["shape"]!.GetValue<string>());
        Assert.Equal("#F59E0B", payload["color"]!.GetValue<string>());
        Assert.Equal("star", payload["iconKey"]!.GetValue<string>());
        Assert.StartsWith("M10 3 a7", payload["iconPath"]!.GetValue<string>());
    }

    [Fact]
    public void Monitoring_uses_valid_custom_style_and_falls_back_for_invalid_style()
    {
        var layer = Layer(withPoint: false);
        layer.Color = "#123abc";
        layer.IconKey = "star";

        var customized = CoordinateLayerMessageFactory.Upsert(layer, MapLayerKind.Monitoring);
        Assert.Equal("square", customized["shape"]!.GetValue<string>());
        Assert.Equal("#123ABC", customized["color"]!.GetValue<string>());
        Assert.StartsWith("M12 2 l3.1", customized["iconPath"]!.GetValue<string>());
        Assert.Null(customized["bounds"]);
        Assert.Empty((System.Text.Json.Nodes.JsonArray)customized["points"]!["features"]!);

        layer.Color = "not-a-color";
        layer.IconKey = "unknown-icon";
        var fallback = CoordinateLayerMessageFactory.Upsert(layer, MapLayerKind.Monitoring);
        Assert.Equal("#E11D48", fallback["color"]!.GetValue<string>());
        Assert.StartsWith("M12 2.5 a1.5", fallback["iconPath"]!.GetValue<string>());
    }
}
