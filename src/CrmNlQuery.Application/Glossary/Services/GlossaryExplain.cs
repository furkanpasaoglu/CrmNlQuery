using CrmNlQuery.Application.Localization.Models;
using CrmNlQuery.Application.Localization.Services;
using CrmNlQuery.Domain.Glossary.Constants;
using CrmNlQuery.Domain.Glossary.Entities;
using CrmNlQuery.Domain.Metadata.Entities;

namespace CrmNlQuery.Application.Glossary.Services;

/// <summary>
/// Produces the readable text of an applied glossary entry without a model, such as conditions with labels and operator symbols.
/// </summary>
public static class GlossaryExplain
{
    /// <summary>
    /// Describes a glossary entry as readable text using entity metadata labels.
    /// </summary>
    public static string Describe(GlossaryEntry entry, EntityInfo? info)
    {
        if (entry.Kind == GlossaryKind.Alias)
        {
            var entityLabel = info?.Label ?? entry.Entity;
            var attribute = string.IsNullOrWhiteSpace(entry.Attribute) ? null : info?.Attributes.FirstOrDefault(a => string.Equals(a.LogicalName, entry.Attribute, StringComparison.OrdinalIgnoreCase))?.Label ?? entry.Attribute;
            return attribute is null ? entityLabel : entityLabel + " · " + attribute;
        }
        return string.Join(", ", entry.Conditions.Select(c => Condition(c, info)));
    }

    private static string Condition(GlossaryCondition c, EntityInfo? info)
    {
        var attribute = info?.Attributes.FirstOrDefault(a => string.Equals(a.LogicalName, c.Attribute, StringComparison.OrdinalIgnoreCase));
        var label = attribute?.Label ?? c.Attribute;
        if (c.Operator == GlossaryOperator.Null) { return label + " " + MessageCatalog.Now(Msg.GlossaryIsEmpty); }
        if (c.Operator == GlossaryOperator.NotNull) { return label + " " + MessageCatalog.Now(Msg.GlossaryIsNotEmpty); }

        var values = c.Values.Select(v => ValueLabel(attribute, v)).ToList();
        return c.Operator switch
        {
            GlossaryOperator.Eq => $"{label} = {values[0]}",
            GlossaryOperator.Ne => $"{label} ≠ {values[0]}",
            GlossaryOperator.Gt => $"{label} > {values[0]}",
            GlossaryOperator.Ge => $"{label} ≥ {values[0]}",
            GlossaryOperator.Lt => $"{label} < {values[0]}",
            GlossaryOperator.Le => $"{label} ≤ {values[0]}",
            GlossaryOperator.Like => $"{label}: {values[0]}…",
            GlossaryOperator.In => $"{label} ∈ ({string.Join(", ", values)})",
            GlossaryOperator.NotIn => $"{label} ∉ ({string.Join(", ", values)})",
            _ => $"{label} {c.Operator} {string.Join(", ", values)}",
        };
    }

    private static string ValueLabel(AttributeInfo? attribute, string value)
    {
        var text = value.Trim();
        if (attribute is not null && int.TryParse(text, out var code))
        {
            var option = attribute.Options.FirstOrDefault(o => o.Value == code);
            if (option is not null) { return option.Label; }
        }
        return text;
    }
}
