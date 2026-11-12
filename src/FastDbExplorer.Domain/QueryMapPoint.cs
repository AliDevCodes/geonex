namespace FastDbExplorer.Domain;

/// <summary>A row sent to the map: its name, its position, and optional detail texts shown on the card.</summary>
public sealed record QueryMapPoint(
    string Name,
    double Latitude,
    double Longitude,
    IReadOnlyDictionary<string, string>? Details = null);
