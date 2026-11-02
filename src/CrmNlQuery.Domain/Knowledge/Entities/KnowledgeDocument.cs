namespace CrmNlQuery.Domain.Knowledge.Entities;

/// <summary>
/// A company knowledge document such as a term definition or rule, visible company-wide.
/// </summary>
public sealed record KnowledgeDocument(Guid Id, string Title, string Body, bool Enabled, string UpdatedBy, DateTimeOffset UpdatedAt);
