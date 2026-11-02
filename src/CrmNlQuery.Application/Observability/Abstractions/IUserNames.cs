namespace CrmNlQuery.Application.Observability.Abstractions;

/// <summary>
/// Port that resolves CRM user ids to display names for the admin panel.
/// </summary>
public interface IUserNames
{
    /// <summary>
    /// Returns display names for the given user ids; users the CRM does not know are omitted from the result.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, string>> GetNamesAsync(IEnumerable<Guid> ids, CancellationToken ct = default);
}
