using FastDbExplorer.Domain;
using FastDbExplorer.Domain.Map;

namespace FastDbExplorer.Application.Map;

/// <summary>A readable, read-only map file. Tile coordinates are XYZ (y = 0 at the top), whatever the file uses inside.</summary>
public interface IMapSource : IDisposable
{
    MapSourceInfo Info { get; }
    Task<MapTile?> GetTileAsync(int zoom, int x, int y, CancellationToken ct);
}

public interface IMapSourceFactory
{
    /// <exception cref="UnsupportedMapFormatException">Known-but-unsupported or unknown format.</exception>
    /// <exception cref="MapSourceException">Damaged or unreadable file.</exception>
    Task<IMapSource> OpenAsync(string path, CancellationToken ct);
}
