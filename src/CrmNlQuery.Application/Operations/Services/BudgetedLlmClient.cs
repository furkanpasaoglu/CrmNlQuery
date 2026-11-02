using CrmNlQuery.Application.Ask.Abstractions;
using CrmNlQuery.Application.Operations.Abstractions;
using CrmNlQuery.Application.Common.Exceptions;

namespace CrmNlQuery.Application.Operations.Services;

/// <summary>
/// Wraps the cloud model so no call is made when the daily budget is exhausted and returned token counts are added to the counter.
/// </summary>
public sealed class BudgetedLlmClient : ILlmClient
{
    private readonly ILlmClient _inner;
    private readonly ILimitsSource _limits;
    private readonly CloudBudget _budget;

    public BudgetedLlmClient(ILlmClient inner, ILimitsSource limits, CloudBudget budget)
    {
        _inner = inner;
        _limits = limits;
        _budget = budget;
    }

    /// <summary>
    /// Calls the inner client for JSON output after checking the daily budget, and adds the tokens used.
    /// </summary>
    public async Task<string> CompleteJsonAsync(string system, string user, string jsonSchema, CancellationToken ct = default)
    {
        return (await CompleteAsync(system, user, jsonSchema, ct)).Text;
    }

    /// <summary>
    /// Calls the inner client with usage info after checking the daily budget, and adds the tokens used.
    /// </summary>
    public async Task<LlmResult> CompleteAsync(string system, string user, string jsonSchema, CancellationToken ct = default)
    {
        var limit = _limits.Current.CloudDailyTokenBudget;
        if (_budget.IsExceeded(limit))
        {
            throw new BudgetExceededException($"The daily cloud token budget ({limit}) has been reached.");
        }
        var result = await _inner.CompleteAsync(system, user, jsonSchema, ct);
        if (result.Usage is { } usage)
        {
            _budget.Add((usage.InputTokens ?? 0) + (long)(usage.OutputTokens ?? 0));
        }
        return result;
    }
}
