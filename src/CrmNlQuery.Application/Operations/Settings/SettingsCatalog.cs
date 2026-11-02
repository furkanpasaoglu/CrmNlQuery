using System.Globalization;
using CrmNlQuery.Application.Operations.Models;

namespace CrmNlQuery.Application.Operations.Settings;

/// <summary>
/// Holds the allow-list of settings the panel may change at runtime; secrets, connection strings and cloud permission are never included.
/// </summary>
public static class SettingsCatalog
{
    public const string AskEnabled = "Limits:AskEnabled";
    public const string KnowledgeEnabled = "Knowledge:Enabled";
    public const string KnowledgeMinSimilarity = "Knowledge:MinSimilarity";
    public const string KnowledgeTopK = "Knowledge:TopK";
    public const string KnowledgeMaxChars = "Knowledge:MaxChars";
    public const string PlanCacheMinutes = "Planning:CacheMinutes";
    public const string MetadataCacheMinutes = "Metadata:CacheMinutes";
    public const string ConversationContextMinutes = "Conversation:ContextMinutes";
    public const string HistoryRetentionDays = "History:RetentionDays";
    public const string MetricsRetentionDays = "Metrics:RetentionDays";
    public const string OpenInCrm = "Features:OpenInCrm";
    public const string DataPolicy = "Access:DataPolicy";
    public const string ShowFetchXml = "Features:ShowFetchXml";
    public const string SummaryEnabled = "Features:Summary";
    public const string UiDefaultLanguage = "Ui:DefaultLanguage";
    public const string EmailDraftEnabled = "Features:EmailDraft";
    public const string RecordCreateEnabled = "Features:RecordCreate";
    public const string CreateTables = "Create:Tables";
    public const string CreateHiddenFields = "Create:HiddenFields";
    public const string CreateMaxRows = "Create:MaxRows";
    public const string CreatePerDay = "Create:PerDay";
    public const string GlossaryEnabled = "Features:Glossary";
    public const string SummarySectionTimeoutSeconds = "Summary:SectionTimeoutSeconds";
    public const string FeedbackEnabled = "Features:Feedback";
    public const string FeedbackAllowSharing = "Feedback:AllowSharing";
    public const string FeedbackRetentionDays = "Retention:FeedbackDays";
    public const string ExportMaxRows = "Export:MaxRows";
    public const string QualityEnabled = "Features:Quality";
    public const string QualityMaxCases = "Quality:MaxCases";
    public const string GlossaryMaxEntries = "Glossary:MaxEntries";
    public const string QualityRunTimeoutMinutes = "Quality:RunTimeoutMinutes";
    public const string QualityRunRetentionDays = "Retention:QualityRunDays";
    public const string ReportsEnabled = "Features:Reports";
    public const string ReportsMaxEntries = "Reports:MaxEntries";
    public const string QualityAllowSuggestions = "Quality:AllowSuggestions";
    public const string QualityScheduleEnabled = "Quality:ScheduleEnabled";
    public const string QualityScheduleHour = "Quality:ScheduleHour";
    public const string QualityScheduleRepeat = "Quality:ScheduleRepeat";
    public const string QualityScheduleRunAs = "Quality:ScheduleRunAs";
    public const string DesignTimeoutMinutes = "Design:TimeoutMinutes";
    public const string DesignRetentionDays = "Retention:DesignDays";
    public const string CountTimeoutSeconds = "Counting:TimeoutSeconds";
    public const string AccessRequireAppUse = "Access:RequireAppUse";
    public const string QuestionTextView = "Access:QuestionTextView";
    public const string SecurityLogRetentionDays = "Retention:SecurityLogDays";

    public static readonly IReadOnlyList<string> Groups = new List<string> { "Capacity", "Content", "Knowledge", "Planning", "Conversation", "Retention", "Features", "Interface", "Access" };

    /// <summary>
    /// Tells whether a setting is written only through its own endpoint and not the generic settings endpoint.
    /// </summary>
    public static bool IsManagedByItsOwnEndpoint(string key) => key == QualityScheduleRunAs;

    /// <summary>
    /// Returns the extra permission a setting requires besides Settings.Manage, if any.
    /// </summary>
    public static string? RequiredPermission(string key)
        => key == DataPolicy ? Domain.Access.Constants.Permissions.DataPolicyManage
            : key.StartsWith("Quality:Schedule", StringComparison.Ordinal) ? Domain.Access.Constants.Permissions.QualityManage
            : null;

    public static readonly IReadOnlyList<SettingDefinition> All = new List<SettingDefinition>
    {
        new SettingDefinition(AskEnabled, SettingKind.Boolean, 0, 1, "true", "Capacity", "Allow users to ask questions. Turn off to pause asking (emergency stop)."),
        new SettingDefinition("Limits:QuestionsPerMinute", SettingKind.Integer, 1, 600, "20", "Capacity", "Questions one user may ask in any 60 seconds"),
        new SettingDefinition("Limits:QuestionsPerDay", SettingKind.Integer, 1, 100000, "500", "Capacity", "Questions one user may ask per day (UTC)"),
        new SettingDefinition("Limits:MaxConcurrentPerUser", SettingKind.Integer, 1, 20, "2", "Capacity", "Questions one user may have running at the same time"),
        new SettingDefinition("Limits:MaxConcurrentTotal", SettingKind.Integer, 1, 500, "8", "Capacity", "Questions running at the same time across all users (protects the model server)"),
        new SettingDefinition("Limits:AskDeadlineSeconds", SettingKind.Integer, 5, 900, "120", "Capacity", "Longest time one question may take, in seconds"),
        new SettingDefinition("Limits:CloudDailyTokenBudget", SettingKind.Integer, 0, 1000000000, "0", "Capacity", "Daily token budget for the cloud model (lab only); 0 = unlimited"),
        new SettingDefinition("Limits:MaxQuestionChars", SettingKind.Integer, 50, 20000, "1000", "Content", "Longest question, in characters"),
        new SettingDefinition("Limits:MaxLinkEntities", SettingKind.Integer, 0, 20, "4", "Content", "Most joins (link-entity) one generated query may have"),
        new SettingDefinition("Limits:MaxLinkDepth", SettingKind.Integer, 1, 10, "3", "Content", "Deepest nesting of joins in one generated query"),
        new SettingDefinition("Limits:MaxConditions", SettingKind.Integer, 1, 200, "25", "Content", "Most conditions one generated query may have"),
        new SettingDefinition("Limits:MaxRecordNameChars", SettingKind.Integer, 10, 1000, "120", "Content", "Longest record name sent to the model, in characters"),
        new SettingDefinition(CountTimeoutSeconds, SettingKind.Integer, 2, 120, "15", "Content", "Longest time the \"how many records in total\" count of a truncated result may take, in seconds (the result is shown immediately; the number is filled in when it arrives)"),
        new SettingDefinition(KnowledgeEnabled, SettingKind.Boolean, 0, 1, "false", "Knowledge", "Let the model read the company knowledge documents when it answers"),
        new SettingDefinition(KnowledgeMinSimilarity, SettingKind.Decimal, 0, 1, "0.5", "Knowledge", "How close a document piece must be to the question to be used (0 to 1; higher = stricter)"),
        new SettingDefinition(KnowledgeTopK, SettingKind.Integer, 1, 10, "5", "Knowledge", "Most document pieces added to one question"),
        new SettingDefinition(KnowledgeMaxChars, SettingKind.Integer, 500, 20000, "4000", "Knowledge", "Most characters of document text added to one question"),
        new SettingDefinition(MetadataCacheMinutes, SettingKind.Integer, 1, 1440, "15", "Planning", "How long the CRM table and field list is kept before it is read again, in minutes. New tables and fields reach the chat within this time (the Refresh CRM metadata button does it at once)."),
        new SettingDefinition(PlanCacheMinutes, SettingKind.Integer, 0, 10080, "720", "Planning", "How long a verified query plan is reused for the identical question, in minutes (0 = off)"),
        new SettingDefinition(ConversationContextMinutes, SettingKind.Integer, 5, 240, "30", "Conversation", "How long after an answer a follow-up question may build on it, in minutes (older answers are treated as a fresh start)"),
        new SettingDefinition(HistoryRetentionDays, SettingKind.Integer, 0, 3650, "90", "Retention", "Days to keep chat history (pinned chats are kept); users see this number in the chat window; 0 = keep forever"),
        new SettingDefinition(ShowFetchXml, SettingKind.Boolean, 0, 1, "false", "Features", "Show the raw FetchXML under every answer (for administrators and developers). Off by default: users see a plain-language explanation of the query instead."),
        new SettingDefinition(UiDefaultLanguage, SettingKind.Choice, 0, 0, "en", "Interface",
            "Language of the chat window, result page and admin panel for users who have not chosen one and whose browser language is not supported. Users can change their own language from the chat window's settings menu. en = English, tr = Turkish.",
            new List<string> { "en", "tr" }),
        new SettingDefinition(GlossaryEnabled, SettingKind.Boolean, 0, 1, "true", "Features", "Business glossary: company terms that administrators define (for example: big company means more than 1000 employees) are applied to matching questions, and the query is checked by code to include them. No record data is involved."),
        new SettingDefinition(EmailDraftEnabled, SettingKind.Boolean, 0, 1, "true", "Features", "Write email drafts for a customer or contact (for example: write a follow-up email to this customer). The model writes a template with placeholders; the values (names, counts, dates) are filled in by code from queries run as the user, and no record data goes to the model. Drafts are text only: nothing is sent and nothing is written to the CRM."),
        new SettingDefinition(RecordCreateEnabled, SettingKind.Boolean, 0, 1, "false", "Features", "Let users create new CRM records from the chat (for example: create an account named Alpine). The model only proposes the values: the system checks them, asks for missing required fields, shows a preview, and writes the record in the user's name only after the user clicks Create. Only the tables listed in Create:Tables, and only for users who hold the Records.Create permission. Needs create rights for the service account in the CRM. Off by default."),
        new SettingDefinition(CreateTables, SettingKind.Text, 0, 300, "", "Features", "The tables users may create records in, as a comma-separated list of logical names, for example: account,contact. Empty = none. A table must also be open in Data access."),
        new SettingDefinition(CreateHiddenFields, SettingKind.Text, 0, 600, "", "Features", "Fields the assistant must not fill when creating records, as a comma-separated list of table.field, for example: contact.company,account.fax. Use it for fields that duplicate a better field in your CRM (for example a second phone field that the model picks instead of the main one). It adds to the built-in list; write !table.field to show a field the built-in list hides."),
        new SettingDefinition(CreateMaxRows,SettingKind.Integer, 1, 500, "50", "Capacity", "Most records a user can create in one request (for example when pasting a list)"),
        new SettingDefinition(CreatePerDay, SettingKind.Integer, 1, 100000, "200", "Capacity", "Most records one user can create per day"),
        new SettingDefinition(SummaryEnabled, SettingKind.Boolean, 0, 1, "true", "Features", "Answer \"summarize this customer\" with a fixed customer summary (basic details, open opportunities, recent activities, open cases, contracts, orders, other related tables). Built by code from queries run as the user; no model reads any record."),
        new SettingDefinition(SummarySectionTimeoutSeconds, SettingKind.Integer, 2, 120, "15", "Content", "Longest time one section of a customer summary may take, in seconds (a slow section shows as unavailable; the others still appear)"),
        new SettingDefinition(FeedbackEnabled, SettingKind.Boolean, 0, 1, "true", "Features", "Let users rate answers with thumbs up / down and a reason. Only the rating, the reason and technical details (tables, steps, time) are kept; no question text."),
        new SettingDefinition(FeedbackAllowSharing, SettingKind.Boolean, 0, 1, "true", "Features", "Let a user who rates an answer thumbs down choose to share the question text and its explanation with administrators (golden set candidates). Never on by default for the user; off here hides the choice."),
        new SettingDefinition(OpenInCrm, SettingKind.Boolean, 0, 1, "false", "Features", "Let users open a large (truncated) result in the CRM's own list view (paging and Excel export are handled by the CRM). WRITES one personal view per user into the CRM (named \"NL Query - last result\") on that user's behalf. Off by default. Before turning it on: give the service account the personal view rights (Setup-ServiceAccount-Crm.ps1 -OpenInCrm) and give the Records.OpenInCrm permission to the roles that may use it."),
        new SettingDefinition(DataPolicy, SettingKind.Choice, 0, 0, "on", "Access",
            "The data access list (which CRM tables and fields the chat tool may use, set in Data access). on = closed tables and fields are never shown to the model or used in any query (default). off = EMERGENCY ONLY: nothing is filtered; every change of this switch is written to the audit trail.",
            new List<string> { "off", "on" }),
        new SettingDefinition(AccessRequireAppUse, SettingKind.Choice, 0, 0, "off", "Access",
            "Who may use the chat tool. off = everyone the CRM knows (today's behaviour); report = nobody is blocked, but users who would be blocked are written to the audit log with their AD groups; on = only users who hold the App.Use permission (the admin panel is never affected).",
            new List<string> { "off", "report", "on" }),
        new SettingDefinition(QuestionTextView, SettingKind.Choice, 0, 0, "off", "Access",
            "Let administrators with the Questions.View permission read what users asked (the question text). off = nobody can (default). on = only questions asked while this is on can be read, users see a notice in the chat window, and every view is written to the audit trail. Users deleting a chat also removes it here.",
            new List<string> { "off", "on" }),
        new SettingDefinition(SecurityLogRetentionDays, SettingKind.Integer, 0, 3650, "365", "Retention", "Days to keep the data access log (who read which CRM tables, how many rows; no question text); 0 = keep forever. The admin audit trail is never deleted."),
        new SettingDefinition(DesignTimeoutMinutes, SettingKind.Integer, 1, 60, "15", "Capacity", "Longest one customization design may run, in minutes. It runs in the background; a design that takes longer is stopped and marked failed."),
        new SettingDefinition(DesignRetentionDays, SettingKind.Integer, 0, 3650, "90", "Retention", "Days to keep finished customization designs (the requirement text and the result; only their owner can see them); 0 = keep forever."),
        new SettingDefinition(QualityEnabled, SettingKind.Boolean, 0, 1, "true", "Features", "Quality check in the admin panel: build an acceptance set (cases generated from the CRM schema or saved from real answers) and check that the tool still answers them correctly. Cases hold questions and reference queries, never result rows."),
        new SettingDefinition(ReportsMaxEntries, SettingKind.Integer, 10, 2000, "200", "Capacity", "Most entries the report catalog may hold."),
        new SettingDefinition(GlossaryMaxEntries, SettingKind.Integer, 10, 5000, "500", "Capacity", "Most entries the business glossary may hold."),
        new SettingDefinition(QualityMaxCases, SettingKind.Integer, 10, 5000, "500", "Capacity", "Most cases the acceptance set may hold. Generating stops when the limit is reached."),
        new SettingDefinition(QualityRunTimeoutMinutes, SettingKind.Integer, 5, 1440, "240", "Capacity", "Longest one quality check run may take, in minutes. It runs in the background; a run that takes longer is stopped and marked failed."),
        new SettingDefinition(ReportsEnabled, SettingKind.Boolean, 0, 1, "true", "Features", "Let users find and open CRM reports from the chat (for example \"open the Neglected Cases report\"). The report opens in the CRM's own report viewer; the chat never sees report data, and each user only gets the reports the CRM lets them see. It does nothing until the report table is opened in Data access and the service account may read it."),
        new SettingDefinition(QualityAllowSuggestions, SettingKind.Boolean, 0, 1, "true", "Features", "Let users suggest one of their own answers as a good test example (a thumbs-up, then \"Suggest as a good example\"). The question and the query that answered it become a PENDING case on the Quality check page; it does not run or count until an administrator approves it. Nobody's name is stored. Result rows are never shared. Needs the Quality check feature to be on."),
        new SettingDefinition(QualityScheduleEnabled, SettingKind.Boolean, 0, 1, "false", "Features", "Run the quality check (acceptance set) by itself every day. It runs with the CRM rights of the administrator who saved the schedule. Changing it needs the Quality check permission too. Use the schedule card on the Quality check page."),
        new SettingDefinition(QualityScheduleHour, SettingKind.Integer, 0, 23, "3", "Features", "Hour of the day (server local time) when the scheduled quality check starts. It may start up to 3 hours later; a missed day is skipped."),
        new SettingDefinition(QualityScheduleRepeat, SettingKind.Integer, 1, 3, "1", "Features", "How many times each case is asked in a scheduled quality check (more repeats find unstable answers, but take longer)."),
        new SettingDefinition(QualityScheduleRunAs, SettingKind.Text, 0, 100, "", "Features", "The Windows account (DOMAIN\\user) whose CRM rights the scheduled quality check uses. Filled in automatically with the administrator who saves the schedule."),
        new SettingDefinition(QualityRunRetentionDays, SettingKind.Integer, 0, 3650, "180", "Retention", "Days to keep finished quality check runs and their per-case results (questions and verdicts, never result rows); 0 = keep forever."),
        new SettingDefinition(ExportMaxRows, SettingKind.Integer, 1000, 10000000, "1000000", "Capacity", "Most rows one log export file may contain. A larger range stops at this many rows (the audit trail says so); narrow the date range or filters to get the rest."),
        new SettingDefinition(FeedbackRetentionDays, SettingKind.Integer, 0, 3650, "180", "Retention", "Days to keep the questions users chose to share (golden set candidates); 0 = keep forever. A shared question also goes away when the user deletes the chat or withdraws it, and when the chat itself is removed by History:RetentionDays (pinned chats are kept)."),
        new SettingDefinition(MetricsRetentionDays, SettingKind.Integer, 0, 3650, "90", "Retention", "Days to keep usage metrics and application logs; 0 = keep forever"),
    };

    /// <summary>
    /// Finds a setting definition by key, or null if it is not in the catalog.
    /// </summary>
    public static SettingDefinition? Find(string key)
        => All.FirstOrDefault(d => string.Equals(d.Key, key, StringComparison.Ordinal));

    /// <summary>
    /// Validates a raw value against a setting definition and returns its normalized form or an error.
    /// </summary>
    public static (bool Ok, string Value, string? Error) Validate(SettingDefinition definition, string? raw)
    {
        var text = (raw ?? "").Trim();
        switch (definition.Kind)
        {
            case SettingKind.Boolean:
                return bool.TryParse(text, out var flag)
                    ? (true, flag ? "true" : "false", null)
                    : (false, "", $"{definition.Key} must be true or false.");
            case SettingKind.Text:
                if (definition.Key == QualityScheduleRunAs)
                {
                    return text.Length <= definition.Max && System.Text.RegularExpressions.Regex.IsMatch(text, @"^([A-Za-z0-9_.\-]+\\)?[A-Za-z0-9_.\-@$]+$|^$")
                        ? (true, text, null)
                        : (false, "", $"{definition.Key} must be a Windows account like DOMAIN\\user, at most {definition.Max.ToString("0", CultureInfo.InvariantCulture)} characters.");
                }
                if (text.Length > definition.Max || !System.Text.RegularExpressions.Regex.IsMatch(text, @"^[A-Za-z0-9_,.!\s]*$"))
                {
                    return (false, "", $"{definition.Key} must be a comma-separated list (letters, digits and underscores; for fields table.field, optionally starting with !), at most {definition.Max.ToString("0", CultureInfo.InvariantCulture)} characters.");
                }
                var names = text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(n => n.ToLowerInvariant()).Distinct().ToList();
                return (true, string.Join(",", names), null);
            case SettingKind.Choice:
                var choice = (definition.Choices ?? new List<string>()).FirstOrDefault(c => string.Equals(c, text, StringComparison.OrdinalIgnoreCase));
                return choice is not null
                    ? (true, choice, null)
                    : (false, "", $"{definition.Key} must be one of: {string.Join(", ", definition.Choices ?? new List<string>())}.");
            case SettingKind.Decimal:
                if (!decimal.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var number))
                {
                    return (false, "", $"{definition.Key} must be a number with a dot as the decimal separator, for example 0.5.");
                }
                return InRange(definition, number) ? (true, number.ToString("0.##", CultureInfo.InvariantCulture), null) : OutOfRange(definition);
            default:
                if (!long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var whole))
                {
                    return (false, "", $"{definition.Key} must be a whole number.");
                }
                return InRange(definition, whole) ? (true, whole.ToString(CultureInfo.InvariantCulture), null) : OutOfRange(definition);
        }
    }

    private static bool InRange(SettingDefinition d, decimal value) => value >= d.Min && value <= d.Max;

    private static (bool, string, string?) OutOfRange(SettingDefinition d)
        => (false, "", $"{d.Key} must be between {d.Min.ToString("0.##", CultureInfo.InvariantCulture)} and {d.Max.ToString("0.##", CultureInfo.InvariantCulture)}.");

    /// <summary>
    /// Applies a validated Limits setting value to a guardrail limits record and returns the updated record.
    /// </summary>
    public static GuardrailLimits Apply(GuardrailLimits limits, string key, string value)
    {
        long Number() => long.Parse(value, CultureInfo.InvariantCulture);
        return key switch
        {
            AskEnabled => limits with { AskEnabled = value == "true" },
            "Limits:QuestionsPerMinute" => limits with { QuestionsPerMinute = (int)Number() },
            "Limits:QuestionsPerDay" => limits with { QuestionsPerDay = (int)Number() },
            "Limits:MaxConcurrentPerUser" => limits with { MaxConcurrentPerUser = (int)Number() },
            "Limits:MaxConcurrentTotal" => limits with { MaxConcurrentTotal = (int)Number() },
            "Limits:AskDeadlineSeconds" => limits with { AskDeadlineSeconds = (int)Number() },
            "Limits:CloudDailyTokenBudget" => limits with { CloudDailyTokenBudget = Number() },
            "Limits:MaxQuestionChars" => limits with { MaxQuestionChars = (int)Number() },
            "Limits:MaxLinkEntities" => limits with { MaxLinkEntities = (int)Number() },
            "Limits:MaxLinkDepth" => limits with { MaxLinkDepth = (int)Number() },
            "Limits:MaxConditions" => limits with { MaxConditions = (int)Number() },
            "Limits:MaxRecordNameChars" => limits with { MaxRecordNameChars = (int)Number() },
            _ => limits,
        };
    }

    /// <summary>
    /// Reads the current value of a Limits setting from a guardrail limits record as text.
    /// </summary>
    public static string Read(GuardrailLimits limits, string key) => key switch
    {
        AskEnabled => limits.AskEnabled ? "true" : "false",
        "Limits:QuestionsPerMinute" => limits.QuestionsPerMinute.ToString(CultureInfo.InvariantCulture),
        "Limits:QuestionsPerDay" => limits.QuestionsPerDay.ToString(CultureInfo.InvariantCulture),
        "Limits:MaxConcurrentPerUser" => limits.MaxConcurrentPerUser.ToString(CultureInfo.InvariantCulture),
        "Limits:MaxConcurrentTotal" => limits.MaxConcurrentTotal.ToString(CultureInfo.InvariantCulture),
        "Limits:AskDeadlineSeconds" => limits.AskDeadlineSeconds.ToString(CultureInfo.InvariantCulture),
        "Limits:CloudDailyTokenBudget" => limits.CloudDailyTokenBudget.ToString(CultureInfo.InvariantCulture),
        "Limits:MaxQuestionChars" => limits.MaxQuestionChars.ToString(CultureInfo.InvariantCulture),
        "Limits:MaxLinkEntities" => limits.MaxLinkEntities.ToString(CultureInfo.InvariantCulture),
        "Limits:MaxLinkDepth" => limits.MaxLinkDepth.ToString(CultureInfo.InvariantCulture),
        "Limits:MaxConditions" => limits.MaxConditions.ToString(CultureInfo.InvariantCulture),
        "Limits:MaxRecordNameChars" => limits.MaxRecordNameChars.ToString(CultureInfo.InvariantCulture),
        _ => "",
    };

    /// <summary>
    /// Tells whether a setting key belongs to the Limits group.
    /// </summary>
    public static bool IsLimitKey(string key) => key.StartsWith("Limits:", StringComparison.Ordinal);
}
