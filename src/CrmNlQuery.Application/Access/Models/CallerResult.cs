using CrmNlQuery.Domain.Access.Entities;

namespace CrmNlQuery.Application.Access.Models;

/// <summary>
/// Holds the resolved CRM caller to run the query as, or an error when the request must be rejected.
/// </summary>
public sealed record CallerResult(Caller? Caller, string? Error);
