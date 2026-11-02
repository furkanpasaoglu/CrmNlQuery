namespace CrmNlQuery.Application.Ask.Planning.Validation;

/// <summary>
/// Result of plan validation: valid flag and list of errors.
/// </summary>
public sealed record ValidationResult(bool IsValid, IReadOnlyList<string> Errors, string? NormalizedFetchXml);
