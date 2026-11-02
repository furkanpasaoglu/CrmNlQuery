using System.Xml.Linq;

namespace CrmNlQuery.Application.Ask.Services;

/// <summary>
/// Finds the columns shown on the root entity of a FetchXML, so follow-ups and narrowing show the same columns.
/// </summary>
public static class FetchXmlColumns
{
    /// <summary>
    /// Returns the root entity's displayed columns of a FetchXML.
    /// </summary>
    public static IReadOnlyList<string> Of(string fetchXml)
    {
        try
        {
            var fetch = XDocument.Parse(fetchXml).Root;
            if (fetch is null || string.Equals((string?)fetch.Attribute("aggregate"), "true", StringComparison.OrdinalIgnoreCase))
            {
                return new List<string>();
            }
            var entity = fetch.Element("entity");
            if (entity is null || entity.Element("all-attributes") is not null)
            {
                return new List<string>();
            }
            return entity.Elements("attribute").Where(a => (string?)a.Attribute("alias") != Planning.Validation.DistinctRewriter.RowIdAlias).Select(a => (string?)a.Attribute("name")).Where(n => !string.IsNullOrEmpty(n)).Select(n => n!).ToList();
        }
        catch (System.Xml.XmlException)
        {
            return new List<string>();
        }
    }
}
