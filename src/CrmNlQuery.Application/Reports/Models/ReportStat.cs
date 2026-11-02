namespace CrmNlQuery.Application.Reports.Models;

/// <summary>
/// Usage counter row for one report: its name plus how many times it was shown and opened, with no question text or user.
/// </summary>
public sealed record ReportStat(string Name, int Shown, int Opened);
