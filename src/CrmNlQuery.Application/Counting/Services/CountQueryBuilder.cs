using System.Xml;
using System.Xml.Linq;
using CrmNlQuery.Application.Ask.Planning.Validation;
using CrmNlQuery.Application.Ask.Abstractions;
using CrmNlQuery.Application.Counting.Models;

namespace CrmNlQuery.Application.Counting.Services;

/// <summary>
/// Derives how-many-records queries from a stored approved query using pure rules and metadata.
/// </summary>
public sealed class CountQueryBuilder
{
    private static readonly string[] RootAttributesToDrop = { "top", "count", "page", "paging-cookie", "distinct", "aggregate", "returntotalrecordcount" };

    private readonly PlanValidator _validator;
    private readonly IMetadataSource _metadata;

    public CountQueryBuilder(PlanValidator validator, IMetadataSource metadata)
    {
        _validator = validator;
        _metadata = metadata;
    }

    /// <summary>
    /// Builds count queries from stored FetchXML after validating it, keeping filters and links.
    /// </summary>
    public async Task<CountBuildResult> BuildAsync(string storedFetchXml, CancellationToken ct = default)
    {
        XElement original;
        try
        {
            original = XDocument.Parse(storedFetchXml).Root!;
        }
        catch (XmlException ex)
        {
            return CountBuildResult.Reject(CountRejection.Unparseable, ex.Message);
        }
        if (string.Equals((string?)original.Attribute("aggregate"), "true", StringComparison.OrdinalIgnoreCase))
        {
            return CountBuildResult.Reject(CountRejection.Aggregate);
        }

        var validation = await _validator.ValidateFetchXmlAsync(storedFetchXml, ct);
        if (!validation.IsValid)
        {
            return CountBuildResult.Reject(CountRejection.Invalid, string.Join("; ", validation.Errors));
        }

        var entityName = (string?)XDocument.Parse(validation.NormalizedFetchXml ?? storedFetchXml).Root!.Element("entity")?.Attribute("name") ?? "";
        var info = await _metadata.GetEntityAsync(entityName, ct);
        if (info is null || string.IsNullOrEmpty(info.PrimaryIdAttribute))
        {
            return CountBuildResult.Reject(CountRejection.Invalid, "Unknown entity.");
        }

        var aggregate = Stripped(validation.NormalizedFetchXml ?? storedFetchXml);
        aggregate.SetAttributeValue("aggregate", "true");
        aggregate.Element("entity")!.AddFirst(new XElement("attribute",
            new XAttribute("name", info.PrimaryIdAttribute), new XAttribute("alias", "n"),
            new XAttribute("aggregate", "countcolumn"), new XAttribute("distinct", "true")));

        var total = Stripped(validation.NormalizedFetchXml ?? storedFetchXml);
        total.SetAttributeValue("distinct", "true");
        total.SetAttributeValue("count", "1");
        total.SetAttributeValue("page", "1");
        total.SetAttributeValue("returntotalrecordcount", "true");
        total.Element("entity")!.AddFirst(new XElement("attribute", new XAttribute("name", info.PrimaryIdAttribute)));

        return CountBuildResult.Success(new CountQueries(
            info.EntitySetName,
            aggregate.ToString(SaveOptions.DisableFormatting),
            total.ToString(SaveOptions.DisableFormatting),
            info.Label));
    }

    private static XElement Stripped(string fetchXml)
    {
        var fetch = XDocument.Parse(fetchXml).Root!;
        foreach (var name in RootAttributesToDrop)
        {
            fetch.Attribute(name)?.Remove();
        }
        fetch.Descendants("order").ToList().ForEach(e => e.Remove());
        fetch.Descendants("attribute").ToList().ForEach(e => e.Remove());
        return fetch;
    }
}
