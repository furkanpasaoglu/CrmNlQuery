namespace CrmNlQuery.Application.CrmViews.Models;

/// <summary>
/// Holds the result of building a view, either the view or a rejection with detail.
/// </summary>
public sealed record ViewBuildResult(ViewDefinition? View, ViewRejection? Rejection, string Detail)
{
    public bool Ok => View is not null;

    /// <summary>
    /// Creates a successful view build result that carries the built view definition.
    /// </summary>
    public static ViewBuildResult Success(ViewDefinition view) => new ViewBuildResult(view, null, "");

    /// <summary>
    /// Creates a rejected view build result with a reason and detail.
    /// </summary>
    public static ViewBuildResult Reject(ViewRejection reason, string detail = "") => new ViewBuildResult(null, reason, detail);
}
