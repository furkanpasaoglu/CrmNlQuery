namespace CrmNlQuery.Application.Glossary.Models;

/// <summary>
/// Describes one glossary import entry that was rejected and why.
/// </summary>
public sealed record GlossaryImportRejected(int Index, string Term, string Reason);
