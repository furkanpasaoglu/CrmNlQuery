using CrmNlQuery.Application.Counting.Abstractions;
using CrmNlQuery.Application.Counting.Models;
using CrmNlQuery.Application.History.Abstractions;
using CrmNlQuery.Domain.Access.Entities;
using CrmNlQuery.Application.Common.Exceptions;

namespace CrmNlQuery.Application.Counting.Services;

/// <summary>
/// Counts the total records of a truncated result for its owner, using aggregation first and the CRM total count as fallback.
/// </summary>
public sealed class ResultCountService
{
    private readonly IHistoryStore _history;
    private readonly CountQueryBuilder _builder;
    private readonly IRecordCounter _counter;
    private readonly Func<TimeSpan> _timeout;

    public ResultCountService(IHistoryStore history, CountQueryBuilder builder, IRecordCounter counter, Func<TimeSpan> timeout)
    {
        _history = history;
        _builder = builder;
        _counter = counter;
        _timeout = timeout;
    }

    /// <summary>
    /// Counts the total records for a stored result owned by the caller.
    /// </summary>
    public async Task<CountResult> CountAsync(Guid owner, string resultId, CancellationToken ct = default)
    {
        var entry = await _history.GetAsync(owner, resultId, ct);
        if (entry is null || entry.Kind is not null)
        {
            return new CountResult(CountStatus.NotFound);
        }

        CountBuildResult built;
        try
        {
            built = await _builder.BuildAsync(entry.FetchXml, ct);
        }
        catch (CrmException)
        {
            return new CountResult(CountStatus.Unavailable);
        }
        if (!built.Ok)
        {
            return new CountResult(CountStatus.NotEligible);
        }

        using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limit.CancelAfter(_timeout());
        var queries = built.Queries!;
        try
        {
            try
            {
                return new CountResult(CountStatus.Ok, await _counter.AggregateCountAsync(queries.EntitySetName, queries.AggregateFetchXml, Caller.User(owner), limit.Token), false, queries.EntityLabel);
            }
            catch (CrmException)
            {
            }
            var total = await _counter.TotalCountAsync(queries.EntitySetName, queries.TotalCountFetchXml, Caller.User(owner), limit.Token);
            return new CountResult(CountStatus.Ok, total.Total, total.LimitExceeded, queries.EntityLabel);
        }
        catch (CrmException)
        {
            return new CountResult(CountStatus.Unavailable);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return new CountResult(CountStatus.Unavailable);
        }
    }
}
