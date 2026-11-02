using CrmNlQuery.Domain.Access.Entities;
using CrmNlQuery.Domain.Create.Entities;

namespace CrmNlQuery.Application.Create.Abstractions;

/// <summary>
/// Writes a record to the CRM always as the caller via impersonation, throwing a CRM exception on failure.
/// </summary>
public interface IRecordWriter
{
    /// <summary>
    /// Creates a record from a schema and payload as the caller and returns its id.
    /// </summary>
    Task<Guid> CreateAsync(CreateSchema schema, IReadOnlyDictionary<string, object?> payload, Caller caller, CancellationToken ct = default);
}
