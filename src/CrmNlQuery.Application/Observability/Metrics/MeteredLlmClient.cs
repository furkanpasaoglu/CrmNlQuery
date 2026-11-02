using System.Diagnostics;
using Microsoft.Extensions.Logging;
using CrmNlQuery.Application.Observability.Calls;
using CrmNlQuery.Application.Ask.Abstractions;
using CrmNlQuery.Application.Observability.Abstractions;
using CrmNlQuery.Application.Observability.Services;
using CrmNlQuery.Application.Common.Exceptions;

namespace CrmNlQuery.Application.Observability.Metrics;

/// <summary>
/// LLM client wrapper that records each call's duration, token counts and outcome per purpose without changing the answer or failing the call if recording fails.
/// </summary>
public sealed class MeteredLlmClient : ILlmClient
{
    private readonly ILlmClient _inner;
    private readonly IMetricsStore _store;
    private readonly string _purpose;
    private readonly string _provider;
    private readonly string _model;
    private readonly Func<DateTimeOffset> _clock;
    private readonly ILogger? _logger;

    public MeteredLlmClient(ILlmClient inner, IMetricsStore store, string purpose, string provider, string model,
        Func<DateTimeOffset>? clock = null, ILogger? logger = null)
    {
        _logger = logger;
        _inner = inner;
        _store = store;
        _purpose = purpose;
        _provider = provider;
        _model = model;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    /// <summary>
    /// Handles complete json for metered llm client.
    /// </summary>
    public async Task<string> CompleteJsonAsync(string system, string user, string jsonSchema, CancellationToken ct = default)
    {
        return (await CompleteAsync(system, user, jsonSchema, ct)).Text;
    }

    /// <summary>
    /// Handles complete for metered llm client.
    /// </summary>
    public async Task<LlmResult> CompleteAsync(string system, string user, string jsonSchema, CancellationToken ct = default)
    {
        RequestContext.SetStage(_purpose);
        var watch = Stopwatch.StartNew();
        var promptChars = PromptSize.Chars(system, user, jsonSchema);
        try
        {
            var result = await _inner.CompleteAsync(system, user, jsonSchema, ct);
            await RecordAsync(watch.ElapsedMilliseconds, result.Usage, "ok", null, promptChars);
            return result;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (BudgetExceededException)
        {
            throw;
        }
        catch (PromptTooLongException ex)
        {
            await RecordAsync(watch.ElapsedMilliseconds, new LlmUsage(ex.PromptTokens, ex.OutputTokens), "error", "context_overflow_" + ex.Cause, promptChars);
            throw;
        }
        catch (LlmUnavailableException)
        {
            await RecordAsync(watch.ElapsedMilliseconds, null, "error", "unavailable", promptChars);
            throw;
        }
        catch (Exception)
        {
            await RecordAsync(watch.ElapsedMilliseconds, null, "error", "other", promptChars);
            throw;
        }
    }

    private async Task RecordAsync(long milliseconds, LlmUsage? usage, string outcome, string? errorType, int promptChars)
    {
        var estTokens = PromptSize.EstimateTokens(promptChars);
        try
        {
            _logger?.LogInformation("Model call {Purpose} ({Provider}/{Model}): prompt {PromptChars} chars (~{EstTokens} tokens), tokens in {InputTokens} out {OutputTokens}, {Milliseconds} ms, {Outcome}{ErrorType}",
                _purpose, _provider, _model, promptChars, estTokens, usage?.InputTokens, usage?.OutputTokens, milliseconds, outcome, errorType is null ? "" : " (" + errorType + ")");
            await _store.RecordAiCallAsync(new AiCallRecord(RequestContext.Current, _clock(), _purpose, _provider, _model,
                milliseconds, usage?.InputTokens, usage?.OutputTokens, outcome, errorType, promptChars, estTokens));
        }
        catch (Exception)
        {
        }
    }
}
