using CrmNlQuery.Domain.Create.Entities;

namespace CrmNlQuery.Application.Create.Abstractions;

/// <summary>
/// Provides the writable field description of a table, read from CRM metadata.
/// </summary>
public interface ICreateSchemaSource
{
    /// <summary>
    /// Returns the writable schema of a table, or null if the table does not exist or cannot be created.
    /// </summary>
    Task<CreateSchema?> GetAsync(string entity, CancellationToken ct = default);
}
