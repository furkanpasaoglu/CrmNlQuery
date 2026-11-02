using CrmNlQuery.Domain.Access.Entities;

namespace CrmNlQuery.Domain.Access.Constants;

/// <summary>
/// Fixed code-defined list of permissions that roles are built from.
/// </summary>
public static class Permissions
{
    public const string DashboardView = "Dashboard.View";
    public const string LogsView = "Logs.View";
    public const string UsersManage = "Users.Manage";
    public const string RolesManage = "Roles.Manage";
    public const string AuditView = "Audit.View";
    public const string MetadataRefresh = "Metadata.Refresh";
    public const string DocumentsManage = "Documents.Manage";
    public const string SettingsManage = "Settings.Manage";
    public const string AppUse = "App.Use";
    public const string AccessLogView = "AccessLog.View";
    public const string QuestionsView = "Questions.View";
    public const string DataPolicyManage = "DataPolicy.Manage";
    public const string FeedbackView = "Feedback.View";
    public const string ExportRun = "Export.Run";
    public const string DesignUse = "Design.Use";
    public const string RecordsCreate = "Records.Create";
    public const string RecordsOpenInCrm = "Records.OpenInCrm";
    public const string QualityManage = "Quality.Manage";
    public const string GlossaryManage = "Glossary.Manage";
    public const string ReportsManage = "Reports.Manage";

    public static readonly IReadOnlyList<PermissionInfo> All = new List<PermissionInfo>
    {
        new PermissionInfo(DashboardView, "Overview", "View the usage dashboard"),
        new PermissionInfo(LogsView, "Overview", "View application logs"),
        new PermissionInfo(UsersManage, "Administration", "Add, edit and remove users"),
        new PermissionInfo(RolesManage, "Administration", "Edit what each role may do"),
        new PermissionInfo(AuditView, "Administration", "View the audit trail"),
        new PermissionInfo(MetadataRefresh, "Administration", "Refresh the CRM metadata cache (after entities or fields changed)"),
        new PermissionInfo(SettingsManage, "Administration", "Change runtime settings: question limits, pause asking, knowledge base and retention"),
        new PermissionInfo(DocumentsManage, "Knowledge", "Add, edit and remove the company knowledge documents the model reads"),
        new PermissionInfo(AccessLogView, "Administration", "View the data access log: who read which CRM tables and how many rows (no question text)"),
        new PermissionInfo(QuestionsView, "Administration", "Read what users asked (question text). Works only while the Access:QuestionTextView setting is on; every view is audited"),
        new PermissionInfo(DataPolicyManage, "Administration", "Choose which CRM tables and fields the chat tool may use (on top of the CRM rights); every change is written to the audit trail"),
        new PermissionInfo(FeedbackView, "Administration", "Read the questions users chose to share to improve the tool (golden set candidates; no user names, no result rows); every view is audited"),
        new PermissionInfo(ExportRun, "Administration", "Download logs as a file (CSV or JSON Lines): the audit trail, the data access log, usage and feedback counts. Needs the matching view permission too; every export is written to the audit trail with a SHA-256 of the file"),
        new PermissionInfo(DesignUse, "Administration", "Use the customization design assistant: it reads the CRM schema (table and field names only, never records) and writes a design proposal as a document. It creates nothing in the CRM. Requirements you type are sent to the language model"),
        new PermissionInfo(QualityManage, "Administration", "Build and run the acceptance set: generate cases from the schema, approve or retire them, export them. Cases hold questions and reference queries, never result rows; every change is audited"),
        new PermissionInfo(ReportsManage, "Knowledge", "Manage the report catalog: give CRM reports a company name and search words, and add reports that live outside the CRM (for example a reporting-server address). Entries hold names and addresses, never report data; every change is audited"),
        new PermissionInfo(GlossaryManage, "Knowledge", "Define the company terms the chat tool understands (for example: large company means more than 1000 employees): add, test, import and export them. Definitions hold rules, never record data; every change is audited"),
        new PermissionInfo(RecordsCreate, "Application", "Create new CRM records from the chat (for example an account). The user sees a preview and must click Create; the record is created in the user's own name. Works only while the Features:RecordCreate setting is on and only for the tables listed in Create:Tables"),
        new PermissionInfo(RecordsOpenInCrm, "Application", "Open a large result in the CRM's own list view (a personal view made for the user). Works only while the Features:OpenInCrm setting is on; the CRM's own rules, such as exporting to Excel, still apply"),
        new PermissionInfo(AppUse, "Application", "Use the chat tool"),
    };

    /// <summary>
    /// Returns whether a permission name exists in the fixed list.
    /// </summary>
    public static bool IsKnown(string key) => All.Any(p => p.Key == key);
}
