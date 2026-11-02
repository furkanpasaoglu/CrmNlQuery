namespace CrmNlQuery.Application.Setup.Models;

/// <summary>
/// Outcome of a wizard step: a value, or a short error code (and the field it is about) that the page translates.
/// </summary>
public sealed record SetupResult<T>(T? Value, string? Error = null, string? Field = null)
{
    public bool Ok => Error is null;

    /// <summary>
    /// Creates a successful result.
    /// </summary>
    public static SetupResult<T> Success(T value) => new SetupResult<T>(value);

    /// <summary>
    /// Creates a failed result with an error code and optionally the field it concerns.
    /// </summary>
    public static SetupResult<T> Failure(string error, string? field = null) => new SetupResult<T>(default, error, field);
}
