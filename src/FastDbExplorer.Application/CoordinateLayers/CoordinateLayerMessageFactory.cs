using System.Text.Json.Nodes;
using FastDbExplorer.Domain;

namespace FastDbExplorer.Application.CoordinateLayers;

/// <summary>Builds the map page's layer message without depending on WebView2 or view state.</summary>
public static class CoordinateLayerMessageFactory
{
    public static JsonObject Upsert(MapLayer layer, MapLayerKind kind)
    {
        var shape = MapSourceStyles.Shape(kind);
        var monitoring = kind == MapLayerKind.Monitoring;
        var iconPath = (monitoring ? MapMarkerCatalog.Find(layer.IconKey)?.PathData : null)
                       ?? MapSourceStyles.FixedGlyph(kind) ?? MapMarkerCatalog.Find(layer.IconKey)?.PathData;
        if (iconPath is null && shape != "circle") iconPath = MapMarkerCatalog.Find("dot")?.PathData;
        var color = monitoring && MapMarkerCatalog.IsValidColor(layer.Color) ? layer.Color!.ToUpperInvariant()
                    : MapSourceStyles.FixedColor(kind) ?? MapMarkerCatalog.EffectiveColor(layer.Id, layer.Color);

        return new JsonObject
        {
            ["type"] = "coordUpsert",
            ["id"] = JsonValue.Create(layer.Id),
            ["name"] = layer.Name,
            ["visible"] = layer.Visibility,
            ["radiusMeters"] = layer.RadiusMeters,
            ["kind"] = MapSourceStyles.Key(kind),
            ["shape"] = shape,
            ["color"] = color,
            ["iconKey"] = layer.IconKey,
            ["iconPath"] = iconPath,
            ["points"] = CoordinateLayerGeoJson.Points(layer),
            ["circles"] = CoordinateLayerGeoJson.Circles(layer),
            ["bounds"] = CoordinateLayerGeoJson.Bounds(layer)
        };
    }
}
