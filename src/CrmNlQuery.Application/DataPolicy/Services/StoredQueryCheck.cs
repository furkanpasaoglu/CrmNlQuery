using System.Xml.Linq;
using CrmNlQuery.Application.Ask.Abstractions;
using CrmNlQuery.Domain.Metadata.Entities;

namespace CrmNlQuery.Application.DataPolicy.Services;

/// <summary>
/// Checks a stored query against the data policy allowlist before it is re-run, since fields may have been closed after it was saved.
/// </summary>
public static class StoredQueryCheck
{
    /// <summary>
    /// Returns whether every table and field in the stored FetchXML is still visible in the filtered schema.
    /// </summary>
    public static async Task<bool> IsUsableAsync(string fetchXml, IMetadataSource metadata, CancellationToken ct = default)
    {
        XElement? entity;
        try
        {
            entity = XDocument.Parse(fetchXml).Root?.Element("entity");
        }
        catch (System.Xml.XmlException)
        {
            return false;
        }
        var rootName = (string?)entity?.Attribute("name");
        if (entity is null || string.IsNullOrEmpty(rootName))
        {
            return false;
        }
        var root = await metadata.GetEntityAsync(rootName, ct);
        if (root is null)
        {
            return false;
        }

        var byAlias = new Dictionary<string, EntityInfo>(StringComparer.OrdinalIgnoreCase);
        var byElement = new Dictionary<XElement, EntityInfo>();
        foreach (var link in entity.Descendants("link-entity"))
        {
            var name = (string?)link.Attribute("name") ?? "";
            var info = name.Length == 0 ? null : await metadata.GetEntityAsync(name, ct);
            if (info is null)
            {
                return false;
            }
            byAlias[(string?)link.Attribute("alias") ?? name] = info;
            byElement[link] = info;
        }

        EntityInfo OwnerOf(XElement element)
        {
            var link = element.Ancestors("link-entity").FirstOrDefault();
            return link is not null ? byElement[link] : root;
        }

        bool Has(EntityInfo owner, string? attribute)
        {
            return !string.IsNullOrEmpty(attribute) && owner.FindAttribute(attribute) is not null;
        }

        if (entity.Descendants("condition").Any(c => c.Attribute("valueof") is not null))
        {
            return false;
        }
        foreach (var attribute in entity.Descendants("attribute"))
        {
            if (!Has(OwnerOf(attribute), (string?)attribute.Attribute("name")))
            {
                return false;
            }
        }
        foreach (var order in entity.Descendants("order").Where(o => o.Attribute("attribute") is not null))
        {
            if (!Has(OwnerOf(order), (string?)order.Attribute("attribute")))
            {
                return false;
            }
        }
        foreach (var condition in entity.Descendants("condition"))
        {
            var owner = OwnerOf(condition);
            var alias = (string?)condition.Attribute("entityname");
            if (!string.IsNullOrEmpty(alias))
            {
                if (!byAlias.TryGetValue(alias, out var linked))
                {
                    return false;
                }
                owner = linked;
            }
            if (!Has(owner, (string?)condition.Attribute("attribute")))
            {
                return false;
            }
        }
        foreach (var link in entity.Descendants("link-entity"))
        {
            var parent = link.Parent?.Name.LocalName == "link-entity" ? byElement[link.Parent] : root;
            if (!Has(byElement[link], (string?)link.Attribute("from")) || !Has(parent, (string?)link.Attribute("to")))
            {
                return false;
            }
        }
        return true;
    }
}
