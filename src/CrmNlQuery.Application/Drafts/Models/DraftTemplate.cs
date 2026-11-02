namespace CrmNlQuery.Application.Drafts.Models;

/// <summary>
/// Holds an email template with a subject and body that contain placeholders.
/// </summary>
public sealed record DraftTemplate(string Subject, string Body);
