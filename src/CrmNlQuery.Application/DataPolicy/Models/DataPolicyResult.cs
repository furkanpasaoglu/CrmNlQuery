namespace CrmNlQuery.Application.DataPolicy.Models;

/// <summary>
/// Carries the outcome of a data policy change, either success or an error message.
/// </summary>
public sealed record DataPolicyResult(bool Ok, string? Error)
{
    public static DataPolicyResult Success { get; } = new DataPolicyResult(true, null);

    /// <summary>
    /// Creates a failed data policy result with the given error message.
    /// </summary>
    public static DataPolicyResult Fail(string error) => new DataPolicyResult(false, error);
}
