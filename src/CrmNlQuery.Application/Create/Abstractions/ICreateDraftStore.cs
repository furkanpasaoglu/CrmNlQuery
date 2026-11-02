using CrmNlQuery.Application.Create.Services;

namespace CrmNlQuery.Application.Create.Abstractions;

/// <summary>
/// Stores short-lived creation drafts in memory on a single server, lost when the application pool restarts.
/// </summary>
public interface ICreateDraftStore
{
    /// <summary>
    /// Saves a pending record-creation draft so the user can later confirm or cancel it.
    /// </summary>
    void Save(CreateDraft draft);

    /// <summary>
    /// Returns a draft only for its owner, or null if expired, missing or someone else's.
    /// </summary>
    CreateDraft? Get(Guid id, Guid owner);

    /// <summary>
    /// Removes the pending creation draft with the given id.
    /// </summary>
    void Remove(Guid id);
}
