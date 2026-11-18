namespace FastDbExplorer.Wpf.ViewModels.Search;

/// <summary>A nearby-search point chosen by the user: the centre and the radius in metres.</summary>
public sealed record SearchPoint(double Latitude, double Longitude, double RadiusMeters);
