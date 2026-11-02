using System.Xml.Linq;
using CrmNlQuery.Application.Ask.Abstractions;
using CrmNlQuery.Application.Explain.Models;
using CrmNlQuery.Domain.Ask.Entities;
using CrmNlQuery.Domain.Metadata.Entities;

namespace CrmNlQuery.Application.Explain.Services;

/// <summary>
/// Builds a query explanation from the executed FetchXML without a model, CRM calls or row data.
/// </summary>
public sealed class QueryExplainer
{
    private const int DefaultTop = 500;

    private readonly IMetadataSource _metadata;
    private readonly Func<DateTimeOffset> _clock;

    public QueryExplainer(IMetadataSource metadata, Func<DateTimeOffset>? clock = null)
    {
        _metadata = metadata;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    /// <summary>
    /// Explains the FetchXML as a structured explanation using metadata labels and resolved record names.
    /// </summary>
    public async Task<QueryExplanation?> ExplainAsync(string fetchXml, IReadOnlyList<ResolvedRecord>? resolved = null, CancellationToken ct = default)
    {
        try
        {
            return await BuildAsync(fetchXml, resolved ?? new List<ResolvedRecord>(), ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private async Task<QueryExplanation?> BuildAsync(string fetchXml, IReadOnlyList<ResolvedRecord> resolved, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(fetchXml))
        {
            return null;
        }
        var fetch = XDocument.Parse(fetchXml).Root;
        var entity = fetch?.Element("entity");
        var rootName = (string?)entity?.Attribute("name");
        if (fetch is null || entity is null || string.IsNullOrEmpty(rootName))
        {
            return null;
        }
        var root = await _metadata.GetEntityAsync(rootName, ct);
        if (root is null)
        {
            return null;
        }

        if (entity.Descendants("filter").Any(f => string.Equals((string?)f.Attribute("type"), "or", StringComparison.OrdinalIgnoreCase)
            && f.Elements().Count() > 1 && f.Elements("filter").Any()))
        {
            return null;
        }

        var links = new Dictionary<string, EntityInfo?>(StringComparer.OrdinalIgnoreCase);
        var linkInfo = new Dictionary<string, EntityInfo?>(StringComparer.OrdinalIgnoreCase);
        foreach (var link in entity.Descendants("link-entity"))
        {
            var name = (string?)link.Attribute("name") ?? "";
            if (!linkInfo.TryGetValue(name, out var info))
            {
                info = name.Length == 0 ? null : await _metadata.GetEntityAsync(name, ct);
                linkInfo[name] = info;
            }
            links[(string?)link.Attribute("alias") ?? name] = info;
        }

        var orFilters = entity.Descendants("filter")
            .Where(f => string.Equals((string?)f.Attribute("type"), "or", StringComparison.OrdinalIgnoreCase) && f.Elements("condition").Count() > 1)
            .ToList();

        var filters = new List<ExplainFilter>();
        foreach (var condition in Conditions(entity))
        {
            var attribute = ((string?)condition.Attribute("attribute"))!;

            var owner = root;
            string? via = null;
            var inside = condition.Ancestors("link-entity").FirstOrDefault();
            var aliasName = (string?)condition.Attribute("entityname");
            if (!string.IsNullOrEmpty(aliasName) && links.TryGetValue(aliasName, out var byAlias))
            {
                owner = byAlias;
                via = byAlias?.Label ?? aliasName;
            }
            else if (inside is not null)
            {
                var insideName = (string?)inside.Attribute("name") ?? "";
                var key = (string?)inside.Attribute("alias") ?? insideName;
                links.TryGetValue(key, out owner);
                via = owner?.Label ?? insideName;
            }

            var info = owner?.Attributes.FirstOrDefault(a => string.Equals(a.LogicalName, attribute, StringComparison.OrdinalIgnoreCase));
            var label = string.IsNullOrWhiteSpace(info?.Label) ? attribute : info!.Label;
            var op = ((string?)condition.Attribute("operator") ?? "eq").ToLowerInvariant();

            var raw = new List<string>();
            var single = (string?)condition.Attribute("value");
            if (single is not null)
            {
                raw.Add(single);
            }
            raw.AddRange(condition.Elements("value").Select(v => v.Value));

            var values = new List<ExplainValue>();
            if (op is "like" or "not-like" && raw.Count == 1)
            {
                (op, var shown) = ReadableLike(op, raw[0]);
                values.Add(new ExplainValue(shown, null));
            }
            else
            {
                foreach (var v in raw)
                {
                    values.Add(ToValue(v, info, resolved));
                }
            }

            string? group = null;
            var parentFilter = condition.Parent;
            var orIndex = parentFilter is null ? -1 : orFilters.IndexOf(parentFilter);
            if (orIndex >= 0)
            {
                group = "o" + (orIndex + 1);
            }

            var range = DateRange.Resolve(op, raw.Count > 0 ? raw[0] : null, DateOnly.FromDateTime(_clock().UtcDateTime));
            filters.Add(new ExplainFilter("f" + (filters.Count + 1), attribute, label, op, values)
            {
                Via = via,
                Group = group,
                Edit = EditOf(op, raw, info),
                From = range.From?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
                To = range.To?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
            });
        }

        var aggregate = string.Equals((string?)fetch.Attribute("aggregate"), "true", StringComparison.OrdinalIgnoreCase);

        var measures = new List<ExplainMeasure>();
        if (aggregate)
        {
            foreach (var m in entity.Descendants("attribute").Where(a => !string.IsNullOrEmpty((string?)a.Attribute("aggregate"))))
            {
                var fn = ((string?)m.Attribute("aggregate") ?? "").ToLowerInvariant();
                var name = (string?)m.Attribute("name") ?? "";
                var link = m.Ancestors("link-entity").FirstOrDefault();
                EntityInfo? owner = root;
                if (link is not null)
                {
                    links.TryGetValue((string?)link.Attribute("alias") ?? (string?)link.Attribute("name") ?? "", out owner);
                }
                var field = owner?.Attributes.FirstOrDefault(x => string.Equals(x.LogicalName, name, StringComparison.OrdinalIgnoreCase));
                var rowCount = fn == "count" || string.Equals(name, owner?.PrimaryIdAttribute, StringComparison.OrdinalIgnoreCase);
                var fieldLabel = rowCount ? null : (string.IsNullOrWhiteSpace(field?.Label) ? name : field!.Label);
                measures.Add(new ExplainMeasure(rowCount ? "count" : fn, fieldLabel, (string?)m.Attribute("alias") ?? name));
            }
        }

        var orders = aggregate ? entity.Descendants("order") : entity.Elements("order");
        var sort = orders.Select(o =>
        {
            var alias = (string?)o.Attribute("alias");
            var measure = alias is null ? null : measures.FirstOrDefault(x => string.Equals(x.Alias, alias, StringComparison.OrdinalIgnoreCase));
            var name = (string?)o.Attribute("attribute") ?? alias ?? "";
            var a = root.Attributes.FirstOrDefault(x => string.Equals(x.LogicalName, name, StringComparison.OrdinalIgnoreCase));
            var shown = measure is not null ? (measure.FieldLabel ?? alias!) : string.IsNullOrWhiteSpace(a?.Label) ? name : a!.Label;
            return new ExplainSort(name, shown, string.Equals((string?)o.Attribute("descending"), "true", StringComparison.OrdinalIgnoreCase)) { Fn = measure?.Fn };
        }).Where(s => s.Field.Length > 0).ToList();

        int? top = int.TryParse((string?)fetch.Attribute("top"), out var n) && n > 0 && n < DefaultTop ? n : null;
        var groupBy = new List<string>();
        if (aggregate)
        {
            foreach (var g in entity.Elements("attribute").Where(a => string.Equals((string?)a.Attribute("groupby"), "true", StringComparison.OrdinalIgnoreCase)
                && !string.Equals((string?)a.Attribute("alias"), "_rowid", StringComparison.OrdinalIgnoreCase)))
            {
                var name = (string?)g.Attribute("name") ?? "";
                var a = root.Attributes.FirstOrDefault(x => string.Equals(x.LogicalName, name, StringComparison.OrdinalIgnoreCase));
                var shownName = string.IsNullOrWhiteSpace(a?.Label) ? name : a!.Label;
                var unit = (string?)g.Attribute("dategrouping");
                groupBy.Add(string.IsNullOrWhiteSpace(unit) ? shownName : $"{shownName} ({unit.ToLowerInvariant()})");
            }
        }

        return new QueryExplanation(root.LogicalName, root.Label, filters, sort, top, aggregate, groupBy) { Measures = measures };
    }

    /// <summary>
    /// Returns the filter conditions in the order that defines chip ids so explanation and editing use the same list.
    /// </summary>
    public static IReadOnlyList<XElement> Conditions(XElement entity)
    {
        return entity.Descendants("condition").Where(c => !string.IsNullOrEmpty((string?)c.Attribute("attribute"))).ToList();
    }

    private static readonly HashSet<string> NumberTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Decimal", "Double", "Money" };
    private static readonly HashSet<string> IntegerTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Integer", "BigInt" };
    private static readonly HashSet<string> TextTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "String", "Memo" };

    private static FilterEdit? EditOf(string op, IReadOnlyList<string> raw, AttributeInfo? attribute)
    {
        if (attribute is null || raw.Count != 1 || Guid.TryParseExact(raw[0], "D", out _))
        {
            return null;
        }
        if (op is "eq" or "ne" && attribute.Options.Count > 0)
        {
            return new FilterEdit("option", attribute.Options.Select(o => new ExplainValue(o.Value.ToString(System.Globalization.CultureInfo.InvariantCulture), o.Label)).ToList());
        }
        if (op is "eq" or "ne" or "gt" or "ge" or "lt" or "le" && IntegerTypes.Contains(attribute.AttributeType))
        {
            return new FilterEdit("integer", null);
        }
        if (op is "eq" or "ne" or "gt" or "ge" or "lt" or "le" && NumberTypes.Contains(attribute.AttributeType))
        {
            return new FilterEdit("number", null);
        }
        if (op is "eq" or "ne" or "like" or "not-like" or "contains" or "begins-with" or "ends-with" or "not-contains" or "not-begins-with" or "not-ends-with"
            && TextTypes.Contains(attribute.AttributeType))
        {
            return new FilterEdit("text", null);
        }
        return null;
    }

    private static ExplainValue ToValue(string raw, AttributeInfo? attribute, IReadOnlyList<ResolvedRecord> resolved)
    {
        if (Guid.TryParseExact(raw, "D", out var id))
        {
            var record = resolved.FirstOrDefault(r => r.Id == id);
            return new ExplainValue(null, record?.Name);
        }
        var option = attribute?.Options.FirstOrDefault(o => o.Value.ToString() == raw);
        return new ExplainValue(raw, option?.Label);
    }

    private static (string Op, string Value) ReadableLike(string op, string pattern)
    {
        var negative = op == "not-like";
        var starts = pattern.StartsWith('%');
        var ends = pattern.EndsWith('%');
        var core = pattern.Trim('%');
        var inner = core.Contains('%') || core.Length == 0 ? null : core;
        if (inner is null || (!starts && !ends))
        {
            return (op, pattern);
        }
        var name = starts && ends ? "contains" : ends ? "begins-with" : "ends-with";
        return ((negative ? "not-" : "") + name, inner);
    }
}
