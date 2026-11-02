using System.Text.Json;
using CrmNlQuery.Application.Access.Abstractions;
using CrmNlQuery.Application.Access.Models;
using CrmNlQuery.Application.Common.Exceptions;

namespace CrmNlQuery.Infrastructure.External.Crm;

/// <summary>
/// Looks up a CRM user by system user id with the service account, using only the allowed system user columns, and reads the names of the user's security roles and teams as group names.
/// </summary>
public sealed class CrmUserDirectory : ICrmUserDirectory
{
    public const string RolePrefix = "role:";
    public const string TeamPrefix = "team:";
    private const int MaxGroups = 100;
    private const int MaxNameLength = 100;

    private readonly CrmClient _client;

    public CrmUserDirectory(CrmClient client)
    {
        _client = client;
    }

    /// <summary>
    /// Returns the user with account name, disabled flag and group names (role:Name and team:Name), or null if the user does not exist; groups stay empty when the service account may not read roles or teams.
    /// </summary>
    public async Task<CrmUserRecord?> GetAsync(Guid userId, CancellationToken ct = default)
    {
        var url = "systemusers?$select=systemuserid,domainname,isdisabled&$filter=" + Uri.EscapeDataString($"systemuserid eq {userId:D}") + "&$top=1";
        using var doc = await _client.GetAsServiceAsync(url, ct);
        var rows = doc.RootElement.GetProperty("value");
        if (rows.GetArrayLength() != 1)
        {
            return null;
        }
        var row = rows[0];
        var account = row.TryGetProperty("domainname", out var name) && name.ValueKind == JsonValueKind.String ? name.GetString() ?? "" : "";
        var disabled = row.TryGetProperty("isdisabled", out var flag) && flag.ValueKind == JsonValueKind.True;
        var groups = new List<string>();
        groups.AddRange(await ReadNamesAsync(userId, "systemuserroles_association", RolePrefix, ct));
        groups.AddRange(await ReadNamesAsync(userId, "teammembership_association", TeamPrefix, ct));
        return new CrmUserRecord(userId, account, disabled, groups.Take(MaxGroups * 2).ToList());
    }

    /// <summary>
    /// Turns the CRM answer for roles or teams into clean group names with the given prefix: no control characters, trimmed, limited in length and count, without duplicates.
    /// </summary>
    public static IReadOnlyList<string> ParseNames(JsonElement root, string prefix)
    {
        var names = new List<string>();
        if (!root.TryGetProperty("value", out var rows) || rows.ValueKind != JsonValueKind.Array)
        {
            return names;
        }
        foreach (var row in rows.EnumerateArray())
        {
            if (!row.TryGetProperty("name", out var value) || value.ValueKind != JsonValueKind.String)
            {
                continue;
            }
            var clean = new string((value.GetString() ?? "").Where(c => !char.IsControl(c)).ToArray()).Trim();
            if (clean.Length == 0)
            {
                continue;
            }
            if (clean.Length > MaxNameLength)
            {
                clean = clean.Substring(0, MaxNameLength);
            }
            var group = prefix + clean;
            if (!names.Contains(group, StringComparer.OrdinalIgnoreCase))
            {
                names.Add(group);
            }
            if (names.Count >= MaxGroups)
            {
                break;
            }
        }
        return names;
    }

    private async Task<IReadOnlyList<string>> ReadNamesAsync(Guid userId, string association, string prefix, CancellationToken ct)
    {
        try
        {
            using var doc = await _client.GetAsServiceAsync($"systemusers({userId:D})/{association}?$select=name&$top=200", ct);
            return ParseNames(doc.RootElement, prefix);
        }
        catch (CrmException ex) when (ex.IsPermissionDenied)
        {
            return new string[0];
        }
    }
}
