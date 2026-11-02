namespace CrmNlQuery.Domain.Ask.Entities;

/// <summary>
/// Holds a matched record's id, name and optional creation time used to tell same-named records apart.
/// </summary>
public sealed record RecordMatch(Guid Id, string Name, DateTime? CreatedOn = null);
