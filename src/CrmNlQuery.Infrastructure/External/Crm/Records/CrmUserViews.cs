using System.Text.Json;
using CrmNlQuery.Application.CrmViews.Abstractions;
using CrmNlQuery.Application.CrmViews.Models;
using CrmNlQuery.Application.CrmViews.Services;
using CrmNlQuery.Domain.Access.Entities;

namespace CrmNlQuery.Infrastructure.External.Crm.Records;

/// <summary>
/// CRM implementation of the personal view store, working on the caller's own views through impersonation and matching views by name and owner.
/// </summary>
public sealed class CrmUserViews : IUserViewStore
{
    private readonly CrmClient _client;
    private readonly Dictionary<string, int> _typeCodes = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _typeCodeGate = new SemaphoreSlim(1, 1);

    public CrmUserViews(CrmClient client)
    {
        _client = client;
    }

    private sealed record Existing(string Id, string Entity);

    /// <summary>
    /// Creates or updates the caller's personal view and returns its id.
    /// </summary>
    public async Task<string> UpsertAsync(Guid caller, string name, ViewDefinition view, CancellationToken ct = default)
    {
        var typeCode = await ObjectTypeCodeAsync(view.Entity, ct);
        var layout = ViewLayout.ToLayoutXml(view, typeCode);
        var mine = await FindAsync(caller, name, ct);

        if (mine.Count == 0)
        {
            return await CreateAsync(caller, name, view, layout, ct);
        }

        foreach (var extra in mine.Skip(1))
        {
            await DeleteByIdAsync(caller, extra.Id, ct);
        }

        var first = mine[0];
        if (!string.Equals(first.Entity, view.Entity, StringComparison.OrdinalIgnoreCase))
        {
            await DeleteByIdAsync(caller, first.Id, ct);
            return await CreateAsync(caller, name, view, layout, ct);
        }

        var patch = JsonSerializer.Serialize(new Dictionary<string, string> { ["fetchxml"] = view.FetchXml, ["layoutxml"] = layout });
        await _client.SendJsonAsync(HttpMethod.Patch, $"userqueries({first.Id})", patch, caller, ct);
        return first.Id;
    }

    /// <summary>
    /// Deletes the caller's personal view by name.
    /// </summary>
    public async Task DeleteAsync(Guid caller, string name, CancellationToken ct = default)
    {
        foreach (var view in await FindAsync(caller, name, ct))
        {
            await DeleteByIdAsync(caller, view.Id, ct);
        }
    }

    private async Task<List<Existing>> FindAsync(Guid caller, string name, CancellationToken ct)
    {
        var filter = $"name eq '{name.Replace("'", "''")}' and _ownerid_value eq {caller:D}";
        var url = "userqueries?$select=userqueryid,returnedtypecode,createdon&$orderby=createdon asc&$filter=" + Uri.EscapeDataString(filter);
        using var doc = await _client.GetAsync(url, Caller.User(caller), ct);
        var list = new List<Existing>();
        foreach (var item in doc.RootElement.GetProperty("value").EnumerateArray())
        {
            list.Add(new Existing(item.GetProperty("userqueryid").GetString()!, item.GetProperty("returnedtypecode").GetString() ?? ""));
        }
        return list;
    }

    private async Task<string> CreateAsync(Guid caller, string name, ViewDefinition view, string layout, CancellationToken ct)
    {
        var body = JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["name"] = name,
            ["returnedtypecode"] = view.Entity,
            ["querytype"] = 0,
            ["fetchxml"] = view.FetchXml,
            ["layoutxml"] = layout,
        });
        using var created = await _client.SendJsonAsync(HttpMethod.Post, "userqueries", body, caller, ct);
        return created!.RootElement.GetProperty("userqueryid").GetString()!;
    }

    private async Task DeleteByIdAsync(Guid caller, string id, CancellationToken ct)
    {
        await _client.SendJsonAsync(HttpMethod.Delete, $"userqueries({id})", null, caller, ct);
    }

    private async Task<int> ObjectTypeCodeAsync(string entity, CancellationToken ct)
    {
        await _typeCodeGate.WaitAsync(ct);
        try
        {
            if (_typeCodes.TryGetValue(entity, out var known))
            {
                return known;
            }
            using var doc = await _client.GetAsServiceAsync($"EntityDefinitions(LogicalName='{entity.Replace("'", "''")}')?$select=ObjectTypeCode", ct);
            var code = doc.RootElement.GetProperty("ObjectTypeCode").GetInt32();
            _typeCodes[entity] = code;
            return code;
        }
        finally
        {
            _typeCodeGate.Release();
        }
    }
}
