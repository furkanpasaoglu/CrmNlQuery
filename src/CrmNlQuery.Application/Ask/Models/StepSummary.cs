namespace CrmNlQuery.Application.Ask.Models;

/// <summary>
/// Short summary of one chain step (number, question, row count, columns, up to five preview rows, optional record names) with no ids.
/// </summary>
public sealed record StepSummary(int Number, string Question, int Rows, IReadOnlyList<string> Columns, IReadOnlyList<IReadOnlyList<string>> Preview, IReadOnlyList<string>? Names = null);
