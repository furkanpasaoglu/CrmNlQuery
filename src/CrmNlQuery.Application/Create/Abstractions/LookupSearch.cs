using CrmNlQuery.Domain.Ask.Entities;

namespace CrmNlQuery.Application.Create.Abstractions;

/// <summary>
/// Holds the lookup field search that resolves a record name to a record id.
/// </summary>
public delegate Task<IReadOnlyList<RecordMatch>> LookupSearch(string entity, string text, CancellationToken ct);
