using CrmNlQuery.Application.Access.Abstractions;

namespace CrmNlQuery.Infrastructure.External.Crm;

/// <summary>
/// Finds the CRM user for a Windows account using the service account, since this lookup is not user data.
/// </summary>
public sealed class CrmCallerResolver : ICallerResolver
{
    private readonly CrmClient _client;

    public CrmCallerResolver(CrmClient client)
    {
        _client = client;
    }

    /// <summary>
    /// Resolves the CRM user id for a Windows account.
    /// </summary>
    public async Task<Guid?> ResolveAsync(string windowsAccount, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(windowsAccount))
        {
            return null;
        }

        var escaped = windowsAccount.Trim().Replace("'", "''");
        var filter = $"domainname eq '{escaped}' and isdisabled eq false";
        var url = "systemusers?$select=systemuserid&$filter=" + Uri.EscapeDataString(filter) + "&$top=2";
        using var doc = await _client.GetAsServiceAsync(url, ct);

        var rows = doc.RootElement.GetProperty("value");
        if (rows.GetArrayLength() != 1)
        {
            return null;
        }
        return rows[0].GetProperty("systemuserid").GetGuid();
    }
}
