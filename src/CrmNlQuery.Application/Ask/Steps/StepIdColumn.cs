using System.Xml;
using System.Xml.Linq;
using CrmNlQuery.Application.Ask.Planning.Validation;
using CrmNlQuery.Application.Ask.Abstractions;

namespace CrmNlQuery.Application.Ask.Steps;

/// <summary>
/// Provides the id column of a producer step in a dependent chain so the next step can consume its ids.
/// </summary>
public sealed class StepIdColumn
{
    private readonly IMetadataSource _metadata;

    public StepIdColumn(IMetadataSource metadata)
    {
        _metadata = metadata;
    }

    /// <summary>
    /// Adds the root primary id as a _rowid alias to a non-aggregate query, reusing an already selected id so no duplicate column appears; returns the updated FetchXML.
    /// </summary>
    public async Task<string> EnsureAsync(string fetchXml, CancellationToken ct = default)
    {
        if (!TryParse(fetchXml, out var doc, out var root, out var entity) || IsAggregate(root))
        {
            return fetchXml;
        }
        var info = await _metadata.GetEntityAsync(entity.Attribute("name")?.Value ?? "", ct);
        if (info is null)
        {
            return fetchXml;
        }
        if (entity.Elements("attribute").Any(a => (string?)a.Attribute("alias") == DistinctRewriter.RowIdAlias))
        {
            return fetchXml;
        }

        var selected = entity.Elements("attribute").FirstOrDefault(a =>
            string.Equals((string?)a.Attribute("name"), info.PrimaryIdAttribute, StringComparison.OrdinalIgnoreCase) && a.Attribute("alias") is null);
        if (selected is not null)
        {
            selected.SetAttributeValue("alias", DistinctRewriter.RowIdAlias);
        }
        else
        {
            var rowId = new XElement("attribute", new XAttribute("name", info.PrimaryIdAttribute), new XAttribute("alias", DistinctRewriter.RowIdAlias));
            var last = entity.Elements("attribute").LastOrDefault();
            if (last is null)
            {
                entity.AddFirst(rowId);
            }
            else
            {
                last.AddAfterSelf(rowId);
            }
        }
        return doc.ToString(SaveOptions.DisableFormatting);
    }

    /// <summary>
    /// Handles id entities for step id column.
    /// </summary>
    public async Task<IReadOnlyList<string>> IdEntitiesAsync(string fetchXml, CancellationToken ct = default)
    {
        var none = new List<string>();
        if (!TryParse(fetchXml, out _, out var root, out var entity))
        {
            return none;
        }
        var rootInfo = await _metadata.GetEntityAsync(entity.Attribute("name")?.Value ?? "", ct);
        if (rootInfo is null)
        {
            return none;
        }
        if (!IsAggregate(root))
        {
            return new List<string> { rootInfo.LogicalName };
        }

        var marked = entity.Descendants("attribute").FirstOrDefault(a =>
            (string?)a.Attribute("alias") == DistinctRewriter.RowIdAlias
            && string.Equals((string?)a.Attribute("groupby"), "true", StringComparison.OrdinalIgnoreCase));
        if (marked is null)
        {
            return none;
        }
        var owner = marked.Parent!;
        var ownerInfo = owner == entity ? rootInfo : await _metadata.GetEntityAsync(owner.Attribute("name")?.Value ?? "", ct);
        var attribute = ownerInfo?.FindAttribute((string?)marked.Attribute("name") ?? "");
        if (ownerInfo is null || attribute is null)
        {
            return none;
        }
        if (string.Equals(attribute.LogicalName, ownerInfo.PrimaryIdAttribute, StringComparison.OrdinalIgnoreCase))
        {
            return new List<string> { ownerInfo.LogicalName };
        }
        if (attribute.AttributeType is "Lookup" or "Customer" or "Owner")
        {
            return ownerInfo.Relationships
                .Where(r => string.Equals(r.ReferencingEntity, ownerInfo.LogicalName, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(r.ReferencingAttribute, attribute.LogicalName, StringComparison.OrdinalIgnoreCase))
                .Select(r => r.ReferencedEntity).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }
        return none;
    }

    private static bool TryParse(string fetchXml, out XDocument doc, out XElement root, out XElement entity)
    {
        doc = null!;
        root = null!;
        entity = null!;
        try
        {
            doc = XDocument.Parse(fetchXml);
        }
        catch (XmlException)
        {
            return false;
        }
        if (doc.Root is null || doc.Root.Element("entity") is null)
        {
            return false;
        }
        root = doc.Root;
        entity = doc.Root.Element("entity")!;
        return true;
    }

    private static bool IsAggregate(XElement root)
    {
        return string.Equals((string?)root.Attribute("aggregate"), "true", StringComparison.OrdinalIgnoreCase);
    }
}
