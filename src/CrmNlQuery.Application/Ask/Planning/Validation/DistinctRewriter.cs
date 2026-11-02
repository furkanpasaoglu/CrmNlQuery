using System.Xml;
using System.Xml.Linq;
using CrmNlQuery.Application.Ask.Abstractions;

namespace CrmNlQuery.Application.Ask.Planning.Validation;

/// <summary>
/// Adds distinct and a hidden root id when a one-to-many link is used only as a filter, so root rows are not duplicated.
/// </summary>
public sealed class DistinctRewriter
{
    public const string RowIdAlias = "_rowid";

    private readonly IMetadataSource _metadata;

    public DistinctRewriter(IMetadataSource metadata)
    {
        _metadata = metadata;
    }

    /// <summary>
    /// Rewrites the FetchXML to avoid duplicate root rows, leaving queries with linked columns or aggregates unchanged.
    /// </summary>
    public async Task<string> ApplyAsync(string fetchXml, CancellationToken ct = default)
    {
        XDocument doc;
        try
        {
            doc = XDocument.Parse(fetchXml);
        }
        catch (XmlException)
        {
            return fetchXml;
        }

        var root = doc.Root;
        var entity = root?.Element("entity");
        if (root is null || entity is null || IsAggregate(root))
        {
            return fetchXml;
        }

        var links = entity.Elements("link-entity").ToList();
        if (links.Count == 0 || links.Any(HasColumnsOrOrder))
        {
            return fetchXml;
        }

        var info = await _metadata.GetEntityAsync(entity.Attribute("name")?.Value ?? "", ct);
        if (info is null || !await AnyLinkMultipliesAsync(links, info.PrimaryIdAttribute, ct))
        {
            return fetchXml;
        }

        root.SetAttributeValue("distinct", "true");
        var hasId = entity.Elements("attribute").Any(a =>
            string.Equals((string?)a.Attribute("name"), info.PrimaryIdAttribute, StringComparison.OrdinalIgnoreCase)
            || string.Equals((string?)a.Attribute("alias"), RowIdAlias, StringComparison.Ordinal));
        if (!hasId)
        {
            var rowId = new XElement("attribute", new XAttribute("name", info.PrimaryIdAttribute), new XAttribute("alias", RowIdAlias));
            var lastAttribute = entity.Elements("attribute").LastOrDefault();
            if (lastAttribute is null)
            {
                entity.AddFirst(rowId);
            }
            else
            {
                lastAttribute.AddAfterSelf(rowId);
            }
        }
        return doc.ToString(SaveOptions.DisableFormatting);
    }

    private static bool IsAggregate(XElement root)
    {
        return string.Equals((string?)root.Attribute("aggregate"), "true", StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasColumnsOrOrder(XElement link)
    {
        return link.Descendants().Any(e => e.Name.LocalName is "attribute" or "order");
    }

    private async Task<bool> AnyLinkMultipliesAsync(IEnumerable<XElement> links, string parentPrimaryId, CancellationToken ct)
    {
        foreach (var link in links)
        {
            var child = await _metadata.GetEntityAsync(link.Attribute("name")?.Value ?? "", ct);
            if (child is null)
            {
                continue;
            }
            var to = (string?)link.Attribute("to") ?? "";
            var from = (string?)link.Attribute("from") ?? "";
            var multiplies = string.Equals(to, parentPrimaryId, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(from, child.PrimaryIdAttribute, StringComparison.OrdinalIgnoreCase);
            if (multiplies || await AnyLinkMultipliesAsync(link.Elements("link-entity"), child.PrimaryIdAttribute, ct))
            {
                return true;
            }
        }
        return false;
    }
}
