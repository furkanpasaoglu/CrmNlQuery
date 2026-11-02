namespace CrmNlQuery.Application.Export.Models;

/// <summary>
/// Enumerates the file formats an admin export can be written in, CSV and JSON Lines.
/// </summary>
public enum ExportFormat
{
    Csv,
    JsonLines,
}
