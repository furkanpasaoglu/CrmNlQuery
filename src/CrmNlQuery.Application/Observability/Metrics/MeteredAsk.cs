using System.Diagnostics;
using CrmNlQuery.Application.Observability.AccessLog;
using CrmNlQuery.Application.Ask.Models;
using CrmNlQuery.Application.Observability.Abstractions;
using CrmNlQuery.Application.Observability.Services;

namespace CrmNlQuery.Application.Observability.Metrics;

/// <summary>
/// Wraps the ask pipeline to give each request a correlation id and record its duration and outcome kind.
/// </summary>
public sealed class MeteredAsk
{
    private readonly Func<AskRequest, CancellationToken, Task<AskOutcome>> _run;
    private readonly IMetricsStore _store;
    private readonly Func<DateTimeOffset> _clock;

    public MeteredAsk(Func<AskRequest, CancellationToken, Task<AskOutcome>> run, IMetricsStore store, Func<DateTimeOffset>? clock = null)
    {
        _run = run;
        _store = store;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    /// <summary>
    /// Runs the ask pipeline and records the request's duration, outcome and failure kind.
    /// </summary>
    public async Task<AskOutcome> RunAsync(AskRequest request, CancellationToken ct)
    {
        var id = Guid.NewGuid();
        var watch = Stopwatch.StartNew();
        using var scope = RequestContext.Begin(id);
        using var accessScope = AccessLogContext.Begin("ask", id);
        AskOutcome outcome;
        try
        {
            outcome = await _run(request, ct);
        }
        catch (OperationCanceledException)
        {
            if (!RequestContext.DeadlineExpired)
            {
                await RecordAsync(id, request, watch.ElapsedMilliseconds, "cancelled", RequestContext.Stage);
            }
            throw;
        }
        catch (Exception)
        {
            await RecordAsync(id, request, watch.ElapsedMilliseconds, "failure", "unexpected");
            throw;
        }

        switch (outcome)
        {
            case AnswerOutcome answer:
                await RecordAsync(id, request, watch.ElapsedMilliseconds, "answer", null, answer.BasedOn is not null);
                outcome = answer with { DurationMs = (int)Math.Min(int.MaxValue, watch.ElapsedMilliseconds) };
                break;
            case SummaryOutcome or DraftOutcome:
                await RecordAsync(id, request, watch.ElapsedMilliseconds, "answer", null);
                break;
            case QuestionOutcome:
                await RecordAsync(id, request, watch.ElapsedMilliseconds, "question", null);
                break;
            case FailureOutcome failure:
                await RecordAsync(id, request, watch.ElapsedMilliseconds, "failure", failure.Kind);
                break;
        }
        return outcome;
    }

    private async Task RecordAsync(Guid id, AskRequest request, long milliseconds, string outcome, string? errorType, bool? followUp = null)
    {
        try
        {
            await _store.RecordRequestAsync(new RequestRecord(id, _clock(), request.Caller.IsEvaluation ? null : request.Caller.UserId, milliseconds, outcome, errorType, RequestContext.KnowledgePieces, followUp));
        }
        catch (Exception)
        {
        }
    }
}
