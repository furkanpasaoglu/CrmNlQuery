namespace CrmNlQuery.Application.Reports.Abstractions;

/// <summary>
/// Provides a CRM entity's object type code for the report viewer's records context, or null when unknown.
/// </summary>
public interface IObjectTypeCodeSource
{
    /// <summary>
    /// Returns the object type code for an entity, or null when unknown.
    /// </summary>
    Task<int?> GetAsync(string entityLogicalName, CancellationToken ct = default);
}
