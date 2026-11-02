using System.Text.Json;
using CrmNlQuery.Application.Ask.Abstractions;
using CrmNlQuery.Domain.Access.Entities;
using CrmNlQuery.Domain.Ask.Entities;
using CrmNlQuery.Application.Common.Exceptions;

namespace CrmNlQuery.Infrastructure.External.Crm.Records;

/// <summary>
/// Sends validated FetchXML to the CRM Web API, serving use cases as a table executor and infrastructure as a raw JSON runner.
/// </summary>
public sealed class FetchQueryExecutor : IQueryExecutor, IFetchRunner
{
    private const int MaxUrlLength = 4000;
    private readonly CrmClient _client;

    public FetchQueryExecutor(CrmClient client)
    {
        _client = client;
    }

    /// <summary>
    /// Executes validated FetchXML as the caller and returns the result table.
    /// </summary>
    public async Task<ResultTable> ExecuteAsync(string entitySetName, string fetchXml, Caller caller, CancellationToken ct = default)
    {
        var rows = await RunAsync(entitySetName, fetchXml, caller, ct);
        return ResultTableBuilder.Build(rows, fetchXml);
    }

    /// <summary>
    /// Executes validated FetchXML as the caller and returns the raw value array as JSON.
    /// </summary>
    public async Task<JsonElement> RunAsync(string entitySetName, string fetchXml, Caller caller, CancellationToken ct = default)
    {
        var url = entitySetName + "?fetchXml=" + Uri.EscapeDataString(fetchXml);
        if (url.Length > MaxUrlLength)
        {
            throw new CrmException("The generated query is too long to send to CRM.");
        }
        using var doc = await _client.GetAsync(url, caller, ct);
        return doc.RootElement.GetProperty("value").Clone();
    }
}
