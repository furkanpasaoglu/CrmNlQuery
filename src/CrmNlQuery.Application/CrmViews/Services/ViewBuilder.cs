using System.Xml;
using System.Xml.Linq;
using CrmNlQuery.Application.Ask.Planning.Validation;
using CrmNlQuery.Application.Ask.Abstractions;
using CrmNlQuery.Application.CrmViews.Models;

namespace CrmNlQuery.Application.CrmViews.Services;

/// <summary>
/// Turns a stored, approved FetchXML into a CRM personal view using pure rules and metadata, without touching the CRM.
/// </summary>
public sealed class ViewBuilder
{
    private static readonly string[] PagingAttributes = { "top", "count", "page", "paging-cookie" };

    private readonly PlanValidator _validator;
    private readonly IMetadataSource _metadata;

    public ViewBuilder(PlanValidator validator, IMetadataSource metadata)
    {
        _validator = validator;
        _metadata = metadata;
    }

    /// <summary>
    /// Builds a view definition from stored FetchXML, validating it first and refusing unsupported shapes.
    /// </summary>
    public async Task<ViewBuildResult> BuildAsync(string storedFetchXml, CancellationToken ct = default)
    {
        XElement root;
        try
        {
            root = XDocument.Parse(storedFetchXml).Root!;
        }
        catch (XmlException ex)
        {
            return ViewBuildResult.Reject(ViewRejection.Unparseable, ex.Message);
        }
        if (string.Equals((string?)root.Attribute("aggregate"), "true", StringComparison.OrdinalIgnoreCase))
        {
            return ViewBuildResult.Reject(ViewRejection.Aggregate);
        }

        var validation = await _validator.ValidateFetchXmlAsync(storedFetchXml, ct);
        if (!validation.IsValid)
        {
            return ViewBuildResult.Reject(ViewRejection.Invalid, string.Join("; ", validation.Errors));
        }

        var fetch = XDocument.Parse(validation.NormalizedFetchXml ?? storedFetchXml).Root!;
        var entityElement = fetch.Element("entity")!;
        var entity = await _metadata.GetEntityAsync((string?)entityElement.Attribute("name") ?? "", ct);
        if (entity is null)
        {
            return ViewBuildResult.Reject(ViewRejection.Invalid, "Unknown entity.");
        }

        foreach (var name in PagingAttributes)
        {
            fetch.Attribute(name)?.Remove();
        }

        foreach (var order in fetch.Descendants("order"))
        {
            var descending = string.Equals((string?)order.Attribute("descending"), "true", StringComparison.OrdinalIgnoreCase);
            order.SetAttributeValue("descending", descending ? "true" : "false");
        }

        var columns = entityElement.Elements("attribute")
            .Where(a => (string?)a.Attribute("alias") != DistinctRewriter.RowIdAlias)
            .Select(a => (string?)a.Attribute("name")).Where(n => !string.IsNullOrEmpty(n)).Select(n => n!).ToList();
        if (columns.Count == 0)
        {
            return ViewBuildResult.Reject(ViewRejection.NoColumns);
        }

        foreach (var link in entityElement.Elements("link-entity"))
        {
            if (link.Descendants("link-entity").SelectMany(l => l.Elements("attribute")).Any())
            {
                return ViewBuildResult.Reject(ViewRejection.NestedColumns);
            }
            var linkColumns = link.Elements("attribute")
                .Select(a => (string?)a.Attribute("name")).Where(n => !string.IsNullOrEmpty(n)).Select(n => n!).ToList();
            if (linkColumns.Count == 0)
            {
                continue;
            }

            var linked = await _metadata.GetEntityAsync((string?)link.Attribute("name") ?? "", ct);
            if (linked is null)
            {
                return ViewBuildResult.Reject(ViewRejection.Invalid, "Unknown linked entity.");
            }
            if (!string.Equals((string?)link.Attribute("from"), linked.PrimaryIdAttribute, StringComparison.OrdinalIgnoreCase))
            {
                return ViewBuildResult.Reject(ViewRejection.OneToManyColumns, linked.LogicalName);
            }

            var alias = (string?)link.Attribute("alias") ?? "";
            foreach (var column in linkColumns)
            {
                columns.Add(alias + "." + column);
            }
        }

        return ViewBuildResult.Success(new ViewDefinition(
            entity.LogicalName,
            fetch.ToString(SaveOptions.DisableFormatting),
            columns,
            entity.PrimaryIdAttribute,
            entity.PrimaryNameAttribute));
    }
}
