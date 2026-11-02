
namespace CrmNlQuery.Application.Access.Models;

/// <summary>
/// Outcome of an access-administration operation: an error kind and message, plus the new role's id when a role was created.
/// </summary>
public sealed record AccessResult(AccessError Error, string Message, Guid? Id = null)
{
    public bool Ok => Error == AccessError.None;
    public static AccessResult Success { get; } = new AccessResult(AccessError.None, "");
    /// <summary>
    /// Creates a successful result for access result.
    /// </summary>
    public static AccessResult Created(Guid id) => new AccessResult(AccessError.None, "", id);
    /// <summary>
    /// Creates a failed result for for access result.
    /// </summary>
    public static AccessResult Fail(AccessError error, string message) => new AccessResult(error, message);
}
