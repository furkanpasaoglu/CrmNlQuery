namespace CrmNlQuery.Domain.Access.Entities;

/// <summary>
/// Represents the explicit identity attached to every CRM query; there is no null identity.
/// </summary>
public sealed class Caller : IEquatable<Caller>
{
    private static readonly Caller Evaluation = new Caller(null);

    private readonly Guid? _userId;

    private Caller(Guid? userId)
    {
        _userId = userId;
    }

    /// <summary>
    /// Creates a caller for the given CRM user id.
    /// </summary>
    public static Caller User(Guid userId)
    {
        if (userId == Guid.Empty)
        {
            throw new ArgumentException("A caller needs a real CRM user id.", nameof(userId));
        }
        return new Caller(userId);
    }

    /// <summary>
    /// Creates the evaluation caller used only by eval and tests.
    /// </summary>
    public static Caller ForEvaluation() => Evaluation;

    public bool IsEvaluation => _userId is null;

    public Guid UserId => _userId ?? throw new InvalidOperationException("The evaluation caller has no user id.");

    /// <summary>
    /// Compares this caller with another caller for equality.
    /// </summary>
    public bool Equals(Caller? other) => other is not null && _userId == other._userId;

    /// <summary>
    /// Compares this caller with an object for equality.
    /// </summary>
    public override bool Equals(object? obj) => Equals(obj as Caller);

    /// <summary>
    /// Returns the hash code of the caller.
    /// </summary>
    public override int GetHashCode() => _userId?.GetHashCode() ?? 0;

    /// <summary>
    /// Returns only the first 8 characters of the identity so the full id never leaks into logs.
    /// </summary>
    public override string ToString() => _userId is null ? "evaluation" : "user:" + _userId.Value.ToString().Substring(0, 8);
}
