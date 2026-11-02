namespace CrmNlQuery.Application.Create.Models;

/// <summary>
/// Parsed JSON the user pasted for record creation: an optional entity name and a list of field-value maps.
/// </summary>
public sealed record CreateJson(string? Entity, IReadOnlyList<IReadOnlyDictionary<string, string>> Records);
