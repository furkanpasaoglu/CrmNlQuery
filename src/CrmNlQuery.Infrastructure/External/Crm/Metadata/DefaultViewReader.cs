using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using CrmNlQuery.Application.Ask.Abstractions;

namespace CrmNlQuery.Infrastructure.External.Crm.Metadata;

/// <summary>
/// Reads a table's default view columns, serving as the default-view source.
/// </summary>
public sealed class DefaultViewReader : IDefaultViewSource
{
    private static readonly Regex SafeName = new Regex("^[a-z0-9_]+$", RegexOptions.Compiled);
    private readonly CrmClient _client;

    public DefaultViewReader(CrmClient client)
    {
        _client = client;
    }

    /// <summary>
    /// Gets default columns for default view reader.
    /// </summary>
    public async Task<IReadOnlyList<string>> GetDefaultColumnsAsync(string entityLogicalName, CancellationToken ct = default)
    {
        if (!SafeName.IsMatch(entityLogicalName ?? ""))
        {
            return new List<string>();
        }

        var filter = $"returnedtypecode eq '{entityLogicalName}' and isdefault eq true and querytype eq 0";
        var url = "savedqueries?$select=layoutxml&$top=1&$filter=" + Uri.EscapeDataString(filter);
        using var doc = await _client.GetAsServiceAsync(url, ct);
        foreach (var row in doc.RootElement.GetProperty("value").EnumerateArray())
        {
            if (row.TryGetProperty("layoutxml", out var layout) && layout.GetString() is { } xml)
            {
                return ParseLayout(xml);
            }
        }
        return new List<string>();
    }

    /// <summary>
    /// Parses layout for default view reader.
    /// </summary>
    public static IReadOnlyList<string> ParseLayout(string layoutXml)
    {
        try
        {
            return XDocument.Parse(layoutXml).Descendants("cell")
                .Select(c => c.Attribute("name")?.Value)
                .Where(n => IsPlainAttribute(n))
                .Select(n => n!)
                .ToList();
        }
        catch (XmlException)
        {
            return new List<string>();
        }
    }

    private static bool IsPlainAttribute(string? name)
    {
        return !string.IsNullOrEmpty(name) && !name.Contains('.');
    }
}
