using System.Xml.Linq;
using CrmNlQuery.Application.Ask.Abstractions;
using CrmNlQuery.Application.Localization.Models;
using CrmNlQuery.Application.Localization.Services;
using CrmNlQuery.Domain.Metadata.Entities;

namespace CrmNlQuery.Application.Explain.Services;

/// <summary>
/// Turns result table headers into readable labels using the executed FetchXML and metadata labels, without a model.
/// </summary>
public static class ColumnLabeler
{
    private const string RowId = "_rowid";

    /// <summary>
    /// Returns labels matching the result table columns, or null on any error so the headers stay as they were.
    /// </summary>
    public static async Task<IReadOnlyList<string>?> TryLabelAsync(string fetchXml, IReadOnlyList<string> columns, IMetadataSource metadata, CancellationToken ct = default)
    {
        try
        {
            var root = XDocument.Parse(fetchXml).Root;
            var entity = root?.Element("entity");
            if (entity is null) { return null; }
            var infos = new Dictionary<string, EntityInfo?>(StringComparer.OrdinalIgnoreCase);
            var labels = new List<string>();
            await AddAsync(entity, null, null, metadata, infos, labels, ct);
            return labels.Count == columns.Count ? labels : null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return null;
        }
    }

    private static async Task AddAsync(XElement scope, string? prefix, string? linkLabel, IMetadataSource metadata, Dictionary<string, EntityInfo?> infos, List<string> labels, CancellationToken ct)
    {
        var entityName = (string?)scope.Attribute("name") ?? "";
        if (!infos.TryGetValue(entityName, out var info))
        {
            info = await metadata.GetEntityAsync(entityName, ct);
            infos[entityName] = info;
        }
        foreach (var child in scope.Elements())
        {
            if (child.Name.LocalName == "attribute")
            {
                var alias = (string?)child.Attribute("alias");
                var name = (string?)child.Attribute("name") ?? "";
                var key = prefix is null ? (alias ?? name) : prefix + "." + (alias ?? name);
                if (key == RowId || key.EndsWith("." + RowId, StringComparison.Ordinal)) { continue; }
                labels.Add(LabelOf(child, info, linkLabel, key));
            }
            else if (child.Name.LocalName == "link-entity")
            {
                var linkAlias = (string?)child.Attribute("alias") ?? "";
                var linkName = (string?)child.Attribute("name") ?? "";
                if (!infos.TryGetValue(linkName, out var linkInfo))
                {
                    linkInfo = await metadata.GetEntityAsync(linkName, ct);
                    infos[linkName] = linkInfo;
                }
                await AddAsync(child, linkAlias, linkInfo?.Label, metadata, infos, labels, ct);
            }
        }
    }

    private static string LabelOf(XElement attribute, EntityInfo? info, string? linkLabel, string fallback)
    {
        var name = (string?)attribute.Attribute("name") ?? "";
        var attr = info?.FindAttribute(name);
        var fieldLabel = string.IsNullOrWhiteSpace(attr?.Label) ? null : attr!.Label;
        var aggregate = ((string?)attribute.Attribute("aggregate"))?.ToLowerInvariant();
        string? text;
        if (aggregate is "count" or "countcolumn")
        {
            text = MessageCatalog.Now(Msg.ColCount);
        }
        else if (aggregate is "sum" or "avg" or "min" or "max")
        {
            var key = aggregate switch { "sum" => Msg.ColSum, "avg" => Msg.ColAvg, "min" => Msg.ColMin, _ => Msg.ColMax };
            text = fieldLabel is null ? null : MessageCatalog.Now(key, MessageCatalog.Args(("field", fieldLabel)));
        }
        else if (fieldLabel is null)
        {
            text = null;
        }
        else
        {
            var grouping = ((string?)attribute.Attribute("dategrouping"))?.ToLowerInvariant();
            var unit = grouping switch
            {
                "year" => Msg.UnitYear,
                "quarter" => Msg.UnitQuarter,
                "month" => Msg.UnitMonth,
                "week" => Msg.UnitWeek,
                "day" => Msg.UnitDay,
                _ => null,
            };
            text = unit is null ? fieldLabel : $"{fieldLabel} ({MessageCatalog.Now(unit)})";
        }
        if (text is null) { return fallback; }
        var isMeasure = aggregate is "count" or "countcolumn" or "sum" or "avg" or "min" or "max";
        return linkLabel is not null && !isMeasure ? $"{linkLabel} · {text}" : text;
    }
}
