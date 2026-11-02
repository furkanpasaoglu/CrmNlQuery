namespace CrmNlQuery.Application.Counting.Models;

/// <summary>
/// Result of building count queries, holding either the queries or a rejection with a message.
/// </summary>
public sealed record CountBuildResult(CountQueries? Queries, CountRejection? Rejection, string Detail)
{
    public bool Ok => Queries is not null;

    /// <summary>
    /// Creates a successful count build result that carries the generated count queries.
    /// </summary>
    public static CountBuildResult Success(CountQueries queries) => new CountBuildResult(queries, null, "");

    /// <summary>
    /// Creates a rejected count build result with a reason and message.
    /// </summary>
    public static CountBuildResult Reject(CountRejection reason, string detail = "") => new CountBuildResult(null, reason, detail);
}
