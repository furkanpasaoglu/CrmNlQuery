namespace CrmNlQuery.Domain.Glossary.Entities;

/// <summary>
/// A single glossary condition of field, operator and value or values, using option codes for option fields.
/// </summary>
public sealed record GlossaryCondition(string Attribute, string Operator, IReadOnlyList<string> Values);
