using FastDbExplorer.Domain;

namespace FastDbExplorer.Application.Abstractions;

/// <summary>Stores named searches on this computer. Implementations never store connection details or passwords.</summary>
public interface ISavedSearchStore
{
    Task<IReadOnlyList<SavedSearch>> LoadAsync(CancellationToken ct = default);
    Task SaveAsync(SavedSearch search, CancellationToken ct = default);
    Task RemoveAsync(Guid id, CancellationToken ct = default);
}
