using System.Diagnostics;
using CrmNlQuery.Application.Ask.Abstractions;
using CrmNlQuery.Application.Common.Exceptions;

namespace CrmNlQuery.Application.Observability.Calls;

/// <summary>
/// Wraps an LLM client to record each call without changing it, used per step in eval.
/// </summary>
public sealed class RecordingLlmClient : ILlmClient
{
    private readonly ILlmClient _inner;
    private readonly LlmCallLog _log;
    private readonly string _purpose;

    public RecordingLlmClient(ILlmClient inner, LlmCallLog log, string purpose)
    {
        _inner = inner;
        _log = log;
        _purpose = purpose;
    }

    /// <summary>
    /// Completes a JSON request through the inner client while recording the call.
    /// </summary>
    public async Task<string> CompleteJsonAsync(string system, string user, string jsonSchema, CancellationToken ct = default)
    {
        return (await CompleteAsync(system, user, jsonSchema, ct)).Text;
    }

    /// <summary>
    /// Completes a request through the inner client while recording the call.
    /// </summary>
    public async Task<LlmResult> CompleteAsync(string system, string user, string jsonSchema, CancellationToken ct = default)
    {
        var chars = PromptSize.Chars(system, user, jsonSchema);
        var estimate = PromptSize.EstimateTokens(chars);
        var watch = Stopwatch.StartNew();
        try
        {
            var result = await _inner.CompleteAsync(system, user, jsonSchema, ct);
            _log.Add(new LlmCallSample(_purpose, chars, estimate, result.Usage?.InputTokens, result.Usage?.OutputTokens, watch.ElapsedMilliseconds, null,
                _log.CaptureText ? system : null, _log.CaptureText ? user : null, _log.CaptureText ? result.Text : null));
            return result;
        }
        catch (PromptTooLongException ex)
        {
            _log.Add(new LlmCallSample(_purpose, chars, estimate, ex.PromptTokens, ex.OutputTokens, watch.ElapsedMilliseconds, ex.Cause,
                _log.CaptureText ? system : null, _log.CaptureText ? user : null));
            throw;
        }
    }
}
