namespace CrmNlQuery.Application.Design.Models;

/// <summary>
/// Schema-search match for the design assistant: an existing table with its score, matched terms and matched fields.
/// </summary>
public sealed record SchemaMatch(string Entity, string Label, bool IsCustom, int AttributeCount, int Score, IReadOnlyList<string> Terms, IReadOnlyList<MatchedField> Fields,
    IReadOnlyList<string> CustomFields);
