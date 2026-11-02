namespace CrmNlQuery.Application.Export.Models;

/// <summary>
/// Describes one export column with its name and description.
/// </summary>
public sealed record ExportColumn(string Name, string Description, bool IsText);
