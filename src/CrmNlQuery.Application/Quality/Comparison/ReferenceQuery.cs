using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using CrmNlQuery.Domain.Ask.Entities;

namespace CrmNlQuery.Application.Quality.Comparison;

/// <summary>
/// Shares reference query helpers between eval and quality check: row cap, ordering detection and fingerprint.
/// </summary>
public static class ReferenceQuery
{
    public const int RowCap = 500;

    /// <summary>
    /// Checks whether the FetchXML has an order.
    /// </summary>
    public static bool HasOrder(string fetchXml)
    {
        try { return XDocument.Parse(fetchXml).Descendants("order").Any(); }
        catch (System.Xml.XmlException) { return false; }
    }

    /// <summary>
    /// Applies the product row cap to a non-aggregate reference query, using 500 when no top is set.
    /// </summary>
    public static string WithRowCap(string fetchXml)
    {
        var doc = XDocument.Parse(fetchXml);
        var root = doc.Root!;
        var aggregate = string.Equals((string?)root.Attribute("aggregate"), "true", StringComparison.OrdinalIgnoreCase);
        if (!aggregate && root.Attribute("top") is null && root.Attribute("count") is null)
        {
            root.SetAttributeValue("top", RowCap);
        }
        return root.ToString(SaveOptions.DisableFormatting);
    }

    /// <summary>
    /// Builds a short fingerprint of the reference result from row count and cell digest.
    /// </summary>
    public static string Fingerprint(ResultTable table)
    {
        var all = string.Join("\n", (table.Raw ?? table.Rows).Select(r => string.Join("\u001f", r)).OrderBy(x => x, StringComparer.Ordinal));
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(all))).Substring(0, 12).ToLowerInvariant();
        return $"{table.Rows.Count}:{hash}";
    }
}
