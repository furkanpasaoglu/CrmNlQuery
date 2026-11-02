using System.Text.Json;
using CrmNlQuery.Application.Counting.Abstractions;
using CrmNlQuery.Application.Counting.Models;
using CrmNlQuery.Domain.Access.Entities;
using CrmNlQuery.Application.Common.Exceptions;

namespace CrmNlQuery.Infrastructure.External.Crm.Records;

/// <summary>
/// CRM implementation of the record counter: runs derived count queries as the user via impersonation, read-only.
/// </summary>
public sealed class CrmRecordCounter : IRecordCounter
{
    private const int MaxUrlLength = 4000;
    private const string TotalAnnotation = "@Microsoft.Dynamics.CRM.totalrecordcount";
    private const string ExceededAnnotation = "@Microsoft.Dynamics.CRM.totalrecordcountlimitexceeded";

    private readonly CrmClient _client;

    public CrmRecordCounter(CrmClient client)
    {
        _client = client;
    }

    /// <summary>
    /// Gets the aggregate count for crm record counter.
    /// </summary>
    public async Task<long> AggregateCountAsync(string entitySetName, string aggregateFetchXml, Caller caller, CancellationToken ct = default)
    {
        using var doc = await GetAsync(entitySetName, aggregateFetchXml, caller, ct);
        var rows = doc.RootElement.GetProperty("value");
        if (rows.GetArrayLength() == 0 || !rows[0].TryGetProperty("n", out var n) || n.ValueKind != JsonValueKind.Number || !n.TryGetInt64(out var count))
        {
            throw new CrmException("CRM returned no count.");
        }
        return count;
    }

    /// <summary>
    /// Gets the total count for crm record counter.
    /// </summary>
    public async Task<RecordCount> TotalCountAsync(string entitySetName, string totalCountFetchXml, Caller caller, CancellationToken ct = default)
    {
        using var doc = await GetAsync(entitySetName, totalCountFetchXml, caller, ct);
        var root = doc.RootElement;
        if (!root.TryGetProperty(TotalAnnotation, out var total) || total.ValueKind != JsonValueKind.Number || !total.TryGetInt64(out var count) || count < 0)
        {
            throw new CrmException("CRM returned no total record count.");
        }
        var exceeded = root.TryGetProperty(ExceededAnnotation, out var flag) && flag.ValueKind == JsonValueKind.True;
        return new RecordCount(count, exceeded);
    }

    private Task<JsonDocument> GetAsync(string entitySetName, string fetchXml, Caller caller, CancellationToken ct)
    {
        var url = entitySetName + "?fetchXml=" + Uri.EscapeDataString(fetchXml);
        if (url.Length > MaxUrlLength)
        {
            throw new CrmException("The count query is too long to send to CRM.");
        }
        return _client.GetAsync(url, caller, ct);
    }
}
