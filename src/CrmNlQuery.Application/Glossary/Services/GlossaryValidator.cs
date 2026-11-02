using System.Globalization;
using CrmNlQuery.Application.Ask.Abstractions;
using CrmNlQuery.Application.Glossary.Models;
using CrmNlQuery.Domain.Glossary.Constants;
using CrmNlQuery.Domain.Glossary.Entities;
using CrmNlQuery.Domain.Metadata.Entities;

namespace CrmNlQuery.Application.Glossary.Services;

/// <summary>
/// Validates a glossary entry against the filtered schema (data policy applied), both when saving and when reading to detect broken entries.
/// </summary>
public static class GlossaryValidator
{
    public const int MaxTerms = 6;
    public const int MaxConditions = 6;
    public const int MaxValues = 20;

    /// <summary>
    /// Checks a glossary entry's tables and fields against the filtered schema.
    /// </summary>
    public static async Task<GlossaryCheck> CheckAsync(GlossaryEntry entry, IMetadataSource metadata, CancellationToken ct = default)
    {
        var terms = CheckTerms(entry);
        if (!terms.Ok) { return terms; }

        var info = await metadata.GetEntityAsync((entry.Entity ?? "").Trim().ToLowerInvariant(), ct);
        if (info is null) { return GlossaryCheck.Fail("entity_unavailable"); }

        if (entry.Kind == GlossaryKind.Alias)
        {
            return string.IsNullOrWhiteSpace(entry.Attribute) || Find(info, entry.Attribute!) is not null ? GlossaryCheck.Pass : GlossaryCheck.Fail("attribute_unavailable");
        }
        if (entry.Kind != GlossaryKind.Filter) { return GlossaryCheck.Fail("terms_invalid"); }
        if (entry.Conditions.Count == 0 || entry.Conditions.Count > MaxConditions) { return GlossaryCheck.Fail("conditions_missing"); }
        foreach (var condition in entry.Conditions)
        {
            var check = CheckCondition(info, condition);
            if (!check.Ok) { return check; }
        }
        return GlossaryCheck.Pass;
    }

    /// <summary>
    /// Checks the terms of a glossary entry for validity.
    /// </summary>
    public static GlossaryCheck CheckTerms(GlossaryEntry entry)
    {
        if (entry.Terms.Count == 0 || entry.Terms.Count > MaxTerms) { return GlossaryCheck.Fail("terms_invalid"); }
        foreach (var term in entry.Terms)
        {
            var text = (term.Text ?? "").Trim();
            if (text.Length < 2 || text.Length > 60 || GlossaryMatcher.Tokens(text).Count == 0) { return GlossaryCheck.Fail("terms_invalid"); }
        }
        return GlossaryCheck.Pass;
    }

    private static AttributeInfo? Find(EntityInfo info, string attribute)
        => info.Attributes.FirstOrDefault(a => string.Equals(a.LogicalName, attribute.Trim(), StringComparison.OrdinalIgnoreCase));

    private static GlossaryCheck CheckCondition(EntityInfo info, GlossaryCondition condition)
    {
        var attribute = Find(info, condition.Attribute ?? "");
        if (attribute is null) { return GlossaryCheck.Fail("attribute_unavailable"); }
        var op = (condition.Operator ?? "").Trim().ToLowerInvariant();
        if (!GlossaryOperator.All.Contains(op)) { return GlossaryCheck.Fail("operator_not_allowed"); }

        var kind = KindOf(attribute);
        if (kind == "unsupported") { return GlossaryCheck.Fail("type_unsupported"); }
        if (!Allowed(kind).Contains(op)) { return GlossaryCheck.Fail("operator_not_allowed"); }

        var values = condition.Values ?? new List<string>();
        if (GlossaryOperator.NeedsNoValue(op)) { return values.Count == 0 ? GlossaryCheck.Pass : GlossaryCheck.Fail("value_invalid"); }
        if (values.Count == 0 || values.Any(v => string.IsNullOrWhiteSpace(v))) { return GlossaryCheck.Fail("value_missing"); }
        if (!GlossaryOperator.NeedsManyValues(op) && values.Count != 1) { return GlossaryCheck.Fail("value_invalid"); }
        if (values.Count > MaxValues) { return GlossaryCheck.Fail("value_invalid"); }

        foreach (var value in values)
        {
            var text = value.Trim();
            switch (kind)
            {
                case "option":
                    if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var code)) { return GlossaryCheck.Fail("value_invalid"); }
                    if (attribute.Options.Count > 0 && attribute.Options.All(o => o.Value != code)) { return GlossaryCheck.Fail("option_unknown"); }
                    break;
                case "boolean":
                    if (text is not ("0" or "1" or "true" or "false")) { return GlossaryCheck.Fail("value_invalid"); }
                    break;
                case "number":
                    if (!decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out _)) { return GlossaryCheck.Fail("value_invalid"); }
                    break;
                case "date":
                    if (!DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out _)) { return GlossaryCheck.Fail("value_invalid"); }
                    break;
                case "guid":
                    if (!Guid.TryParse(text, out _)) { return GlossaryCheck.Fail("value_invalid"); }
                    break;
                default:
                    if (text.Length > 200) { return GlossaryCheck.Fail("value_invalid"); }
                    break;
            }
        }
        return GlossaryCheck.Pass;
    }

    private static string KindOf(AttributeInfo attribute) => attribute.AttributeType switch
    {
        "Picklist" or "State" or "Status" => "option",
        "Boolean" => attribute.Options.Count > 0 ? "option" : "boolean",
        "Integer" or "BigInt" or "Decimal" or "Double" or "Money" => "number",
        "String" or "Memo" => "text",
        "DateTime" => "date",
        "Lookup" or "Customer" or "Owner" or "Uniqueidentifier" => "guid",
        _ => "unsupported",
    };

    private static IReadOnlyList<string> Allowed(string kind) => kind switch
    {
        "option" or "boolean" => new[] { GlossaryOperator.Eq, GlossaryOperator.Ne, GlossaryOperator.In, GlossaryOperator.NotIn, GlossaryOperator.Null, GlossaryOperator.NotNull },
        "number" => new[] { GlossaryOperator.Eq, GlossaryOperator.Ne, GlossaryOperator.Gt, GlossaryOperator.Ge, GlossaryOperator.Lt, GlossaryOperator.Le, GlossaryOperator.In, GlossaryOperator.NotIn, GlossaryOperator.Null, GlossaryOperator.NotNull },
        "text" => new[] { GlossaryOperator.Eq, GlossaryOperator.Ne, GlossaryOperator.Like, GlossaryOperator.In, GlossaryOperator.NotIn, GlossaryOperator.Null, GlossaryOperator.NotNull },
        "date" => new[] { GlossaryOperator.Eq, GlossaryOperator.Gt, GlossaryOperator.Ge, GlossaryOperator.Lt, GlossaryOperator.Le, GlossaryOperator.Null, GlossaryOperator.NotNull },
        _ => new[] { GlossaryOperator.Eq, GlossaryOperator.Ne, GlossaryOperator.In, GlossaryOperator.Null, GlossaryOperator.NotNull },
    };
}
