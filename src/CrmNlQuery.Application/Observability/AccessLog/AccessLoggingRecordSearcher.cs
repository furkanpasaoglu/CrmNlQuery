using CrmNlQuery.Application.Ask.Abstractions;
using CrmNlQuery.Application.Observability.Abstractions;
using CrmNlQuery.Application.Observability.Services;
using CrmNlQuery.Domain.Access.Entities;
using CrmNlQuery.Domain.Ask.Entities;
using CrmNlQuery.Domain.Metadata.Entities;

namespace CrmNlQuery.Application.Observability.AccessLog;

/// <summary>
/// Record searcher wrapper that logs name lookups as reads, recording the table, the number of candidates returned and source lookup.
/// </summary>
public sealed class AccessLoggingRecordSearcher : IRecordSearcher
{
    private readonly IRecordSearcher _inner;
    private readonly AccessLogWriter _writer;

    public AccessLoggingRecordSearcher(IRecordSearcher inner, IAccessLogStore store, Func<DateTimeOffset>? clock = null)
    {
        _inner = inner;
        _writer = new AccessLogWriter(store, clock);
    }

    /// <summary>
    /// Searches by name for access logging record searcher.
    /// </summary>
    public async Task<IReadOnlyList<RecordMatch>> SearchByNameAsync(EntityInfo entity, string text, Caller caller, CancellationToken ct = default)
    {
        RequestContext.SetStage("resolve");
        IReadOnlyList<RecordMatch> matches;
        var tables = new List<string> { (entity.LogicalName ?? "").Trim().ToLowerInvariant() };
        try
        {
            matches = await _inner.SearchByNameAsync(entity, text, caller, ct);
        }
        catch (Exception ex)
        {
            await _writer.WriteAsync(caller, tables, 0, AccessLogWriter.OutcomeOf(ex), "lookup");
            throw;
        }
        await _writer.WriteAsync(caller, tables, matches.Count, "ok", "lookup");
        return matches;
    }
}
