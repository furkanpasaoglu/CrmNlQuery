using CrmNlQuery.Domain.Access.Entities;

namespace CrmNlQuery.Tests.Support;

/// <summary>
/// Extension methods on the Caller type.
/// </summary>
public static class CallerExtensions
{
    /// <summary>
    /// Returns the caller's user id, or null for the evaluation caller that has no user.
    /// </summary>
    public static Guid? UserIdOrNull(this Caller caller) => caller.IsEvaluation ? null : caller.UserId;
}
