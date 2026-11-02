namespace CrmNlQuery.Application.DataPolicy.Models;

/// <summary>
/// Represents an automatically closed sensitive field listed for administrator review.
/// </summary>
public sealed record DataPolicyReviewRow(string Entity, string EntityLabel, string Attribute, string Label);
