using CrmNlQuery.Domain.Access.Constants;

namespace CrmNlQuery.Application.Export.Models;

/// <summary>
/// Defines the export datasets and the view permission each one also requires.
/// </summary>
public static class ExportDatasets
{
    public const string Audit = "audit";
    public const string AccessLog = "accesslog";
    public const string Usage = "usage";
    public const string Feedback = "feedback";
    public const string Questions = "questions";

    public static readonly IReadOnlyList<string> All = new[] { Audit, AccessLog, Usage, Feedback, Questions };

    /// <summary>
    /// Returns the viewing permission required for an export dataset.
    /// </summary>
    public static string? ViewPermission(string dataset) => dataset switch
    {
        Audit => Permissions.AuditView,
        AccessLog => Permissions.AccessLogView,
        Usage => Permissions.DashboardView,
        Feedback => Permissions.DashboardView,
        Questions => Permissions.QuestionsView,
        _ => null,
    };
}
