namespace CrmNlQuery.Application.Glossary.Models;

/// <summary>
/// Report of a glossary import listing accepted and rejected entries.
/// </summary>
public sealed record GlossaryImportReport(int Total, int Accepted, IReadOnlyList<GlossaryImportRejected> Rejected, bool Applied);
