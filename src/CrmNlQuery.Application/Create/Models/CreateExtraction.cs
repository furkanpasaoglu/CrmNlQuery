namespace CrmNlQuery.Application.Create.Models;

/// <summary>
/// Carries what the model extracted for record creation: the chosen entity and the values per row.
/// </summary>
public sealed record CreateExtraction(string? Entity, IReadOnlyList<IReadOnlyDictionary<string, string>> Records);
