namespace CrmNlQuery.Application.Create.Models;

/// <summary>
/// Describes one row of a creation preview with its name, fields, problems and status.
/// </summary>
public sealed record CreateRowView(
    string Name,
    IReadOnlyList<CreateFieldView> Fields,
    IReadOnlyList<CreateProblemView> Problems,
    IReadOnlyList<string> Duplicates,
    string Status,
    string? RecordId = null,
    string? Error = null,
    IReadOnlyList<string>? FieldNames = null);
