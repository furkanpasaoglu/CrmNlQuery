using System.Text.Json;
using CrmNlQuery.Application.Access.Abstractions;
using CrmNlQuery.Application.Access.Models;
using CrmNlQuery.Application.Access.Services;

namespace CrmNlQuery.Infrastructure.External.Crm;

/// <summary>
/// Reads the sign-in proof task a browser created in the CRM, with the service account, limited to the creator and change fields of that one task.
/// </summary>
public sealed class CrmLoginProofReader : ICrmProofReader
{
    private readonly CrmClient _client;

    public CrmLoginProofReader(CrmClient client)
    {
        _client = client;
    }

    /// <summary>
    /// Finds the single task whose subject carries the nonce and returns who created it and whether it was changed afterwards, or null when none or more than one matches.
    /// </summary>
    public async Task<CrmProof?> ReadAsync(string nonce, CancellationToken ct = default)
    {
        var filter = $"subject eq '{CrmLoginService.SubjectPrefix}{nonce}'";
        var url = "tasks?$select=activityid,createdon,modifiedon,_createdby_value,_modifiedby_value,_createdonbehalfby_value,_modifiedonbehalfby_value&$filter=" + Uri.EscapeDataString(filter) + "&$top=2";
        using var doc = await _client.GetAsServiceAsync(url, ct);
        return Parse(doc.RootElement);
    }

    /// <summary>
    /// Turns the CRM answer into a proof: exactly one row is required, and a record counts as changed unless its timestamps and editor show it was never touched after creation.
    /// </summary>
    public static CrmProof? Parse(JsonElement root)
    {
        if (!root.TryGetProperty("value", out var rows) || rows.ValueKind != JsonValueKind.Array || rows.GetArrayLength() != 1)
        {
            return null;
        }
        var row = rows[0];
        var createdBy = Text(row, "_createdby_value");
        var modifiedBy = Text(row, "_modifiedby_value");
        var createdOn = Text(row, "createdon");
        var modifiedOn = Text(row, "modifiedon");
        Guid? createdById = Guid.TryParse(createdBy, out var id) ? id : null;
        var onBehalf = !string.IsNullOrEmpty(Text(row, "_createdonbehalfby_value"));
        var modifiedAfterCreation = string.IsNullOrEmpty(createdOn) || createdOn != modifiedOn
            || string.IsNullOrEmpty(createdBy) || createdBy != modifiedBy
            || !string.IsNullOrEmpty(Text(row, "_modifiedonbehalfby_value"));
        return new CrmProof(Text(row, "activityid") ?? "", createdById, onBehalf, modifiedAfterCreation);
    }

    private static string? Text(JsonElement row, string name)
    {
        return row.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    }
}
