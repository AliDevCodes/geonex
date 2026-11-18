using FastDbExplorer.Domain;
using FastDbExplorer.Domain.Filters;

namespace FastDbExplorer.Application.Filters;

/// <summary>Stores named Explorer filters on this computer. Implementations never store connection details or passwords.</summary>
public interface ISavedFilterStore
{
    Task<IReadOnlyList<SavedFilter>> LoadAsync(CancellationToken ct = default);
    Task SaveAsync(SavedFilter filter, CancellationToken ct = default);
    Task RemoveAsync(Guid id, CancellationToken ct = default);
}
