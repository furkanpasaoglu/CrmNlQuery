using CrmNlQuery.Domain.Glossary.Constants;
using CrmNlQuery.Domain.Glossary.Entities;

namespace CrmNlQuery.Application.Glossary.Services;

/// <summary>
/// Describes glossary entry conditions as short technical text for prompts and errors.
/// </summary>
public static class GlossaryDescribe
{
    /// <summary>
    /// Describes all conditions of a glossary entry.
    /// </summary>
    public static string Conditions(GlossaryEntry entry) => string.Join("; ", entry.Conditions.Select(Condition));

    /// <summary>
    /// Describes one glossary condition as attribute, operator and comma-separated trimmed values, omitting values for operators that take none.
    /// </summary>
    public static string Condition(GlossaryCondition c)
        => GlossaryOperator.NeedsNoValue(c.Operator) ? $"{c.Attribute} {c.Operator}" : $"{c.Attribute} {c.Operator} {string.Join(", ", c.Values.Select(v => v.Trim()))}";
}
