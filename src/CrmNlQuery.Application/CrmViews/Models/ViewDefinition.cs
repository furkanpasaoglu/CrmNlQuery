namespace CrmNlQuery.Application.CrmViews.Models;

/// <summary>
/// Holds the personal view contents to write to the CRM, with paging attributes removed and filters, links and sorting kept.
/// </summary>
public sealed record ViewDefinition(string Entity, string FetchXml, IReadOnlyList<string> Columns, string PrimaryIdAttribute, string PrimaryNameAttribute);
