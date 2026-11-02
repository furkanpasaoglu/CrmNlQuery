namespace CrmNlQuery.Application.Ask.Abstractions;

/// <summary>
/// Source of the CRM default view columns for an entity.
/// </summary>
public interface IDefaultViewSource
{
    /// <summary>
    /// Returns the logical names of the columns in the CRM default view for the entity, or an empty list if not found.
    /// </summary>
    Task<IReadOnlyList<string>> GetDefaultColumnsAsync(string entityLogicalName, CancellationToken ct = default);
}
