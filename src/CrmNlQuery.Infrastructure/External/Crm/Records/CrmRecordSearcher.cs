using System.Globalization;
using System.Text.Json;
using System.Xml.Linq;
using CrmNlQuery.Application.Ask.Abstractions;
using CrmNlQuery.Domain.Access.Entities;
using CrmNlQuery.Domain.Ask.Entities;
using CrmNlQuery.Domain.Common.Services;
using CrmNlQuery.Domain.Metadata.Entities;

namespace CrmNlQuery.Infrastructure.External.Crm.Records;

/// <summary>
/// Finds records by name in CRM for the caller using a generated FetchXML query.
/// </summary>
public sealed class CrmRecordSearcher : IRecordSearcher
{
    private const int CandidateLimit = 25;
    private const string CreatedOnAttribute = "createdon";
    private readonly IFetchRunner _executor;

    public CrmRecordSearcher(IFetchRunner executor)
    {
        _executor = executor;
    }

    /// <summary>
    /// Builds the name-search FetchXML for an entity with XElement so special characters are escaped.
    /// </summary>
    public static string BuildFetchXml(EntityInfo entity, string text)
    {
        var entityElement = new XElement("entity", new XAttribute("name", entity.LogicalName),
            new XElement("attribute", new XAttribute("name", entity.PrimaryIdAttribute)),
            new XElement("attribute", new XAttribute("name", entity.PrimaryNameAttribute)));
        if (entity.FindAttribute(CreatedOnAttribute) is not null)
        {
            entityElement.Add(new XElement("attribute", new XAttribute("name", CreatedOnAttribute)));
        }
        entityElement.Add(
            new XElement("filter",
                new XElement("condition",
                    new XAttribute("attribute", entity.PrimaryNameAttribute),
                    new XAttribute("operator", "like"),
                    new XAttribute("value", NameNormalizer.ToLikePattern(text)))),
            new XElement("order", new XAttribute("attribute", entity.PrimaryNameAttribute)));
        var fetch = new XElement("fetch", new XAttribute("top", CandidateLimit), entityElement);
        return fetch.ToString(SaveOptions.DisableFormatting);
    }

    private static DateTime? ReadCreatedOn(JsonElement row)
    {
        if (row.TryGetProperty(CreatedOnAttribute, out var value) && value.ValueKind == JsonValueKind.String
            && DateTime.TryParse(value.GetString(), CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var parsed))
        {
            return parsed;
        }
        return null;
    }

    /// <summary>
    /// Searches an entity for records matching the text, as the given caller.
    /// </summary>
    public async Task<IReadOnlyList<RecordMatch>> SearchByNameAsync(EntityInfo entity, string text, Caller caller, CancellationToken ct = default)
    {
        var rows = await _executor.RunAsync(entity.EntitySetName, BuildFetchXml(entity, text), caller, ct);
        var matches = new List<RecordMatch>();
        foreach (var row in rows.EnumerateArray())
        {
            var id = row.GetProperty(entity.PrimaryIdAttribute).GetGuid();
            var name = row.TryGetProperty(entity.PrimaryNameAttribute, out var n) ? n.GetString() ?? "" : "";
            matches.Add(new RecordMatch(id, name, ReadCreatedOn(row)));
        }

        var wanted = NameNormalizer.Normalize(text);
        var exact = matches.Where(m => NameNormalizer.Normalize(m.Name) == wanted).ToList();
        return exact.Count > 0 ? exact : matches;
    }
}
