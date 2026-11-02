
namespace CrmNlQuery.Application.Observability.Metrics;

/// <summary>
/// Estimates model call cost from token counts and list prices.
/// </summary>
public static class CostEstimator
{
    public static readonly IReadOnlyDictionary<string, ModelPrice> Defaults = new Dictionary<string, ModelPrice>(StringComparer.OrdinalIgnoreCase)
    {
        ["claude-haiku-5-5"] = new ModelPrice(0.10, 0.50),
        ["claude-sonnet-5-5"] = new ModelPrice(2.00, 10.00),
        ["claude-opus-5-5"] = new ModelPrice(4.00, 20.00),
        ["claude-fable-5-1"] = new ModelPrice(10.00, 50.00),
    };

    /// <summary>
    /// Returns an estimated cost in dollars, zero for the free local model, or null when the model price is unknown.
    /// </summary>
    public static double? Estimate(string provider, string model, long inputTokens, long outputTokens,
        IReadOnlyDictionary<string, ModelPrice>? overrides)
    {
        ModelPrice? price = null;
        if (overrides is not null && overrides.TryGetValue(model, out var configured))
        {
            price = configured;
        }
        else if (string.Equals(provider, "Ollama", StringComparison.OrdinalIgnoreCase))
        {
            return 0.0;
        }
        else if (Defaults.TryGetValue(model, out var known))
        {
            price = known;
        }

        return price is null ? null : (inputTokens * price.InputPerMTok + outputTokens * price.OutputPerMTok) / 1_000_000.0;
    }
}
