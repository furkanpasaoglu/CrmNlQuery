using CrmNlQuery.Domain.Create.Enums;

namespace CrmNlQuery.Domain.Create.Entities;

/// <summary>
/// Describes one writable field for record creation.
/// </summary>
public sealed record CreateField(
    string Name,
    string Label,
    CreateFieldType Type,
    bool Required,
    int? MaxLength,
    IReadOnlyList<CreateOption> Options,
    IReadOnlyList<LookupTarget> Targets);
