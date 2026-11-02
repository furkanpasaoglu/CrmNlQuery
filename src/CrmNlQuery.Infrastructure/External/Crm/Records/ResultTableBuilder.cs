using System.Text.Json;
using System.Xml.Linq;
using CrmNlQuery.Application.Ask.Planning.Validation;
using CrmNlQuery.Domain.Ask.Entities;

namespace CrmNlQuery.Infrastructure.External.Crm.Records;

/// <summary>
/// Builds a result table from the CRM value array and the executed FetchXML.
/// </summary>
public static class ResultTableBuilder
{
    private const string FormattedSuffix = "@OData.Community.Display.V1.FormattedValue";

    private sealed record ColumnSpec(string Key, string? AliasOnlyKey);

    private static List<ColumnSpec> Specs(string fetchXml)
    {
        var entity = XDocument.Parse(fetchXml).Root!.Element("entity")!;
        var specs = new List<ColumnSpec>();
        AddSpecs(entity, null, specs);
        return specs;
    }

    private static void AddSpecs(XElement scope, string? prefix, List<ColumnSpec> specs)
    {
        foreach (var child in scope.Elements())
        {
            if (child.Name.LocalName == "attribute")
            {
                var alias = child.Attribute("alias")?.Value;
                var name = alias ?? child.Attribute("name")!.Value;
                var key = prefix is null ? name : prefix + "." + name;
                var aliasOnly = prefix is not null && alias is not null ? alias : null;
                specs.Add(new ColumnSpec(key, aliasOnly));
            }
            else if (child.Name.LocalName == "link-entity")
            {
                AddSpecs(child, child.Attribute("alias")!.Value, specs);
            }
        }
    }

    /// <summary>
    /// Builds the result table with columns, rows, and hidden row ids from the Web API value array.
    /// </summary>
    public static ResultTable Build(JsonElement valueArray, string fetchXml)
    {
        var all = Specs(fetchXml);
        static bool IsRowId(ColumnSpec s) => s.Key == DistinctRewriter.RowIdAlias || s.Key.EndsWith("." + DistinctRewriter.RowIdAlias, StringComparison.Ordinal);
        var rowIdSpec = all.FirstOrDefault(IsRowId);
        var specs = all.Where(s => !IsRowId(s)).ToList();
        var rows = new List<IReadOnlyList<string>>();
        var rowIds = rowIdSpec is null ? null : new List<string>();
        var isAggregate = string.Equals((string?)XDocument.Parse(fetchXml).Root!.Attribute("aggregate"), "true", StringComparison.OrdinalIgnoreCase);
        var rawRows = isAggregate ? new List<IReadOnlyList<string>>() : null;
        foreach (var record in valueArray.EnumerateArray())
        {
            rows.Add(specs.Select(s => CellText(record, s)).ToList());
            rawRows?.Add(specs.Select(s => RawText(record, s)).ToList());
            rowIds?.Add(RawText(record, rowIdSpec!));
        }
        return new ResultTable(specs.Select(s => s.Key).ToList(), rows, rowIds) { Raw = rawRows };
    }

    private static string RawText(JsonElement record, ColumnSpec spec)
    {
        foreach (var property in CandidateProperties(spec))
        {
            if (record.TryGetProperty(property, out var value))
            {
                return ValueText(value);
            }
        }
        return "";
    }

    private static string CellText(JsonElement record, ColumnSpec spec)
    {
        foreach (var property in CandidateProperties(spec))
        {
            if (record.TryGetProperty(property + FormattedSuffix, out var formatted) && formatted.ValueKind == JsonValueKind.String)
            {
                return formatted.GetString() ?? "";
            }
            if (record.TryGetProperty(property, out var value))
            {
                return ValueText(value);
            }
        }
        return "";
    }

    private static IEnumerable<string> CandidateProperties(ColumnSpec spec)
    {
        yield return spec.Key;
        if (!spec.Key.Contains('.'))
        {
            yield return "_" + spec.Key + "_value";
        }
        if (spec.AliasOnlyKey is not null)
        {
            yield return spec.AliasOnlyKey;
        }
    }

    private static string ValueText(JsonElement value)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Null:
            case JsonValueKind.Undefined:
                return "";
            case JsonValueKind.String:
                return value.GetString() ?? "";
            case JsonValueKind.True:
                return "true";
            case JsonValueKind.False:
                return "false";
            default:
                return value.GetRawText();
        }
    }
}
