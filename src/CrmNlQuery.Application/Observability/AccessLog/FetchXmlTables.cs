using System.Xml.Linq;

namespace CrmNlQuery.Application.Observability.AccessLog;

/// <summary>
/// Extracts only the table names (root table plus nested link-entity tables) from a FetchXML string, ignoring values and ids; malformed input yields an empty list.
/// </summary>
public static class FetchXmlTables
{
    /// <summary>
    /// Creates a value from for fetch xml tables.
    /// </summary>
    public static IReadOnlyList<string> From(string? fetchXml)
    {
        if (string.IsNullOrWhiteSpace(fetchXml))
        {
            return new List<string>();
        }
        try
        {
            var root = XDocument.Parse(fetchXml).Root;
            var names = new List<string>();
            var entity = root?.Element("entity");
            if (entity is null)
            {
                return names;
            }
            names.Add((string?)entity.Attribute("name") ?? "");
            names.AddRange(entity.Descendants("link-entity").Select(l => (string?)l.Attribute("name") ?? ""));
            return names.Select(n => n.Trim().ToLowerInvariant()).Where(n => n.Length > 0).Distinct().ToList();
        }
        catch (Exception)
        {
            return new List<string>();
        }
    }
}
