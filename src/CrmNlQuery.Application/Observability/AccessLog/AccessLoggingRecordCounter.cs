using CrmNlQuery.Application.Counting.Abstractions;
using CrmNlQuery.Application.Counting.Models;
using CrmNlQuery.Application.Observability.Abstractions;
using CrmNlQuery.Application.Observability.Services;
using CrmNlQuery.Domain.Access.Entities;

namespace CrmNlQuery.Application.Observability.AccessLog;

/// <summary>
/// Record counter wrapper that logs total-count queries as reads with source count and rows equal to the counted total, without reading row contents.
/// </summary>
public sealed class AccessLoggingRecordCounter : IRecordCounter
{
    private readonly IRecordCounter _inner;
    private readonly AccessLogWriter _writer;

    public AccessLoggingRecordCounter(IRecordCounter inner, IAccessLogStore store, Func<DateTimeOffset>? clock = null)
    {
        _inner = inner;
        _writer = new AccessLogWriter(store, clock);
    }

    /// <summary>
    /// Gets the aggregate count for access logging record counter.
    /// </summary>
    public async Task<long> AggregateCountAsync(string entitySetName, string aggregateFetchXml, Caller caller, CancellationToken ct = default)
    {
        RequestContext.SetStage("crm");
        long count;
        try
        {
            count = await _inner.AggregateCountAsync(entitySetName, aggregateFetchXml, caller, ct);
        }
        catch (Exception ex)
        {
            await _writer.WriteAsync(caller, FetchXmlTables.From(aggregateFetchXml), 0, AccessLogWriter.OutcomeOf(ex), "count");
            throw;
        }
        await _writer.WriteAsync(caller, FetchXmlTables.From(aggregateFetchXml), (int)Math.Clamp(count, 0, int.MaxValue), "ok", "count");
        return count;
    }

    /// <summary>
    /// Gets the total count for access logging record counter.
    /// </summary>
    public async Task<RecordCount> TotalCountAsync(string entitySetName, string totalCountFetchXml, Caller caller, CancellationToken ct = default)
    {
        RequestContext.SetStage("crm");
        RecordCount total;
        try
        {
            total = await _inner.TotalCountAsync(entitySetName, totalCountFetchXml, caller, ct);
        }
        catch (Exception ex)
        {
            await _writer.WriteAsync(caller, FetchXmlTables.From(totalCountFetchXml), 0, AccessLogWriter.OutcomeOf(ex), "count");
            throw;
        }
        await _writer.WriteAsync(caller, FetchXmlTables.From(totalCountFetchXml), (int)Math.Clamp(total.Total, 0, int.MaxValue), "ok", "count");
        return total;
    }
}
