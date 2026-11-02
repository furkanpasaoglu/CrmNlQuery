using CrmNlQuery.Application.Observability.Abstractions;

namespace CrmNlQuery.Infrastructure.External.Crm;

/// <summary>
/// Resolves CRM user names for ids, using the service account in a single query of at most 50 ids.
/// </summary>
public sealed class CrmUserNames : IUserNames
{
    private const int MaxIds = 50;
    private readonly CrmClient _client;

    public CrmUserNames(CrmClient client)
    {
        _client = client;
    }

    /// <summary>
    /// Returns a dictionary of user ids to full names.
    /// </summary>
    public async Task<IReadOnlyDictionary<Guid, string>> GetNamesAsync(IEnumerable<Guid> ids, CancellationToken ct = default)
    {
        var asked = ids.Distinct().Take(MaxIds).ToList();
        var result = new Dictionary<Guid, string>();
        if (asked.Count == 0)
        {
            return result;
        }

        var filter = string.Join(" or ", asked.Select(id => $"systemuserid eq {id}"));
        var url = "systemusers?$select=systemuserid,fullname&$filter=" + Uri.EscapeDataString(filter);
        using var doc = await _client.GetAsServiceAsync(url, ct);
        foreach (var row in doc.RootElement.GetProperty("value").EnumerateArray())
        {
            var name = row.TryGetProperty("fullname", out var n) ? n.GetString() : null;
            if (name is not null && row.TryGetProperty("systemuserid", out var id) && id.TryGetGuid(out var guid))
            {
                result[guid] = name;
            }
        }
        return result;
    }
}
