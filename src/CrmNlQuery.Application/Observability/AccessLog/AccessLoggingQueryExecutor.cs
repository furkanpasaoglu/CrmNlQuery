using CrmNlQuery.Application.Ask.Abstractions;
using CrmNlQuery.Application.Observability.Abstractions;
using CrmNlQuery.Application.Observability.Services;
using CrmNlQuery.Domain.Access.Entities;
using CrmNlQuery.Domain.Ask.Entities;

namespace CrmNlQuery.Application.Observability.AccessLog;

/// <summary>
/// Query executor wrapper that logs every read run on a user's behalf: questions, each chain step, result-page reruns and form-context reads.
/// </summary>
public sealed class AccessLoggingQueryExecutor : IQueryExecutor
{
    private readonly IQueryExecutor _inner;
    private readonly AccessLogWriter _writer;

    public AccessLoggingQueryExecutor(IQueryExecutor inner, IAccessLogStore store, Func<DateTimeOffset>? clock = null)
    {
        _inner = inner;
        _writer = new AccessLogWriter(store, clock);
    }

    /// <summary>
    /// Executes for access logging query executor.
    /// </summary>
    public async Task<ResultTable> ExecuteAsync(string entitySetName, string fetchXml, Caller caller, CancellationToken ct = default)
    {
        RequestContext.SetStage("crm");
        ResultTable table;
        try
        {
            table = await _inner.ExecuteAsync(entitySetName, fetchXml, caller, ct);
        }
        catch (Exception ex)
        {
            await _writer.WriteAsync(caller, FetchXmlTables.From(fetchXml), 0, AccessLogWriter.OutcomeOf(ex));
            throw;
        }
        await _writer.WriteAsync(caller, FetchXmlTables.From(fetchXml), table.Rows.Count, "ok");
        return table;
    }
}
