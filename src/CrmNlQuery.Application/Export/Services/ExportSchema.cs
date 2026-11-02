using CrmNlQuery.Application.Export.Models;

namespace CrmNlQuery.Application.Export.Services;

/// <summary>
/// Single source of the export column dictionary used by files, the panel and docs.
/// </summary>
public static class ExportSchema
{
    private static readonly ExportColumn[] Times =
    {
        new ExportColumn("time_utc", "When it happened, ISO 8601 in UTC (ends with Z).", true),
        new ExportColumn("time_local", "The same moment in the time zone named in time_zone, ISO 8601 with its UTC offset.", true),
        new ExportColumn("time_zone", "The IANA time zone used for time_local (the browser's zone, or the server's when none was given).", true),
    };

    /// <summary>
    /// Returns the column definitions for an export dataset.
    /// </summary>
    public static IReadOnlyList<ExportColumn> For(string dataset) => dataset switch
    {
        ExportDatasets.Audit => Times.Concat(new[]
        {
            new ExportColumn("actor", "Account that did it (or 'bootstrap' for the configured first administrator).", true),
            new ExportColumn("action", "What was done, for example user.add, role.update, access.denied, export.done.", true),
            new ExportColumn("target", "What it was done to: an account, a role, a group, a table or an endpoint.", true),
            new ExportColumn("detail", "Short description of the change. Never contains question text or CRM record contents.", true),
        }).ToList(),
        ExportDatasets.AccessLog => Times.Concat(new[]
        {
            new ExportColumn("user_id", "CRM system user id of the person whose data access this was.", true),
            new ExportColumn("user_name", "Name of that user as the CRM knows it (empty when it could not be resolved).", true),
            new ExportColumn("source", "Where the read came from: ask, result, lookup, record_context, count, summary, draft or other.", true),
            new ExportColumn("tables", "CRM tables that were read, separated by semicolons. Table names only.", true),
            new ExportColumn("rows", "Number of rows returned.", false),
            new ExportColumn("outcome", "ok, crm_denied (the CRM refused) or error.", true),
            new ExportColumn("request_id", "Groups the reads that belong to the same question.", true),
        }).ToList(),
        ExportDatasets.Questions => Times.Concat(new[]
        {
            new ExportColumn("user_id", "CRM system user id of the person who asked.", true),
            new ExportColumn("user_name", "Name of that user as the CRM knows it (empty when it could not be resolved).", true),
            new ExportColumn("table", "The main CRM table the question was about.", true),
            new ExportColumn("question", "The question as typed. Only questions asked while the Access:QuestionTextView setting was on are included; they disappear when the user deletes the chat or it expires.", true),
        }).ToList(),
        ExportDatasets.Usage => new[]
        {
            new ExportColumn("date", "Calendar day in UTC (yyyy-MM-dd).", true),
            new ExportColumn("questions", "Questions asked that day.", false),
            new ExportColumn("failed", "Questions that failed.", false),
            new ExportColumn("model_calls", "Language model calls.", false),
            new ExportColumn("input_tokens", "Model input tokens.", false),
            new ExportColumn("output_tokens", "Model output tokens.", false),
        },
        ExportDatasets.Feedback => new[]
        {
            new ExportColumn("date", "Calendar day in UTC (yyyy-MM-dd).", true),
            new ExportColumn("thumbs_up", "Answers rated helpful that day.", false),
            new ExportColumn("thumbs_down", "Answers rated not helpful that day.", false),
        },
        _ => Array.Empty<ExportColumn>(),
    };
}
