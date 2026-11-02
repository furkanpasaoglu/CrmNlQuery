namespace CrmNlQuery.Application.Observability.Metrics;

/// <summary>
/// Holds a model's input and output price in dollars per million tokens, as a list-price estimate.
/// </summary>
public sealed record ModelPrice(double InputPerMTok, double OutputPerMTok);
