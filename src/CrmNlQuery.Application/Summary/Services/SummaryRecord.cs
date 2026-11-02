using System.Xml.Linq;

namespace CrmNlQuery.Application.Summary.Services;

/// <summary>
/// Helper for reading a customer summary's stored record reference.
/// </summary>
public static class SummaryRecord
{
    /// <summary>
    /// Returns the root record id from a history entry's stored query, or null if it is broken.
    /// </summary>
    public static Guid? IdOf(string fetchXml)
    {
        try
        {
            var value = (string?)XDocument.Parse(fetchXml).Descendants("condition").FirstOrDefault()?.Attribute("value");
            return Guid.TryParse(value, out var id) ? id : null;
        }
        catch (System.Xml.XmlException)
        {
            return null;
        }
    }
}
